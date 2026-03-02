using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using PaperNexus.Core;

namespace PaperNexus;

internal class HttpWallpaperSourceService
{
    private readonly ILogger<HttpWallpaperSourceService> _logger;

    public HttpWallpaperSourceService(ILogger<HttpWallpaperSourceService> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    public async Task<List<WallpaperImage>> GetImages(WallpaperSource source)
    {
        _logger.LogInformation($"Getting images from source '{source.Name}': {source.Url}");
        var watch = Stopwatch.StartNew();
        using var client = new HttpClient();
        using var getResponse = await client.GetAsync(source.Url);
        if (!getResponse.IsSuccessStatusCode)
        {
            var msg = await getResponse.Content.ReadAsStringAsync();
            throw new Exception(msg);
        }

        var json = await getResponse.Content.ReadAsStringAsync();
        var images = ParseImages(source, json);
        _logger.LogInformation("Complete: " + new { watch.Elapsed });
        return images;
    }

    private static List<WallpaperImage> ParseImages(WallpaperSource source, string json)
    {
        var token = JToken.Parse(json);
        var imageUrls = token.SelectTokens(source.ImageUrlJPath).Select(t => t.Value<string>() ?? string.Empty).ToList();
        var titles = token.SelectTokens(source.TitleJPath).Select(t => t.Value<string>() ?? string.Empty).ToList();

        var images = imageUrls
            .Zip(titles, (url, title) => new WallpaperImage { ImageUrl = url, Title = title })
            .ToList();

        if (!string.IsNullOrEmpty(source.ImageUrlRegex))
        {
            var regex = new Regex(source.ImageUrlRegex);
            images = images.Where(img => regex.IsMatch(img.ImageUrl)).ToList();
        }

        return images;
    }
}

public class WallpaperImage
{
    public string ImageUrl { get; set; }
    public string Title { get; set; }
}
