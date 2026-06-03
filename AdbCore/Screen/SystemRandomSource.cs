namespace AdbCore.Screen;

/// <summary>Production <see cref="IRandomSource"/> over <see cref="Random.Shared"/>.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxInclusive) => Random.Shared.Next(minInclusive, maxInclusive + 1);
}
