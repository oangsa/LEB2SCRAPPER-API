namespace LEB2SCRAPPER.Repository.Caching;

public sealed class StructuralScrapeCacheOptions
{
    public int AbsoluteTtlSeconds { get; set; } = 60;

    public int MaximumEntries { get; set; } = 10_000;
}
