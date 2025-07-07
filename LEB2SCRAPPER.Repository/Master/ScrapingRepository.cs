using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Models.Authentication;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Exceptions.ScrapingCustomException;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium.DevTools;
using DevToolsSessionDomains = OpenQA.Selenium.DevTools.V138.DevToolsSessionDomains;
using Network = OpenQA.Selenium.DevTools.V138.Network;

namespace LEB2SCRAPPER.Repository.Master;

public class ScrapingRepository : IScrapingRepository
{
    private readonly string _baseUrl = "https://app.leb2.org";
    private readonly string _loginUrl = "https://signin.leb2.org/login";
    private readonly ChromeOptions _chromeOptions = new ChromeOptions();
    protected DevToolsSessionDomains? devToolsSession;
    protected IWebDriver driver;

    public ScrapingRepository()
    {
        _chromeOptions.AddArgument("--headless");
        _chromeOptions.AddArgument("--no-sandbox");
        _chromeOptions.AddArgument("--disable-dev-shm-usage");
        _chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/");
        driver = new ChromeDriver(_chromeOptions);
    }

    public async Task<string?> GetCookieAsync(Credentials credentials)
    {
        if (credentials == null || string.IsNullOrEmpty(credentials.Username) || string.IsNullOrEmpty(credentials.Password))
        {
            throw new ScrapingCustomException("Credentials must be provided.");
        }

        driver = new ChromeDriver(_chromeOptions);
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

        try
        {
            await driver.Navigate().GoToUrlAsync(_loginUrl);

            await Task.Run(() => wait.Until(d => d.Title.Contains("LEB2")));
            await Task.Run(() => wait.Until(d => d.FindElement(By.Id("input-1"))));

            var usernameField = driver.FindElement(By.Id("input-1"));
            usernameField.SendKeys(credentials.Username);

            var passwordField = driver.FindElement(By.Id("input-4"));
            passwordField.SendKeys(credentials.Password);

            var loginButton = driver.FindElement(By.CssSelector(".v-btn.v-btn--block.v-btn--elevated.v-theme--light.bg-primary.v-btn--density-default.v-btn--size-default.v-btn--variant-elevated.mt-6"));
            loginButton.Click();

            await Task.Run(() => wait.Until(d => d.Url.Contains("https://app.leb2.org/")));

            // Get cookies
            var cookies = driver.Manage().Cookies.AllCookies;
            var cookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

            driver.Quit();

            if (string.IsNullOrEmpty(cookieString))
            {
                throw new ScrapingCustomException("Failed to retrieve cookies. Please check your credentials.");
            }

            return cookieString;
        }
        catch (Exception ex)
        {

            throw new ScrapingCustomException("An error occurred while scraping the cookie.", ex);
        }
    }

    public async Task<List<int>?> GetSemesterIdsAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ScrapingCustomException("Token must be provided.");

        var headers = new Network.Headers
        {
            { "X-Requested-With", "XMLHttpRequest" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" },
            { "Cookie", token }
        };

        driver = new ChromeDriver(_chromeOptions);

        IDevTools? devTools = driver as IDevTools;

        var session = devTools?.GetDevToolsSession();
        devToolsSession = session?.GetVersionSpecificDomains<DevToolsSessionDomains>();
        devToolsSession?.Network.Enable(new Network.EnableCommandSettings());
        devToolsSession?.Network.SetExtraHTTPHeaders(new Network.SetExtraHTTPHeadersCommandSettings()
        {
            Headers = headers
        });

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

        try
        {
            await driver.Navigate().GoToUrlAsync($"{_baseUrl}/class");

            await Task.Run(() => wait.Until(d => d.FindElements(By.TagName("body")).Count > 0));

            var semesterIds = await ExtractSemesterIdsAsync(driver);

            driver.Quit();

            return semesterIds;
        }
        catch (Exception)
        {

            throw new ScrapingCustomException("Failed to get semester IDs. Please check your token.");
        }
    }

    public async Task<List<ClassInfo>?> GetClassesBySemesterIdAsync(int semesterId, string token)
    {
        if (semesterId <= 0 || string.IsNullOrEmpty(token))
            throw new ScrapingCustomException("Semester ID and Token must be provided.");

        var headers = new Network.Headers
        {
            { "X-Requested-With", "XMLHttpRequest" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" },
            { "Cookie", token }
        };

        driver = new ChromeDriver(_chromeOptions);

        IDevTools? devTools = driver as IDevTools;

        var session = devTools?.GetDevToolsSession();
        devToolsSession = session?.GetVersionSpecificDomains<DevToolsSessionDomains>();
        devToolsSession?.Network.Enable(new Network.EnableCommandSettings());
        devToolsSession?.Network.SetExtraHTTPHeaders(new Network.SetExtraHTTPHeadersCommandSettings()
        {
            Headers = headers
        });

        try
        {
            await driver.Navigate().GoToUrlAsync($"{_baseUrl}/class?semester_id={semesterId}");

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            await Task.Run(() => wait.Until(d => d.FindElements(By.TagName("body")).Count > 0));

            // Extract classes from the page
            var classes = await ExtractClasses(driver);
            driver.Quit();

            return classes;
        }
        catch (Exception)
        {
            throw new ScrapingCustomException($"Failed to Get Class Data or semester Id {semesterId} is not valid.");
        }
    }



    /*********************
     *   HELPER METHODS  *
     *********************/

    private static async Task<List<int>> ExtractSemesterIdsAsync(IWebDriver driver)
    {
        try
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

            await Task.Run(() => wait.Until(d => d.FindElements(By.CssSelector("a[href*='semester_id=']")).Count > 0));

            // Find all semester links
            var semesterLinks = driver.FindElements(By.CssSelector("a[href*='semester_id=']"));
            var semesterIds = new List<int>();

            foreach (var link in semesterLinks)
            {
                var href = link.GetAttribute("href");
                if (!string.IsNullOrEmpty(href))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(href, @"semester_id=(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var semesterId))
                    {
                        semesterIds.Add(semesterId);
                    }
                }
            }

            semesterIds = semesterIds.Distinct().ToList();

            return semesterIds;
        }
        catch (Exception)
        {
            throw new ScrapingCustomException("Failed to extract semester IDs.");
        }
    }

    private static Task<List<ClassInfo>?> ExtractClasses(IWebDriver driver)
    {
        var classes = new List<ClassInfo>();

        try
        {
            var classCards = driver.FindElements(By.CssSelector(".class-card"));
            var classNames = driver.FindElements(By.Name("code"));

            for (int i = 0; i < Math.Min(classCards.Count, classNames.Count); i++)
            {

                var cardId = classCards[i];
                var cardName = classNames[i];
                var classIdWithName = cardId.GetAttribute("name");
                var className = cardName.Text;

                if (!string.IsNullOrEmpty(classIdWithName) && !string.IsNullOrEmpty(className))
                {
                    // Extract the number after "card-"
                    var parts = classIdWithName.Split('-');
                    if (parts.Length > 1 && int.TryParse(parts[1], out var classId))
                    {
                        classes.Add(new ClassInfo
                        {
                            Id = classId,
                            Name = className.Trim()
                        });
                    }
                }
            }

            return Task.FromResult<List<ClassInfo>?>(classes);

        }
        catch (Exception)
        {
            throw new ScrapingCustomException("Failed to extract classes.");
        }
    }
}
