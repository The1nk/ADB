using System.Drawing;
using Tesseract;

namespace AdbCore.Ocr;

/// <summary>Tesseract-backed OCR engine (charlesw Tesseract 5.2.0). Loads <c>eng</c> trained data from a
/// <c>tessdata</c> directory (default: a <c>tessdata</c> folder next to the running executable)
/// LAZILY on first recognition, so registering the OCR actions (e.g. in BotBuilder or tests) does not
/// load the 23 MB data. Live adapter — verified by hand, not unit-tested.
/// The native engine isn't thread-safe, so Recognize is serialized by a lock.</summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly string _tessdataPath;
    private readonly string _language;
    // TesseractEngine is not thread-safe; serialize all engine/Pix/page/iter access.
    private readonly object _lock = new();
    private TesseractEngine? _engine;

    public TesseractOcrEngine(string? tessdataPath = null, string language = "eng")
    {
        _tessdataPath = tessdataPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "tessdata");
        _language = language;
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
            _engine ??= new TesseractEngine(_tessdataPath, _language, EngineMode.Default);

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

    public void Dispose()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
