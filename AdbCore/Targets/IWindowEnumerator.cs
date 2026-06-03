namespace AdbCore.Targets;

/// <summary>Enumerates visible top-level windows suitable for capture/selection in the UI.</summary>
public interface IWindowEnumerator
{
    IReadOnlyList<WindowInfo> Enumerate();
}
