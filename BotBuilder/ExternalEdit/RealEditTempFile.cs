using System.IO;
using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.ExternalEdit;

/// <summary>Real filesystem implementation of <see cref="IEditTempFile"/>.</summary>
internal sealed class RealEditTempFile : IEditTempFile
{
    public void Write(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, text);
    }

    public string? TryRead(string path)
    {
        // Retry up to 3 times in case the editor holds a brief exclusive lock while flushing.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                if (attempt < 2)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    public void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* swallow — best effort */ }
    }
}
