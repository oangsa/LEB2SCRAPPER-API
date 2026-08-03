using LEB2SCRAPPER.Repository.Master;
using OpenQA.Selenium;

namespace LEB2SCRAPPER.Tests.Repository;

public class ScrapingFailureClassifierTests
{
    [Fact]
    public void NavigationPageLoadTimeout_IsTransientLeb2Failure()
    {
        var kind = ScrapingFailureClassifier.Classify(
            "navigate-class-page",
            new WebDriverTimeoutException("The page load timed out."));

        Assert.Equal(ScrapingFailureKind.TransientLeb2, kind);
    }

    [Fact]
    public void NavigationBrowserCrash_IsBrowserAutomationFailure()
    {
        var kind = ScrapingFailureClassifier.Classify(
            "navigate-class-page",
            new WebDriverException("chrome not reachable"));

        Assert.Equal(ScrapingFailureKind.BrowserAutomation, kind);
    }

    [Theory]
    [InlineData("create-driver")]
    [InlineData("configure-cookie-header")]
    [InlineData("wait-semester-links")]
    [InlineData("read-page-source")]
    public void NonNavigationWebDriverFailure_IsBrowserAutomationFailure(
        string stage)
    {
        var kind = ScrapingFailureClassifier.Classify(
            stage,
            new WebDriverException("Synthetic WebDriver failure."));

        Assert.Equal(ScrapingFailureKind.BrowserAutomation, kind);
    }
}
