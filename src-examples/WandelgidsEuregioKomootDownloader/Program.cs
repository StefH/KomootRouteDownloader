using System.Runtime.Loader;
using System.Text.RegularExpressions;
using KomootRouteDownloader;
using Microsoft.Extensions.Logging;

namespace WandelgidsEuregioKomootDownloader;

public static class Program
{
    private const string SourcePage = "https://www.wandelgidseuregio.nl/komoot/";
    private const string DownloadFolder = "downloads";

    private static readonly KomootRouteDownloadServiceOptions Options = new()
    {
        PuppeteerHeadless = true,
        KomootEmail = Environment.GetEnvironmentVariable("KOMOOT_USER") ?? throw new InvalidOperationException("KOMOOT_USER environment variable is not set."),
        KomootPassword = Environment.GetEnvironmentVariable("KOMOOT_PASSWORD") ?? throw new InvalidOperationException("KOMOOT_PASSWORD environment variable is not set.")
    };
    private static readonly ILogger Logger = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    }).CreateLogger(nameof(Program));
    private static readonly CancellationTokenSource CancellationTokenSource = new();

    static async Task Main()
    {
        Console.CancelKeyPress += (s, e) =>
        {
            CancellationTokenSource.Cancel();
            e.Cancel = true;
        };

        AssemblyLoadContext.Default.Unloading += ctx =>
        {
            CancellationTokenSource.Cancel();
        };

        Directory.CreateDirectory(DownloadFolder);

        var links = await GetKomootTourLinksAsync(CancellationTokenSource.Token);
        Logger.LogInformation("Found {Count} tour links.", links.Length);

        if (links.Length == 0)
        {
            Logger.LogInformation("Nothing to download.");
            return;
        }

        var downloadService = new KomootRouteDownloadService(Microsoft.Extensions.Options.Options.Create(Options), Logger);

        var files = await downloadService.DownloadRoutesAsync(links.Skip(20).Take(10).ToArray(), CancellationTokenSource.Token);
        foreach (var (fileName, gpxContent) in files)
        {
            await File.WriteAllBytesAsync(Path.Combine(DownloadFolder, fileName), gpxContent, CancellationTokenSource.Token);
        }

        // Rename GPX files to remove date and ID prefix
        RenameGpxFiles(CancellationTokenSource.Token);

        Logger.LogInformation("Finished.");
    }

    private static async Task<string[]> GetKomootTourLinksAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        Logger.LogInformation("Reading {Page} page...", SourcePage);

        var html = await client.GetStringAsync(SourcePage, cancellationToken);

        var matches = Regex.Matches(
            html,
            @"https:\/\/www\.komoot\.(?:nl|com)\/(?:[a-z]{2}-[a-z]{2}\/)?tour\/(\d+)",
            RegexOptions.IgnoreCase);

        return matches
            .Select(m => $"https://www.komoot.com/tour/{m.Groups[1].Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RenameGpxFiles(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Renaming GPX files...");

        var downloadPath = Path.GetFullPath(DownloadFolder);
        if (!Directory.Exists(downloadPath))
        {
            Logger.LogInformation("Download folder does not exist.");
            return;
        }

        var gpxFiles = Directory.GetFiles(downloadPath, "*.gpx");
        if (gpxFiles.Length == 0)
        {
            Logger.LogWarning("No GPX files found to rename.");
            return;
        }

        // Pattern to match: YYYY-MM-DD_XXXXXXXXX_<actual_filename>.gpx
        // We want to keep only <actual_filename>.gpx
        var pattern = new Regex(@"^\d{4}-\d{2}-\d{2}_\d+_(.+)$", RegexOptions.IgnoreCase);

        foreach (var filePath in gpxFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogInformation("Renaming cancelled.");
                break;
            }

            var fileName = Path.GetFileName(filePath);
            var match = pattern.Match(fileName);

            if (match.Success)
            {
                var newFileName = match.Groups[1].Value;
                var newFilePath = Path.Combine(downloadPath, newFileName);

                try
                {
                    File.Move(filePath, newFilePath);
                    Logger.LogInformation("Renamed: {OldFileName} -> {NewFileName}", fileName, newFileName);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to rename {FileName}", fileName);
                }
            }
            else
            {
                Logger.LogInformation("Skipped (no match): {FileName}", fileName);
            }
        }

        Logger.LogInformation("GPX file renaming completed.");
    }
}