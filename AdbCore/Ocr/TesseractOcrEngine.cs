using System.Drawing;
using Tesseract;

namespace AdbCore.Ocr;

/// <summary>Tesseract-backed OCR engine (charlesw Tesseract 5.2.0). Loads <c>eng</c> trained data from a
/// <c>tessdata</c> directory (default: a <c>tessdata</c> folder next to the running executable).
/// Live adapter — verified by hand, not unit-tested.</summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly TesseractEngine _engine;
    // TesseractEngine is not thread-safe; serialize all engine/Pix/page/iter access.
    private readonly object _lock = new();

    public TesseractOcrEngine(string? tessdataPath = null, string language = "eng")
    {
        var path = tessdataPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "tessdata");
        _engine = new TesseractEngine(path, language, EngineMode.Default);
    }

    public OcrResult Recognize(Bitmap image)
    {
        // PixConverter is not present in charlesw Tesseract 5.2.0 netstandard2.0.
        // Use Pix.LoadFromMemory(byte[]) via an in-memory PNG instead.
        // PNG-encode is image-local and touches no engine state — do it outside the lock.
        byte[] png;
        using (var ms = new System.IO.MemoryStream())
        {
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }

        lock (_lock)
        {
            using var pix = Pix.LoadFromMemory(png);
            using var page = _engine.Process(pix);

            var text = page.GetText() ?? string.Empty;
            var words = new List<OcrWord>();

            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                    {
                        var wordText = iter.GetText(PageIteratorLevel.Word) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(wordText))
                        {
                            var confidence = iter.GetConfidence(PageIteratorLevel.Word) / 100.0; // 0–100 → 0–1
                            words.Add(new OcrWord(
                                wordText.Trim(),
                                new Rectangle(rect.X1, rect.Y1, rect.Width, rect.Height),
                                confidence));
                        }
                    }
                }
                while (iter.Next(PageIteratorLevel.Word));
            }

            return new OcrResult(text, words);
        }
    }

    public void Dispose() => _engine.Dispose();
}
