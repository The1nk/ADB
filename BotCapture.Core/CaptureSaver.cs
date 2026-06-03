using System.Drawing;
using System.Drawing.Imaging;

namespace BotCapture.Core;

/// <summary>Saves cropped templates into a folder: generates the next free <c>capture_NNN.png</c> name
/// and writes the PNG alongside its confidence sidecar.</summary>
public sealed class CaptureSaver
{
    private readonly string _folder;

    public CaptureSaver(string folder)
    {
        _folder = folder;
    }

    /// <summary>The lowest <c>capture_NNN.png</c> (N from 1) not already present in the folder.</summary>
    public string NextFileName()
    {
        for (var i = 1; ; i++)
        {
            var name = $"capture_{i:000}.png";
            if (!File.Exists(Path.Combine(_folder, name)))
            {
                return name;
            }
        }
    }

    /// <summary>Writes <paramref name="crop"/> as a PNG named <paramref name="fileName"/> in the folder,
    /// plus a confidence sidecar next to it.</summary>
    public void Save(Bitmap crop, string fileName, double confidence)
    {
        var path = Path.Combine(_folder, fileName);
        crop.Save(path, ImageFormat.Png);
        ConfidenceSidecar.Write(path, confidence);
    }
}
