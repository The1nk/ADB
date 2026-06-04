using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Picker;

/// <summary>Sequences a 1- or 2-point coordinate pick. The dialog feeds it source-pixel clicks via
/// <see cref="RecordClick"/>; it advances through the action's <see cref="CoordinatePoint"/>s, exposes a
/// prompt for the current point, and yields the (fieldKey, value) pairs to write back.</summary>
public partial class CoordinatePickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<CoordinatePoint> _points;
    private readonly List<(int X, int Y)> _collected = new();

    public CoordinatePickerViewModel(IReadOnlyList<CoordinatePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points;
    }

    /// <summary>True once every point has a recorded click.</summary>
    public bool IsComplete => _collected.Count >= _points.Count;

    /// <summary>Instruction for the next point, e.g. "Click the Start point". Empty when complete.</summary>
    public string CurrentPrompt => IsComplete ? string.Empty : $"Click the {_points[_collected.Count].Label} point";

    /// <summary>Records a source-pixel click for the current point and advances. No-op once complete.</summary>
    public void RecordClick(int sourceX, int sourceY)
    {
        if (IsComplete)
        {
            return;
        }

        _collected.Add((sourceX, sourceY));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(CurrentPrompt));
    }

    /// <summary>The collected (XKey, YKey, X, Y) write-back tuples — only points recorded so far.</summary>
    public IReadOnlyList<(string XKey, string YKey, int X, int Y)> Results() =>
        _collected.Select((c, i) => (_points[i].XKey, _points[i].YKey, c.X, c.Y)).ToList();
}
