## Info
This project uses PuppeteerSharp to download routes from Komoot.

### NuGet
[![NuGet Badge](https://img.shields.io/nuget/v/KomootRouteDownloader)](https://www.nuget.org/packages/KomootRouteDownloader)

### Usage

``` c#
var downloadService = new KomootRouteDownloadService(options, logger);

var link = "https://www.komoot.com/tour/123456789";
var files = await downloadService.DownloadRoutesAsync([link], cancellationToken);
foreach (var (fileName, gpxContent) in files)
{
    await File.WriteAllBytesAsync(Path.Combine(DownloadFolder, fileName), gpxContent, CancellationTokenSource.Token);
}
```

### Sponsors

[Entity Framework Extensions](https://entityframework-extensions.net/?utm_source=StefH) and [Dapper Plus](https://dapper-plus.net/?utm_source=StefH) are major sponsors and proud to contribute to the development of **IHttpClient**.

[![Entity Framework Extensions](https://raw.githubusercontent.com/StefH/resources/main/sponsor/entity-framework-extensions-sponsor.png)](https://entityframework-extensions.net/bulk-insert?utm_source=StefH)

[![Dapper Plus](https://raw.githubusercontent.com/StefH/resources/main/sponsor/dapper-plus-sponsor.png)](https://dapper-plus.net/bulk-insert?utm_source=StefH)
