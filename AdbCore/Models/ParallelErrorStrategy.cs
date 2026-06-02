namespace AdbCore.Models;

/// <summary>How a Run Parallel block reacts when one of its branches fails.</summary>
public enum ParallelErrorStrategy
{
    /// <summary>Cancel all still-running sibling branches immediately on the first failure.</summary>
    HaltAll,

    /// <summary>Let all in-flight branches finish; do not cancel siblings.</summary>
    WaitThenHalt,

    /// <summary>Treat failures as warnings; never cancel, never halt the run.</summary>
    Continue,
}
