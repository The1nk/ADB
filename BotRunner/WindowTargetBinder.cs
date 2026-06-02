using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;

namespace BotRunner;

/// <summary>At run start, resolves each Window target's selector to a live HWND and stores it on the
/// <see cref="ResolvedTarget.Handle"/>. Non-Window targets are left untouched (handled in later milestones).</summary>
public static class WindowTargetBinder
{
    public static void Bind(IReadOnlyDictionary<Guid, ResolvedTarget> targets, IWindowResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        foreach (var target in targets.Values)
        {
            if (target.Type != BotTargetType.Window)
            {
                continue;
            }

            var handle = resolver.Resolve(target.Selector);
            if (handle == IntPtr.Zero)
            {
                throw new CommandLineException(
                    $"Could not resolve Window target selector '{target.Selector}' to a window.");
            }

            target.Handle = handle;
        }
    }
}
