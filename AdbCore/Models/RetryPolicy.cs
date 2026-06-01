namespace AdbCore.Models;

/// <summary>Per-action retry configuration for flaky operations.</summary>
public class RetryPolicy
{
    public int MaxAttempts { get; set; }
    public int DelayMs { get; set; }
}
