namespace BotBuilder.Core;

/// <summary>A single input or output port shown on a node card.</summary>
public sealed class PortViewModel
{
    public PortViewModel(string name, PortDirection direction)
    {
        Name = name;
        Direction = direction;
    }

    public string Name { get; }
    public PortDirection Direction { get; }
}
