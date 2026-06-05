using System.Drawing;

namespace AdbCore.Ocr;

/// <summary>One recognized word: its text, its bounding box (in the recognized image's pixels), and a
/// 0–1 confidence.</summary>
public readonly record struct OcrWord(string Text, Rectangle Bounds, double Confidence);

/// <summary>The result of recognizing an image: the full text and the per-word boxes.</summary>
public sealed record OcrResult(string Text, IReadOnlyList<OcrWord> Words);

/// <summary>Recognizes text in a bitmap.</summary>
public interface IOcrEngine
{
    OcrResult Recognize(Bitmap image);
}
