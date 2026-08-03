using OpenQA.Selenium;

namespace LEB2SCRAPPER.Repository.Master;

internal enum ScrapingFailureKind
{
    BrowserAutomation,
    TransientLeb2
}

internal static class ScrapingFailureClassifier
{
    private static readonly string[] BrowserFailureMarkers =
    [
        "chrome not reachable",
        "cannot find chrome binary",
        "devtoolsactiveport",
        "disconnected",
        "failed to start",
        "invalid session id",
        "no such driver",
        "session not created",
        "unable to connect to renderer"
    ];

    public static ScrapingFailureKind Classify(
        string stage,
        WebDriverException exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(exception);

        if (!stage.StartsWith("navigate-", StringComparison.Ordinal)
            || IsBrowserFailure(exception))
        {
            return ScrapingFailureKind.BrowserAutomation;
        }

        return ScrapingFailureKind.TransientLeb2;
    }

    private static bool IsBrowserFailure(WebDriverException exception)
    {
        return exception is DriverServiceNotFoundException
            || exception is NoSuchDriverException
            || exception is NoSuchWindowException
            || exception is WebDriverArgumentException
            || BrowserFailureMarkers.Any(marker =>
                exception.Message.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
