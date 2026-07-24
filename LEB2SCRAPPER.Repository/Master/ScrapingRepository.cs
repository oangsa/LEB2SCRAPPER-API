using System.Text.RegularExpressions;
using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Infrastructure.Contracts.Outbound;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace LEB2SCRAPPER.Repository.Master;

public class ScrapingRepository : IScrapingRepository
{
    private const string BaseUrl = "https://app.leb2.org";
    private const string LoginUrl = "https://signin.leb2.org/login";
    private static readonly TimeSpan DriverCommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ElementWaitTimeout = TimeSpan.FromSeconds(10);
    private readonly ChromeOptions _chromeOptions = new();
    private readonly IOutboundRequestGate _outboundRequestGate;

    public ScrapingRepository(IOutboundRequestGate outboundRequestGate)
    {
        _outboundRequestGate = outboundRequestGate;
        _chromeOptions.AddArgument("--headless=new");
        _chromeOptions.AddArgument("--disable-gpu");
        _chromeOptions.AddArgument("--no-sandbox");
        _chromeOptions.AddArgument("--window-size=1920,1080");
        _chromeOptions.AddArgument("--single-process");
        _chromeOptions.AddArgument("--no-zygote");
        _chromeOptions.AddArgument("--disable-dev-shm-usage");
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

        var context = new OutboundRequestContext(Leb2OutboundEndpoints.CookieLogin);

        return _outboundRequestGate.ExecuteAsync(
            context,
            token => GetCookieCoreAsync(credentials, token),
            cancellationToken);
    }

    public Task<List<int>?> GetSemesterIdsAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ScrapingCustomException("Token must be provided.");
        }

        var context = CreateSessionContext(Leb2OutboundEndpoints.Semesters);

        return _outboundRequestGate.ExecuteAsync(
            context,
            requestToken => GetSemesterIdsCoreAsync(token, requestToken),
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

        var context = CreateSessionContext(Leb2OutboundEndpoints.Classes);

        return _outboundRequestGate.ExecuteAsync(
            context,
            requestToken => GetClassesCoreAsync(semesterId, token, requestToken),
            cancellationToken);
    }

    private async Task<string?> GetCookieCoreAsync(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        DriverLease? driverLease = null;

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

            await driver.Navigate().GoToUrlAsync(LoginUrl).WaitAsync(cancellationToken);

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
            throw new TransientLeb2Exception(
                "The browser could not complete the LEB2 login request.",
                exception);
        }
        finally
        {
            driverLease?.Dispose();
        }
    }

    private async Task<List<int>?> GetSemesterIdsCoreAsync(
        string token,
        CancellationToken cancellationToken)
    {
        DriverLease? driverLease = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            driverLease = new DriverLease(CreateDriver());
            var driver = driverLease.Driver;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((DriverLease)state!).Dispose(),
                driverLease);
            cancellationToken.ThrowIfCancellationRequested();
            SetCookieHeader(driver, token);

            await driver.Navigate()
                .GoToUrlAsync($"{BaseUrl}/class")
                .WaitAsync(cancellationToken);
            EnsureSessionIsActive(driver);

            return await ExtractSemesterIdsAsync(driver, cancellationToken);
        }
        catch (WebDriverException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WebDriverException exception)
        {
            throw new TransientLeb2Exception(
                "The browser could not complete the LEB2 semester request.",
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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            driverLease = new DriverLease(CreateDriver());
            var driver = driverLease.Driver;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((DriverLease)state!).Dispose(),
                driverLease);
            cancellationToken.ThrowIfCancellationRequested();
            SetCookieHeader(driver, token);

            await driver.Navigate().GoToUrlAsync(
                    $"{BaseUrl}/class?semester_id={semesterId}")
                .WaitAsync(cancellationToken);
            EnsureSessionIsActive(driver);

            return await ExtractClassesAsync(driver, cancellationToken);
        }
        catch (WebDriverException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WebDriverException exception)
        {
            throw new TransientLeb2Exception(
                "The browser could not complete the LEB2 class request.",
                exception);
        }
        finally
        {
            driverLease?.Dispose();
        }
    }

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

    private static OutboundRequestContext CreateSessionContext(
        string endpoint)
    {
        return new OutboundRequestContext(
            endpoint,
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

    private static async Task<List<int>> ExtractSemesterIdsAsync(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<IWebElement> semesterLinks;

        try
        {
            var wait = new WebDriverWait(driver, ElementWaitTimeout);
            semesterLinks = await RunDriverOperationAsync(
                () => wait.Until(d =>
                {
                    var links = d.FindElements(By.CssSelector("a[href*='semester_id=']"));
                    return links.Count > 0 ? links : null;
                }),
                cancellationToken);
        }
        catch (WebDriverTimeoutException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (WebDriverTimeoutException exception)
        {
            throw new StructuralParseException(
                "semesters.semester_links",
                "The semester links no longer match the expected structure.",
                exception);
        }

        var semesterIds = new List<int>();

        foreach (var link in semesterLinks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var href = link.GetAttribute("href");

            if (string.IsNullOrEmpty(href))
            {
                continue;
            }

            var match = Regex.Match(href, @"semester_id=(\d+)");

            if (match.Success && int.TryParse(match.Groups[1].Value, out var semesterId))
            {
                semesterIds.Add(semesterId);
            }
        }

        semesterIds = semesterIds.Distinct().ToList();

        if (semesterIds.Count == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_link_values",
                "The semester links did not contain recognizable IDs.");
        }

        return semesterIds;
    }

    private static async Task<List<ClassInfo>?> ExtractClassesAsync(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        IWebElement publishedContainer;

        try
        {
            var wait = new WebDriverWait(driver, ElementWaitTimeout);
            publishedContainer = await RunDriverOperationAsync(
                () => wait.Until(d => d.FindElements(By.CssSelector(
                        "#classListMain .class-list__row.class-publish"))
                    .FirstOrDefault()),
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

        var publishedItems = await RunDriverOperationAsync(
            () => publishedContainer.FindElements(By.XPath("./*")),
            cancellationToken);

        if (publishedItems.Count == 0)
        {
            return new List<ClassInfo>();
        }

        var classes = new List<ClassInfo>();

        foreach (var publishedItem in publishedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var classCards = await RunDriverOperationAsync(
                () => publishedItem.FindElements(By.XPath(
                    "self::div[contains(concat(' ', normalize-space(@class), ' '), ' whole-card ')]"
                    + "/div[contains(concat(' ', normalize-space(@class), ' '), ' class-card ')"
                    + " and contains(concat(' ', normalize-space(@class), ' '), ' card ')"
                    + " and @name and @data-url]")),
                cancellationToken);

            if (classCards.Count != 1)
            {
                throw new StructuralParseException(
                    "classes.class_card_pairing",
                    "LEB2 returned malformed published class card markup.");
            }

            var classCard = classCards.Single();
            var classCodes = await RunDriverOperationAsync(
                () => classCard.FindElements(By.CssSelector(
                    "p[name='code'].card-title-class-list")),
                cancellationToken);
            var classIdWithName = classCard.GetAttribute("name");
            var idMatch = Regex.Match(classIdWithName ?? string.Empty, @"^card-(\d+)$");

            if (classCodes.Count != 1
                || string.IsNullOrWhiteSpace(classIdWithName)
                || !idMatch.Success
                || !int.TryParse(idMatch.Groups[1].Value, out var classId)
                || string.IsNullOrWhiteSpace(classCodes.Single().Text))
            {
                throw new StructuralParseException(
                    "classes.class_card_values",
                    "The class cards did not contain recognizable class data.");
            }

            classes.Add(new ClassInfo
            {
                Id = classId,
                Name = classCodes.Single().Text.Trim()
            });
        }

        return classes;
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
