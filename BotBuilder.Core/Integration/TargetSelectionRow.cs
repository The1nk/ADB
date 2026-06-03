using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Integration;

/// <summary>One editable row in the target picker: the declared target's name/type plus the user's
/// chosen selector. <see cref="IsWindow"/> drives whether the row shows a live window dropdown.</summary>
public partial class TargetSelectionRow : ObservableObject
{
    public TargetSelectionRow(string name, BotTargetType type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public BotTargetType Type { get; }
    public bool IsWindow => Type == BotTargetType.Window;

    [ObservableProperty] private string _selector = string.Empty;
}
