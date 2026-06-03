using System.IO;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class ConfidenceSidecarReaderTests
{
    private static string TempImagePath() => Path.Combine(Path.GetTempPath(), $"adb_{Guid.NewGuid():N}.png");

    [Fact]
    public void Read_ReturnsConfidence_FromSidecar()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", """{"confidence":0.83}""");
        try
        {
            Assert.Equal(0.83, ConfidenceSidecarReader.Read(image)!.Value, 3);
        }
        finally { File.Delete(image + ".meta.json"); }
    }

    [Fact]
    public void Read_MissingSidecar_ReturnsNull()
    {
        Assert.Null(ConfidenceSidecarReader.Read(TempImagePath()));
    }

    [Fact]
    public void Read_CorruptSidecar_ReturnsNull()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", "{ not valid json");
        try
        {
            Assert.Null(ConfidenceSidecarReader.Read(image));
        }
        finally { File.Delete(image + ".meta.json"); }
    }

    [Fact]
    public void Read_SidecarWithoutConfidenceKey_ReturnsNull()
    {
        var image = TempImagePath();
        File.WriteAllText(image + ".meta.json", """{"other":1}""");
        try
        {
            Assert.Null(ConfidenceSidecarReader.Read(image));
        }
        finally { File.Delete(image + ".meta.json"); }
    }
}
