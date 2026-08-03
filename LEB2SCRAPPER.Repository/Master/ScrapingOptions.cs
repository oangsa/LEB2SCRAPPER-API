namespace LEB2SCRAPPER.Repository.Master;

public sealed class ScrapingOptions
{
    public int SemesterRenderTimeoutSeconds { get; set; } = 30;

    public TimeSpan SemesterRenderTimeout =>
        TimeSpan.FromSeconds(SemesterRenderTimeoutSeconds);

    public void Validate()
    {
        if (SemesterRenderTimeoutSeconds is < 1 or > 60)
        {
            throw new InvalidOperationException(
                "Scraping:SemesterRenderTimeoutSeconds must be between 1 and 60.");
        }
    }
}
