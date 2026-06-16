using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Input;

namespace KomootRouteDownloader;

public class KomootRouteDownloadService(IOptions<KomootRouteDownloadServiceOptions> options, ILogger logger)
{
    public async Task<IReadOnlyList<(string FileName, byte[] GpxContent)>> DownloadRoutesAsync(string[] links, CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking for browser...");
        await new BrowserFetcher().DownloadAsync();
        logger.LogInformation("Browser check completed.");

        Directory.CreateDirectory(options.Value.ScreenshotFolder);
        Directory.CreateDirectory(options.Value.BrowserDataFolder);

        var chromeProfilePath = Path.Combine(options.Value.BrowserDataFolder, "chrome-profile");
        Directory.CreateDirectory(chromeProfilePath);

        var navigationOptions = new NavigationOptions
        {
            Timeout = 5_000,
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
            CancellationToken = cancellationToken
        };

        var waitForSelectorOptions = new WaitForSelectorOptions
        {
            Timeout = 5_000,
            Visible = true,
            CancellationToken = cancellationToken
        };

        var launchOptions = new LaunchOptions
        {
            Headless = options.Value.PuppeteerHeadless,
            UserDataDir = chromeProfilePath
        };

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);

        await using var page = await browser.NewPageAsync();

        if (options.Value.PuppeteerSaveScreenshots)
        {
            await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"1. NewPageAsync_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
        }

        await page.GoToAsync("https://www.komoot.com/signin", navigationOptions);

        if (options.Value.PuppeteerSaveScreenshots)
        {
            await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"2. GoToAsync_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
        }

        await Task.Delay(Random.Shared.Next(500, 1_000), cancellationToken);

        try
        {
            logger.LogInformation("Checking for existing session...");

            var continueWithUser = await page.QuerySelectorAsync("a[data-test-id='continue_with_user']");
            if (continueWithUser != null && await continueWithUser.IsVisibleAsync())
            {
                if (options.Value.PuppeteerSaveScreenshots)
                {
                    await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"3a. ContinueWithUser_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
                }
                await continueWithUser.ClickAsync();
            }
            else
            {
                if (options.Value.PuppeteerSaveScreenshots)
                {
                    await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"3b. SignIn_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
                }

                logger.LogInformation("No existing session found, performing login...");

                var email = await page.WaitForSelectorAsync("input[id='email']", waitForSelectorOptions);
                await email.TypeAsync(options.Value.KomootEmail, new TypeOptions { Delay = Random.Shared.Next(50, 100) });
                await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);

                var emailNext = await page.WaitForSelectorAsync("a[data-test-id='email_next']", waitForSelectorOptions);
                await emailNext.ClickAsync();

                var password = await page.WaitForSelectorAsync("input[id='password']", waitForSelectorOptions);
                await password.TypeAsync(options.Value.KomootPassword, new TypeOptions { Delay = Random.Shared.Next(50, 100) });
                await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);

                var passwordNext = await page.WaitForSelectorAsync("button[data-test-id='password_next']", waitForSelectorOptions);
                await passwordNext.ClickAsync();

                await page.WaitForNavigationAsync(navigationOptions);

                if (options.Value.PuppeteerSaveScreenshots)
                {
                    await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"3c. SignIn Completed_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
                }
            }

            await Task.Delay(Random.Shared.Next(1_000, 2_000), cancellationToken);

            try
            {
                var declineButton = await page.WaitForSelectorAsync("button[data-test-id='gdpr-banner-decline']", waitForSelectorOptions);
                if (declineButton != null && await declineButton.IsVisibleAsync())
                {
                    await declineButton.ClickAsync();
                    await Task.Delay(Random.Shared.Next(1_000, 2_000), cancellationToken);
                }

                if (options.Value.PuppeteerSaveScreenshots)
                {
                    await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"4a. GDPR Banner Declined_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
                }
            }
            catch
            {
                // GDPR banner not found, continue
                if (options.Value.PuppeteerSaveScreenshots)
                {
                    await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"4b. GDPR Banner Not Found_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
                }
            }
        }
        catch
        {
            logger.LogWarning("An error occurred during the login process. Screenshots have been saved for debugging.");
            if (options.Value.PuppeteerSaveScreenshots)
            {
                await page.ScreenshotAsync(Path.Combine(options.Value.ScreenshotFolder, $"Login_Error_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
            }
            throw;
        }

        logger.LogInformation("Starting downloads for {Count} links...", links.Length);

        using HttpClient client = await CreateHttpClient(page);

        var files = new List<(string FileName, byte[] GpxContent)>();

        foreach (var link in links)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var file = await DownloadAsync(client, link, cancellationToken);
            if (file != null)
            {
                files.Add(file.Value);
            }
        }

        await page.CloseAsync();

        logger.LogInformation("All downloads completed.");

        return files;
    }

    async Task<(string FileName, byte[] GpxContent)?> DownloadAsync(HttpClient client, string link, CancellationToken cancellationToken)
    {
        var id = link.Split('/').Last();
        var url = $"{options.Value.KomootApiBaseUrl}/tours/{id}.gpx";

        try
        {
            logger.LogInformation("Download initiated for {Link} ({Url})", link, url); // 
            //https://www.komoot.com/api/v007/tours/304722787.gpx?hl=en

            var responseMessage = await client.GetAsync(url, cancellationToken);
            responseMessage.EnsureSuccessStatusCode();

            var gpx = await responseMessage.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentDisposition = responseMessage.Content.Headers.ContentDisposition;
            var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName ?? $"{id}.gpx";

            logger.LogInformation("Download completed for {Link} ({Url}) ({FileName})", link, url, fileName);

            return (fileName, gpx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download failed for {Link} ({Url})", link, url);
        }

        return null;
    }

    private static async Task<HttpClient> CreateHttpClient(IPage page)
    {
        var cookies = await page.GetCookiesAsync();
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer()
        };

        foreach (var cookie in cookies)
        {
            try
            {
                handler.CookieContainer.Add(new Uri(page.Url), new Cookie(cookie.Name, cookie.Value));
            }
            catch
            {
                // Ignore any exceptions related to adding cookies, as they may be caused by invalid cookie formats or other issues.
            }
        }

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(await page.Browser.GetUserAgentAsync());
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return client;
    }
}