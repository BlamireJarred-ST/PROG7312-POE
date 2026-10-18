using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Manual override command history.
// DATA STRUCTURE: Stack<OverrideCommand> (LIFO) - Pop() returns the most recently issued command so it can be reverted.
// A separate list keeps the full audit trail (including undo entries) for the History tab.
public sealed class CommandHistory
{
    private const int MaxAudit = 500;

    private readonly Stack<OverrideCommand> _undoStack = new();
    private readonly List<OverrideCommand> _audit = new();
    private readonly object _lock = new();

    public void Push(OverrideCommand command)
    {
        lock (_lock)
        {
            _undoStack.Push(command);
            AddAudit(command);
        }
    }

    // Pops the last issued command; false when there is nothing to undo
    public bool TryPopForUndo(out OverrideCommand? command)
    {
        lock (_lock)
        {
            return _undoStack.TryPop(out command);
        }
    }

    // Undo entries are audited but NOT pushed back on the stack
    public void RecordUndo(OverrideCommand undo)
    {
        lock (_lock) AddAudit(undo);
    }

    // Stack contents, top (most recent) first
    public List<OverrideCommand> StackSnapshot()
    {
        lock (_lock) return _undoStack.ToList();
    }

    // Audit trail, newest first
    public List<OverrideCommand> Audit(int take)
    {
        lock (_lock)
        {
            return Enumerable.Reverse(_audit).Take(take).ToList();
        }
    }

    private void AddAudit(OverrideCommand command)
    {
        _audit.Add(command);
        if (_audit.Count > MaxAudit) _audit.RemoveAt(0);
    }
}
