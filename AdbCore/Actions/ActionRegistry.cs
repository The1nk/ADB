namespace AdbCore.Actions;

/// <summary>Catalogue of available action types, keyed by <see cref="IActionDefinition.TypeKey"/>.</summary>
public class ActionRegistry
{
    private readonly Dictionary<string, IActionDefinition> _byKey = new(StringComparer.Ordinal);

    /// <summary>The number of registered action definitions.</summary>
    public int Count => _byKey.Count;

    /// <summary>All registered action definitions.</summary>
    public IReadOnlyCollection<IActionDefinition> All => _byKey.Values;

    /// <summary>Registers a definition. Throws if its <see cref="IActionDefinition.TypeKey"/> is already registered.</summary>
    public void Register(IActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!_byKey.TryAdd(definition.TypeKey, definition))
        {
            throw new InvalidOperationException(
                $"An action with TypeKey '{definition.TypeKey}' is already registered.");
        }
    }

    /// <summary>Looks up a definition by key, returning false if not found.</summary>
    public bool TryGet(string typeKey, out IActionDefinition? definition)
        => _byKey.TryGetValue(typeKey, out definition);

    /// <summary>Gets a definition by key. Throws <see cref="KeyNotFoundException"/> if not found.</summary>
    public IActionDefinition Get(string typeKey)
    {
        if (!_byKey.TryGetValue(typeKey, out var definition))
        {
            throw new KeyNotFoundException($"No action registered with TypeKey '{typeKey}'.");
        }

        return definition;
    }

    /// <summary>Returns all definitions in the given category.</summary>
    public IEnumerable<IActionDefinition> GetByCategory(string category)
        => _byKey.Values.Where(d => d.Category == category);
}
