namespace AdbCore.Execution;

/// <summary>Catalogue of action executors, keyed by <see cref="IActionExecutor.TypeKey"/>.</summary>
public class ActionExecutorRegistry
{
    private readonly Dictionary<string, IActionExecutor> _byKey = new(StringComparer.Ordinal);

    public int Count => _byKey.Count;

    public IReadOnlyCollection<IActionExecutor> All => _byKey.Values;

    public void Register(IActionExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        if (!_byKey.TryAdd(executor.TypeKey, executor))
        {
            throw new InvalidOperationException(
                $"An executor with TypeKey '{executor.TypeKey}' is already registered.");
        }
    }

    public bool TryGet(string typeKey, out IActionExecutor? executor)
        => _byKey.TryGetValue(typeKey, out executor);

    public IActionExecutor Get(string typeKey)
    {
        if (!_byKey.TryGetValue(typeKey, out var executor))
        {
            throw new KeyNotFoundException($"No executor registered with TypeKey '{typeKey}'.");
        }

        return executor;
    }
}
