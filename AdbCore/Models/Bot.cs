namespace AdbCore.Models;

/// <summary>A bot is a named DAG of actions and the connections between them.</summary>
public class Bot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Named targets, resolved to live handles at run start.</summary>
    public List<BotTarget> Targets { get; set; } = new();

    public List<BotAction> Actions { get; set; } = new();
    public List<ActionConnection> Connections { get; set; } = new();

    /// <summary>Reusable sub-bot definitions embedded in this (root) bot. Nested Bot action cards reference
    /// an entry by id; the library is flat — only the root bot populates this list.</summary>
    public List<Bot> NestedBots { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>The bot's name, used as the display string when a <see cref="Bot"/> is bound directly in the UI
    /// (e.g. the nested-bot picker ComboBox, whose templated selection box renders <c>ToString()</c> rather than
    /// honouring DisplayMemberPath). Falls back to a placeholder for an unnamed bot.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "(unnamed bot)" : Name;
}
