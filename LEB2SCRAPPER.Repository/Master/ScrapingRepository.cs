using System.Diagnostics;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using LEB2SCRAPPER.Repository.Parsing;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Microsoft.Extensions.Logging;
using MicrosoftLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace LEB2SCRAPPER.Repository.Master;

public class ScrapingRepository : IScrapingRepository
{
    private const string BaseUrl = "https://app.leb2.org";
    private const string LoginUrl = "https://signin.leb2.org/login";
    private static readonly TimeSpan DriverCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ElementWaitTimeout = TimeSpan.FromSeconds(10);
    private readonly ChromeOptions _chromeOptions = new();
    private readonly IOutboundRequestGate _outboundRequestGate;
    private readonly IClientFingerprintProvider _clientFingerprintProvider;
    private readonly IStructuralScrapeCache _structuralScrapeCache;
    private readonly ScrapingOptions _scrapingOptions;
    private readonly ILogger<ScrapingRepository> _logger;
    private readonly Leb2RenderedPageParser _renderedPageParser = new();

    public ScrapingRepository(
        IOutboundRequestGate outboundRequestGate,
        IClientFingerprintProvider clientFingerprintProvider,
        IStructuralScrapeCache structuralScrapeCache,
        ScrapingOptions scrapingOptions,
        ILogger<ScrapingRepository> logger)
    {
        _outboundRequestGate = outboundRequestGate;
        _clientFingerprintProvider = clientFingerprintProvider;
        _structuralScrapeCache = structuralScrapeCache;
        _scrapingOptions = scrapingOptions;
        _logger = logger;
        _chromeOptions.AddArgument("--headless=new");
        _chromeOptions.AddArgument("--disable-gpu");
        _chromeOptions.AddArgument("--no-sandbox");
        _chromeOptions.AddArgument("--window-size=1920,1080");
        _chromeOptions.AddArgument("--no-zygote");
        _chromeOptions.AddArgument("--disable-dev-shm-usage");
        _chromeOptions.AddArgument("--blink-settings=imagesEnabled=false");
        _chromeOptions.AddArgument(
            "--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/");
    }

    public Task<string?> GetCookieAsync(
        Credentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (credentials is null
            || string.IsNullOrWhiteSpace(credentials.Username)
            || string.IsNullOrWhiteSpace(credentials.Password))
        {
            throw new ScrapingCustomException("Credentials must be provided.");
        }

        var context = new OutboundRequestContext(
            Leb2OutboundEndpoints.CookieLogin,
            _clientFingerprintProvider.CreateForUsername(credentials.Username));

        return _outboundRequestGate.ExecuteAsync(
            context,
            token => GetCookieCoreAsync(credentials, token),
            cancellationToken);
    }

    public Task<List<SemesterInfo>?> GetSemestersAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ScrapingCustomException("Token must be provided.");
        }

        var clientKey = _clientFingerprintProvider.CreateForSession(token);
        var context = CreateSessionContext(
            Leb2OutboundEndpoints.Semesters,
            clientKey);

        return _structuralScrapeCache.GetSemestersAsync(
            clientKey,
            requestToken => _outboundRequestGate.ExecuteAsync(
                context,
                gateToken => GetSemestersCoreAsync(token, gateToken),
                requestToken),
            cancellationToken);
    }

    public Task<List<ClassInfo>?> GetClassesBySemesterIdAsync(
        int semesterId,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (semesterId <= 0 || string.IsNullOrWhiteSpace(token))
        {
            throw new ScrapingCustomException(
                "Semester ID and token must be provided.");
        }

        var clientKey = _clientFingerprintProvider.CreateForSession(token);
        var context = CreateSessionContext(
            Leb2OutboundEndpoints.Classes,
            clientKey);

        return _structuralScrapeCache.GetClassesAsync(
            clientKey,
            semesterId,
            requestToken => _outboundRequestGate.ExecuteAsync(
                context,
                gateToken => GetClassesCoreAsync(
                    semesterId,
                    token,
                    gateToken),
                requestToken),
            cancellationToken);
    }

    private async Task<string?> GetCookieCoreAsync(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        DriverLease? driverLease = null;
        var stage = "create-driver";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            driverLease = new DriverLease(CreateDriver());
            var driver = driverLease.Driver;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((DriverLease)state!).Dispose(),
                driverLease);
            cancellationToken.ThrowIfCancellationRequested();
            var wait = new WebDriverWait(driver, ElementWaitTimeout);

            stage = "navigate-login";
            await driver.Navigate().GoToUrlAsync(LoginUrl).WaitAsync(cancellationToken);

            stage = "wait-login-form";
            try
            {
                await RunDriverOperationAsync(
                    () => wait.Until(d => d.Title.Contains("LEB2")),
                    cancellationToken);
                await RunDriverOperationAsync(
                    () => wait.Until(d => d.FindElement(By.Id("input-1"))),
                    cancellationToken);
                await RunDriverOperationAsync(
                    () => wait.Until(d => d.FindElement(By.Id("input-4"))),
                    cancellationToken);
            }
            catch (WebDriverTimeoutException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (WebDriverTimeoutException exception)
            {
                throw new StructuralParseException(
                    "cookie-login.login_form",
                    "The LEB2 login form no longer matches the expected structure.",
                    exception);
            }

            stage = "submit-login";
            await RunDriverOperationAsync(
                () =>
                {
                    driver.FindElement(By.Id("input-1")).SendKeys(credentials.Username);
                    driver.FindElement(By.Id("input-4")).SendKeys(credentials.Password);

                    var loginButton = driver.FindElement(By.CssSelector(
                        ".v-btn.v-btn--block.v-btn--elevated.v-theme--light.bg-primary"
                        + ".v-btn--density-default.v-btn--size-default.v-btn--variant-elevated.mt-6"));
                    loginButton.Click();
                    return true;
                },
                cancellationToken);

            try
            {
                stage = "wait-login-redirect";
                await RunDriverOperationAsync(
                    () => wait.Until(d => IsAppHost(d.Url)),
                    cancellationToken);
            }
            catch (WebDriverTimeoutException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (WebDriverTimeoutException exception)
            {
                throw new ScrapingCustomException(
                    "LEB2 did not accept the supplied credentials.",
                    exception);
            }

            stage = "read-session-cookie";
            var cookieString = await RunDriverOperationAsync(
                () => string.Join(
                    "; ",
                    driver.Manage().Cookies.AllCookies.Select(
                        cookie => $"{cookie.Name}={cookie.Value}")),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(cookieString))
            {
                throw new Leb2UpstreamException(
                    "LEB2 completed login without returning a session cookie.");
            }

            return cookieString;
        }
        catch (WebDriverException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (NoSuchElementException exception)
        {
            throw new StructuralParseException(
                "cookie-login.login_form",
                "The LEB2 login form no longer matches the expected structure.",
                exception);
        }
        catch (WebDriverException exception)
        {
            throw MapWebDriverException(
                stage,
                "The browser could not complete the LEB2 login request.",
                "Browser automation failed during the LEB2 login request.",
                exception);
        }
        finally
        {
            driverLease?.Dispose();
        }
    }

    private async Task<List<SemesterInfo>?> GetSemestersCoreAsync(
        string token,
        CancellationToken cancellationToken)
    {
        DriverLease? driverLease = null;
        var startedAt = Stopwatch.GetTimestamp();
        var stage = "create-driver";
        var driverCreated = false;
        var navigationCompleted = false;

        _logger.LogInformation(
            "LEB2 semester scrape started. Trace identifier: {TraceIdentifier}",
            GetTraceIdentifier());

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            driverLease = new DriverLease(CreateDriver());
            driverCreated = true;
            var driver = driverLease.Driver;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((DriverLease)state!).Dispose(),
                driverLease);
            cancellationToken.ThrowIfCancellationRequested();
            stage = "configure-cookie-header";
            SetCookieHeader(driver, token);

            stage = "navigate-class-page";
            await driver.Navigate()
                .GoToUrlAsync($"{BaseUrl}/class")
                .WaitAsync(cancellationToken);
            navigationCompleted = true;

            stage = "validate-session";
            EnsureSessionIsActive(driver);

            var semesters = await ExtractSemestersAsync(
                driver,
                cancellationToken,
                value => stage = value);

            _logger.LogInformation(
                "LEB2 semester scrape completed in {ElapsedMilliseconds} ms. "
                + "Trace identifier: {TraceIdentifier}",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                GetTraceIdentifier());

            return semesters;
        }
        catch (WebDriverException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SessionExpiredException exception)
        {
            LogSemesterScrapeFailure(
                MicrosoftLogLevel.Warning,
                stage,
                exception,
                startedAt,
                driverCreated,
                navigationCompleted);
            throw;
        }
        catch (StructuralParseException exception)
        {
            LogSemesterScrapeFailure(
                MicrosoftLogLevel.Error,
                stage,
                exception,
                startedAt,
                driverCreated,
                navigationCompleted);
            throw;
        }
        catch (WebDriverException exception)
        {
            LogSemesterScrapeFailure(
                MicrosoftLogLevel.Error,
                stage,
                exception,
                startedAt,
                driverCreated,
                navigationCompleted);
            throw MapWebDriverException(
                stage,
                "The browser could not complete the LEB2 semester request.",
                "Browser automation failed during the LEB2 semester request.",
                exception);
        }
        finally
        {
            driverLease?.Dispose();
        }
    }

    private async Task<List<ClassInfo>?> GetClassesCoreAsync(
        int semesterId,
        string token,
        CancellationToken cancellationToken)
    {
        DriverLease? driverLease = null;
        var stage = "create-driver";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            driverLease = new DriverLease(CreateDriver());
            var driver = driverLease.Driver;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((DriverLease)state!).Dispose(),
                driverLease);
            cancellationToken.ThrowIfCancellationRequested();
            stage = "configure-cookie-header";
            SetCookieHeader(driver, token);

            stage = "navigate-class-page";
            await driver.Navigate().GoToUrlAsync(
                    $"{BaseUrl}/class?semester_id={semesterId}")
                .WaitAsync(cancellationToken);
            stage = "validate-session";
            EnsureSessionIsActive(driver);

            stage = "wait-class-cards";
            return await ExtractClassesAsync(driver, cancellationToken);
        }
        catch (WebDriverException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WebDriverException exception)
        {
            throw MapWebDriverException(
                stage,
                "The browser could not complete the LEB2 class request.",
                "Browser automation failed during the LEB2 class request.",
                exception);
        }
        finally
        {
            driverLease?.Dispose();
        }
    }

    // ponytail: one cold Chromium start per request (~seconds); pool drivers only if
    // the startup cost, not the LEB2 render wait, becomes the dominant latency.
    private ChromeDriver CreateDriver()
    {
        var driverService = ChromeDriverService.CreateDefaultService();
        ChromeDriver? driver = null;

        try
        {
            driver = new ChromeDriver(
                driverService,
                _chromeOptions,
                DriverCommandTimeout);
            driver.Manage().Timeouts().PageLoad = DriverCommandTimeout;
            return driver;
        }
        catch
        {
            if (driver is null)
            {
                driverService.Dispose();
            }
            else
            {
                CloseDriver(driver);
            }

            throw;
        }
    }

    private OutboundRequestContext CreateSessionContext(
        string endpoint,
        string clientKey)
    {
        return new OutboundRequestContext(
            endpoint,
            clientKey,
            UsesSessionCredential: true);
    }

    private static void SetCookieHeader(ChromeDriver driver, string token)
    {
        var headers = new Dictionary<string, object>
        {
            { "X-Requested-With", "XMLHttpRequest" },
            {
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/"
            },
            { "Cookie", token }
        };

        driver.ExecuteCdpCommand("Network.enable", new Dictionary<string, object>());
        driver.ExecuteCdpCommand("Network.setExtraHTTPHeaders", new Dictionary<string, object>
        {
            { "headers", headers }
        });
    }

    private static void EnsureSessionIsActive(IWebDriver driver)
    {
        if (!Uri.TryCreate(driver.Url, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("app.leb2.org", StringComparison.OrdinalIgnoreCase)
            || driver.FindElements(By.CssSelector(
                "body > pre:first-child + div.json-formatter-container")).Count > 0)
        {
            throw new SessionExpiredException();
        }
    }

    private static bool IsAppHost(string currentUrl)
    {
        return Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)
            && uri.Host.Equals("app.leb2.org", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<SemesterInfo>> ExtractSemestersAsync(
        IWebDriver driver,
        CancellationToken cancellationToken,
        Action<string> setStage)
    {
        setStage("wait-semester-links");
        var pageSource = string.Empty;
        await WaitForUsableSemesterLinkAsync(
            () => RunDriverOperationAsync(
                () =>
                {
                    EnsureSessionIsActive(driver);
                    pageSource = driver.PageSource;
                    return _renderedPageParser.HasUsableSemesterLink(pageSource);
                },
                cancellationToken),
            _scrapingOptions.SemesterRenderTimeout,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        setStage("parse-semesters");
        return _renderedPageParser.ParseSemesters(pageSource);
    }

    internal static async Task WaitForUsableSemesterLinkAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var startedAt = Stopwatch.GetTimestamp();
        var pollInterval = TimeSpan.FromMilliseconds(100);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await condition())
            {
                return;
            }

            var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                throw new StructuralParseException(
                    "semesters.semester_links",
                    "The semester links no longer match the expected structure.",
                    new TimeoutException(
                        "The semester links did not render before the wait expired."));
            }

            await Task.Delay(
                remaining < pollInterval ? remaining : pollInterval,
                cancellationToken);
        }
    }

    private static Exception MapWebDriverException(
        string stage,
        string transientMessage,
        string browserMessage,
        WebDriverException exception)
    {
        return ScrapingFailureClassifier.Classify(stage, exception)
            is ScrapingFailureKind.TransientLeb2
            ? new TransientLeb2Exception(transientMessage, exception)
            : new BrowserAutomationException(stage, browserMessage, exception);
    }

    private void LogSemesterScrapeFailure(
        MicrosoftLogLevel logLevel,
        string stage,
        Exception exception,
        long startedAt,
        bool driverCreated,
        bool navigationCompleted)
    {
        _logger.Log(
            logLevel,
            "LEB2 semester scrape failed at stage {Stage} with {ExceptionType}. "
            + "Elapsed milliseconds: {ElapsedMilliseconds}. "
            + "Driver created: {DriverCreated}. "
            + "Navigation completed: {NavigationCompleted}. "
            + "Trace identifier: {TraceIdentifier}",
            stage,
            exception.GetType().Name,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            driverCreated,
            navigationCompleted,
            GetTraceIdentifier());
    }

    private static string GetTraceIdentifier()
    {
        return Activity.Current?.TraceId.ToString() ?? "unavailable";
    }

    private async Task<List<ClassInfo>?> ExtractClassesAsync(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        try
        {
            var wait = new WebDriverWait(driver, ElementWaitTimeout);
            await RunDriverOperationAsync(
                () => wait.Until(d => d.FindElements(By.CssSelector(
                        "#classListMain .class-list__row.class-publish"))
                    .Count > 0),
                cancellationToken);
        }
        catch (WebDriverTimeoutException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new StructuralParseException(
                "classes.class_cards",
                "The class cards no longer match the expected structure.",
                exception);
        }

        var pageSource = await RunDriverOperationAsync(
            () => driver.PageSource,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return _renderedPageParser.ParseClasses(pageSource);
    }

    private static Task<T> RunDriverOperationAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(operation, CancellationToken.None).WaitAsync(cancellationToken);
    }

    private static void CloseDriver(IWebDriver? driver)
    {
        if (driver is null)
        {
            return;
        }

        try
        {
            driver.Quit();
        }
        catch (WebDriverException)
        {
        }

        try
        {
            driver.Dispose();
        }
        catch (WebDriverException)
        {
        }
    }

    private sealed class DriverLease : IDisposable
    {
        private ChromeDriver? _driver;

        public DriverLease(ChromeDriver driver)
        {
            _driver = driver;
        }

        public ChromeDriver Driver => _driver
            ?? throw new ObjectDisposedException(nameof(DriverLease));

        public void Dispose()
        {
            var driver = Interlocked.Exchange(ref _driver, null);
            CloseDriver(driver);
        }
    }
}
