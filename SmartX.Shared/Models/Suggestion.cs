namespace SmartX.Shared.Models;

// A "Suggested Action" / "Automated Insight" produced by the Action Engine
public class Suggestion
{
    // Command, Search or Insight
    public string Kind { get; set; } = "Insight";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    // Higher = more important (list is sorted by this)
    public double Score { get; set; }
    public string? DeviceId { get; set; }
    public CommandType? Command { get; set; }
    public string? SearchTerm { get; set; }
}
