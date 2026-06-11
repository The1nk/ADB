using AdbCore.Models;

namespace AdbCore.Execution;

/// <summary>Binds a single bot target to a live handle on demand. Used so a nested bot can resolve its own
/// (non-shared) targets only when its card executes. Implemented outside the engine (the runner) so the engine
/// stays free of Win32/ADB/Playwright. Throws on an unresolvable selector.</summary>
public interface ITargetBinder
{
    Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct);
}
