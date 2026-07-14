using System.Drawing;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Resolves the <see cref="FrameSnapshot"/> a pixel-reading action should read: the named stored
/// frame when Source = Stored (throwing if absent), or a fresh capture wrapped as a snapshot otherwise. The
/// fresh-capture delegate is platform-specific (Win32 HWND capture vs Android screenshot); the caller supplies
/// it, and its returned <see cref="Bitmap"/> is disposed here.</summary>
public static class FrameSourceResolver
{
    public static FrameSnapshot Acquire(ActionExecutionContext context, Func<Bitmap> captureFresh)
    {
        ArgumentNullException.ThrowIfNull(captureFresh);
        if (FrameSourceConfig.UsesStoredFrame(context.Action.Config))
        {
            var name = FrameSourceConfig.FrameNameOf(context.Action.Config);
            if (!context.Context.Frames.TryGet(name, out var snapshot) || snapshot is null)
            {
                throw new InvalidOperationException($"No stored frame named '{name}'. Add a Capture Frame action before this one.");
            }
            return snapshot;
        }

        using var bitmap = captureFresh();
        return FrameSnapshot.FromBitmap(bitmap);
    }
}
