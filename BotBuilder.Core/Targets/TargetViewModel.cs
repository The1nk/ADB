using AdbCore.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core.Targets;

/// <summary>A single bot target (window / Android device / browser) shown as a chip in the Target Bar.</summary>
public partial class TargetViewModel : ObservableObject
{
    /// <summary>All target types, for binding a type picker in the UI.</summary>
    public static IReadOnlyList<BotTargetType> AllTypes { get; } = Enum.GetValues<BotTargetType>();

    public Guid Id { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private BotTargetType _type;
    [ObservableProperty] private string _selector = string.Empty;

    // A ComboBox's selection box renders the selected item via ToString() (DisplayMemberPath only styles the
    // drop-down list), so without this the properties-panel Target combo shows the type name. Show the Name.
    public override string ToString() => Name;
}
