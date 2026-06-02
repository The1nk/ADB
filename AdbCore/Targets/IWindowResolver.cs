namespace AdbCore.Targets;

/// <summary>Resolves a Window target selector (e.g. <c>process:Notepad</c>) to a live window handle (HWND).
/// Returns <see cref="IntPtr.Zero"/> when no matching window is found.</summary>
public interface IWindowResolver
{
    IntPtr Resolve(string selector);
}
