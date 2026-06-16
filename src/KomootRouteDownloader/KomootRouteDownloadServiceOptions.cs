using System.ComponentModel.DataAnnotations;

namespace KomootRouteDownloader;

public class KomootRouteDownloadServiceOptions
{
    [Required]
    public string KomootApiBaseUrl { get; set; } = "https://www.komoot.com/api/v007";

    [Required]
    public required string KomootEmail { get; set; }

    [Required]
    public required string KomootPassword { get; set; }

    [Required]
    public string BrowserDataFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "browser-data");
    
    [Required]
    public string ScreenshotFolder { get; set; } = "screenshots";

    [Required]
    public string ErrorFolder { get; set; } = "errors";

    /// <summary>
    /// Whether to run browser in headless mode. Defaults to <c>true</c> unless the devtools option is <c>true</c>.
    /// </summary>
    public bool PuppeteerHeadless { get; set; } = true;

    public bool PuppeteerSaveScreenshots { get; set; } = true;
}