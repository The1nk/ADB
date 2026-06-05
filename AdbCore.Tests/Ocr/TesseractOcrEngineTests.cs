using System.IO;
using AdbCore.Ocr;
using Xunit;

namespace AdbCore.Tests.Ocr;

public class TesseractOcrEngineTests
{
    [Fact]
    public void Dispose_WithoutRecognize_DoesNotLoadOrThrow()
    {
        // Lazy: constructing with a bogus tessdata path must NOT load (eager load would throw on a bad
        // path), and disposing without ever recognizing must be a safe no-op (including double-dispose).
        var engine = new TesseractOcrEngine(Path.Combine(Path.GetTempPath(), "adb-no-tessdata-here"));
        engine.Dispose();
        engine.Dispose();
    }
}
