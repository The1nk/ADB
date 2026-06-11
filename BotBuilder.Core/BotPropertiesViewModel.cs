using CommunityToolkit.Mvvm.ComponentModel;

namespace BotBuilder.Core;

/// <summary>Backs the File ▸ Properties dialog: editable Name/Description plus read-only Created/Updated
/// (formatted for display). The caller seeds it from the editor and applies Name/Description back on OK.</summary>
public partial class BotPropertiesViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;

    public BotPropertiesViewModel(string name, string description, DateTime createdAt, DateTime updatedAt)
    {
        _name = name;
        _description = description;
        CreatedDisplay = Format(createdAt);
        UpdatedDisplay = Format(updatedAt);
    }

    public string CreatedDisplay { get; }
    public string UpdatedDisplay { get; }

    private static string Format(DateTime dt) =>
        dt == default ? "—" : dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
