namespace AdbCore.Screen;

/// <summary>Indirection over RNG so image-search randomness (e.g. a random click point within a match)
/// is deterministic in tests.</summary>
public interface IRandomSource
{
    /// <summary>Returns a random integer in the inclusive range [<paramref name="minInclusive"/>, <paramref name="maxInclusive"/>].</summary>
    int Next(int minInclusive, int maxInclusive);
}
