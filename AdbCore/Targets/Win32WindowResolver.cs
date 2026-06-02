using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace AdbCore.Targets;

/// <summary>Win32 implementation of <see cref="IWindowResolver"/>. Resolves <c>process:</c> (main window of
/// a named process), <c>title:</c> (first visible top-level window whose title contains the value), and
/// <c>hwnd:</c> (a literal handle, decimal or 0x-hex). Returns <see cref="IntPtr.Zero"/> if not found.</summary>
public sealed class Win32WindowResolver : IWindowResolver
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public IntPtr Resolve(string selector)
    {
        var parsed = WindowSelector.Parse(selector);
        return parsed.Kind switch
        {
            WindowSelectorKind.Handle => ParseHandle(parsed.Value),
            WindowSelectorKind.Process => ResolveProcess(parsed.Value),
            WindowSelectorKind.Title => ResolveTitle(parsed.Value),
            _ => IntPtr.Zero,
        };
    }

    private static IntPtr ParseHandle(string value)
    {
        var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var style = isHex ? NumberStyles.HexNumber : NumberStyles.Integer;
        var text = isHex ? value[2..] : value;
        return long.TryParse(text, style, CultureInfo.InvariantCulture, out var n) ? (IntPtr)n : IntPtr.Zero;
    }

    private static IntPtr ResolveProcess(string name)
    {
        var result = IntPtr.Zero;
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process) // release the OS handle held by every Process object, including unmatched ones
            {
                if (result != IntPtr.Zero)
                {
                    continue; // already found; keep iterating only to dispose the rest
                }

                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        result = process.MainWindowHandle;
                    }
                }
                catch (InvalidOperationException)
                {
                    // process exited between enumeration and access; ignore
                }
            }
        }

        return result;
    }

    private static IntPtr ResolveTitle(string titleSubstring)
    {
        var found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return true;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumerating
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }
}
