using System.Windows;
using System.Windows.Controls;
using BotBuilder.Core.Properties;

namespace BotBuilder;

/// <summary>Chooses a config-field editor template based on the field's type.</summary>
public sealed class ConfigFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }
    public DataTemplate? MultilineTemplate { get; set; }
    public DataTemplate? NumberTemplate { get; set; }
    public DataTemplate? BooleanTemplate { get; set; }
    public DataTemplate? EnumTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not ConfigFieldViewModel field)
        {
            return base.SelectTemplate(item, container);
        }

        return field.Type switch
        {
            AdbCore.Actions.ConfigFieldType.MultilineString => MultilineTemplate,
            AdbCore.Actions.ConfigFieldType.Number => NumberTemplate,
            AdbCore.Actions.ConfigFieldType.Boolean => BooleanTemplate,
            AdbCore.Actions.ConfigFieldType.Enum => EnumTemplate,
            _ => StringTemplate, // String + (for now) FilePath/ImagePath fall back to a text box
        };
    }
}
