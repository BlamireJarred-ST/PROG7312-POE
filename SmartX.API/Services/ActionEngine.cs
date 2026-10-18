using SmartX.Shared.Models;
using SmartX.Shared.Services;

namespace SmartX.API.Services;

// Predictive Action and Recommendation Engine.
//
// 1. PATTERN ANALYSIS  - every alert, search and manual command is an "event token". A nested Dictionary
//    (token -> next token -> count) is a first-order transition-frequency model (a simple Markov chain).
//    Each new event is linked to the previous few events, so "Alert on Moisture B" followed by
//    "Command PumpOn on Pump A" three times becomes a strong learned link.
// 2. ALGORITHMIC SUGGESTION - when a device currently has an alert (or after the user's last action), the
//    most frequent follow-up events (support >= 2) are suggested, ranked by confidence = count / total.
// 3. HEURISTICS - problem devices are highlighted before the user searches: critical / warning / offline
//    state, a least-squares trend that projects a reading to cross its critical limit soon, and a flapping valve.
public sealed class ActionEngine
{
    private const int MinSupport = 2;
    private const int ContextWindow = 3;

    private readonly DeviceRegistry _registry;
    private readonly HistoryStore _history;

    private readonly Dictionary<string, Dictionary<string, int>> _transitions = new();
    private readonly LinkedList<string> _recent = new();
    private readonly object _lock = new();

    public ActionEngine(DeviceRegistry registry, HistoryStore history)
    {
        _registry = registry;
        _history = history;
    }

    // ---------- event recording ----------

    public void RecordAlert(string mac) => Record($"Alert:{mac}");

    public void RecordSearch(string term)
    {
        term = term.Trim().ToLowerInvariant();
        if (term.Length > 0) Record($"Search:{term}");
    }

    public void RecordCommand(string mac, CommandType type) => Record($"Command:{mac}:{type}");

    private void Record(string token)
    {
        lock (_lock)
        {
            foreach (string previous in _recent)
            {
                if (previous == token) continue;

                if (!_transitions.TryGetValue(previous, out var next))
                {
                    _transitions[previous] = next = new Dictionary<string, int>();
                }

                next[token] = next.GetValueOrDefault(token) + 1;
            }

            _recent.AddLast(token);
            if (_recent.Count > ContextWindow) _recent.RemoveFirst();
        }
    }

    // Trains the engine with a sample pattern (for demos / testing)
    public void SeedDemoPattern(string alertMac, string commandMac, CommandType command, string searchTerm, int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            RecordAlert(alertMac);
            RecordSearch(searchTerm);
            RecordCommand(commandMac, command);
        }
    }

    // ---------- suggestions ----------

    public List<Suggestion> GetSuggestions()
    {
        var devices = _registry.Snapshot();
        var byMac = devices.ToDictionary(d => d.MacAddress, StringComparer.OrdinalIgnoreCase);
        var best = new Dictionary<string, Suggestion>();

        void Add(string key, Suggestion suggestion)
        {
            if (!best.TryGetValue(key, out var existing) || suggestion.Score > existing.Score)
            {
                best[key] = suggestion;
            }
        }

        string Name(string mac) => byMac.TryGetValue(mac, out var d) ? d.NodeId : mac;

        // 1. learned patterns ------------------------------------------------
        var contexts = devices
            .Where(d => d.AlertState != "Normal")
            .Select(d => $"Alert:{d.MacAddress}")
            .ToList();

        var candidates = new List<(string Context, string Token, int Count, int Total)>();

        lock (_lock)
        {
            if (_recent.Last is not null) contexts.Add(_recent.Last.Value);

            foreach (string context in contexts.Distinct())
            {
                if (!_transitions.TryGetValue(context, out var next)) continue;

                int total = next.Values.Sum();

                foreach (var (token, count) in next)
                {
                    if (count >= MinSupport && !token.StartsWith("Alert:"))
                    {
                        candidates.Add((context, token, count, total));
                    }
                }
            }
        }

        foreach (var c in candidates)
        {
            double confidence = (double)c.Count / c.Total;

            string why = c.Context.StartsWith("Alert:")
                ? $"After alerts on {Name(c.Context["Alert:".Length..])}"
                : "After your last action";

            string detail = $"{why} you did this {c.Count} of {c.Total} times ({confidence:P0}).";

            if (c.Token.StartsWith("Command:"))
            {
                // token = Command:<mac>:<type>  (the MAC itself contains colons)
                string rest = c.Token["Command:".Length..];
                int split = rest.LastIndexOf(':');
                if (split < 0) continue;

                string mac = rest[..split];

                if (!Enum.TryParse<CommandType>(rest[(split + 1)..], out var command)) continue;
                if (!byMac.TryGetValue(mac, out var device)) continue;
                if (IsNoOp(device, command)) continue;

                Add(c.Token, new Suggestion
                {
                    Kind = "Command",
                    Title = $"{command.ToLabel()} on {device.NodeId}",
                    Detail = detail,
                    Score = 60 + 40 * confidence,
                    DeviceId = device.MacAddress,
                    Command = command
                });
            }
            else if (c.Token.StartsWith("Search:"))
            {
                string term = c.Token["Search:".Length..];

                Add(c.Token, new Suggestion
                {
                    Kind = "Search",
                    Title = $"Search \"{term}\"",
                    Detail = detail,
                    Score = 45 + 40 * confidence,
                    SearchTerm = term
                });
            }
        }

        // 2. problem devices -------------------------------------------------
        foreach (var d in devices)
        {
            switch (d.AlertState)
            {
                case "Critical":
                    Add($"state:{d.MacAddress}", new Suggestion
                    {
                        Kind = "Insight",
                        Title = $"{d.NodeId} is CRITICAL",
                        Detail = $"{d.PacketType} reading {d.LastValue} in {d.Room}. Immediate attention recommended.",
                        Score = 100,
                        DeviceId = d.MacAddress
                    });
                    break;

                case "Offline":
                    Add($"state:{d.MacAddress}", new Suggestion
                    {
                        Kind = "Insight",
                        Title = $"{d.NodeId} is offline",
                        Detail = $"No telemetry since {d.LastSeenLocal:HH:mm:ss}. Check power and mesh connectivity.",
                        Score = 90,
                        DeviceId = d.MacAddress
                    });
                    break;

                case "Warning":
                    Add($"state:{d.MacAddress}", new Suggestion
                    {
                        Kind = "Insight",
                        Title = $"{d.NodeId} out of range",
                        Detail = $"{d.PacketType} reading {d.LastValue} in {d.Room} is outside the normal range.",
                        Score = 70,
                        DeviceId = d.MacAddress
                    });
                    break;
            }

            AddTrendInsights(d, Add);
        }

        return best.Values.OrderByDescending(s => s.Score).Take(8).ToList();
    }

    private void AddTrendInsights(DeviceState device, Action<string, Suggestion> add)
    {
        if (device.Status != DeviceStatus.Online || string.IsNullOrEmpty(device.PacketType)) return;

        var points = _history.Latest(device.MacAddress, 10)
            .Where(r => r.PacketType == device.PacketType)
            .TakeLast(8)
            .ToList();

        // flapping valve: opens/closes repeatedly
        if (device.PacketType == "ValveState" && points.Count >= 6)
        {
            int changes = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].DataValue != points[i - 1].DataValue) changes++;
            }

            if (changes >= 4)
            {
                add($"flap:{device.MacAddress}", new Suggestion
                {
                    Kind = "Insight",
                    Title = $"{device.NodeId} valve is flapping",
                    Detail = $"Valve state changed {changes} times in the last {points.Count} readings. Consider a manual valve override.",
                    Score = 55,
                    DeviceId = device.MacAddress
                });
            }

            return;
        }

        var limit = SeverityEvaluator.GetLimit(device.PacketType);
        if (limit is null || device.Severity == "Critical" || points.Count < 5) return;

        var ys = new List<double>();
        foreach (var p in points)
        {
            if (ValueParser.TryParseNumber(p.DataValue, out double v)) ys.Add(v);
        }
        if (ys.Count < 5) return;

        // least-squares slope per reading
        int n = ys.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += i; sumY += ys[i]; sumXY += i * ys[i]; sumXX += (double)i * i;
        }

        double denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-9) return;

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double last = ys[^1];
        double worsening = limit.HigherIsWorse ? slope : -slope; // > 0 means heading towards the critical limit

        if (worsening <= 0.01 * Math.Max(1, Math.Abs(last))) return;

        double distance = limit.HigherIsWorse ? limit.Critical - last : last - limit.Critical;
        double readingsLeft = distance / worsening;

        if (readingsLeft is > 0 and <= 10)
        {
            add($"trend:{device.MacAddress}", new Suggestion
            {
                Kind = "Insight",
                Title = $"{device.NodeId} trending towards critical",
                Detail = $"{device.PacketType} is {(limit.HigherIsWorse ? "rising" : "falling")} " +
                         $"(now {last:0.#}, critical at {limit.Critical:0.#}). Projected to cross in ~{Math.Ceiling(readingsLeft):0} readings.",
                Score = 65 + (10 - readingsLeft),
                DeviceId = device.MacAddress
            });
        }
    }

    // Do not suggest a command that would change nothing
    private static bool IsNoOp(DeviceState d, CommandType c) => c switch
    {
        CommandType.PumpOn => d.PumpOn,
        CommandType.PumpOff => !d.PumpOn,
        CommandType.RelayOn => d.RelayOn,
        CommandType.RelayOff => !d.RelayOn,
        CommandType.ValveOpen => d.ValveOpen,
        CommandType.ValveClose => !d.ValveOpen,
        _ => false
    };
}
