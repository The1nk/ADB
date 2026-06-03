using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class ConfidenceSidecarTests
{
    private static string TempImagePath() =>
        Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}.png");

    [Fact]
    public void Write_ThenRead_RoundTripsConfidence()
    {
        var image = TempImagePath();
        try
        {
            ConfidenceSidecar.Write(image, 0.83);
            Assert.Equal(0.83, ConfidenceSidecar.Read(image, fallback: 0.5), 3);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }

    [Fact]
    public void Read_MissingSidecar_ReturnsFallback()
    {
        Assert.Equal(0.9, ConfidenceSidecar.Read(TempImagePath(), fallback: 0.9), 3);
    }

    [Fact]
    public void Read_CorruptSidecar_ReturnsFallback()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", "{ not valid json");
        try
        {
            Assert.Equal(0.9, ConfidenceSidecar.Read(image, fallback: 0.9), 3);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }

    [Fact]
    public void Write_ProducesCamelCaseConfidenceKey()
    {
        var image = TempImagePath();
        try
        {
            ConfidenceSidecar.Write(image, 0.7);
            var json = File.ReadAllText(image + ".meta.json");
            Assert.Contains("\"confidence\"", json);
        }
        finally
        {
            File.Delete(image + ".meta.json");
        }
    }
}
