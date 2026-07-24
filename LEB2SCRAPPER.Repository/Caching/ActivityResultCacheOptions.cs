namespace LEB2SCRAPPER.Repository.Caching;

public sealed class ActivityResultCacheOptions
{
    public int AbsoluteTtlSeconds { get; set; } = 30;

    public int MaximumEntries { get; set; } = 2_000;
}
