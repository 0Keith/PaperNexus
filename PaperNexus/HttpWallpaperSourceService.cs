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

    // Fetches the JSON feed from the wallpaper source URL and returns all images
    // extracted via the source's configured JPath expressions.
    public async Task<List<WallpaperImage>> GetImages(WallpaperSource source)
    {
        _logger.LogInformation($"Getting images from source '{source.Name}': {source.Url}");
        var watch = Stopwatch.StartNew();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var getResponse = await client.GetAsync(source.Url);
        if (!getResponse.IsSuccessStatusCode)
        {
            var msg = await getResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)getResponse.StatusCode} {getResponse.StatusCode} from '{source.Url}': {msg}");
        }

        var json = await getResponse.Content.ReadAsStringAsync();
        var images = ParseImages(source, json);
        _logger.LogInformation("Complete: " + new { watch.Elapsed });
        return images;
    }

    // Uses the source's JPath expressions to extract parallel lists of image URLs and titles
    // from the raw JSON, then zips them into WallpaperImage records.
    private static List<WallpaperImage> ParseImages(WallpaperSource source, string json)
    {
        var token = JToken.Parse(json);
        var imageUrls = token.SelectTokens(source.ImageUrlJPath).Select(t => t.Value<string>() ?? string.Empty).ToList();
        var titles = token.SelectTokens(source.TitleJPath).Select(t => t.Value<string>() ?? string.Empty).ToList();

        // Zip stops at the shorter list, so mismatched result counts are handled gracefully
        return imageUrls
            .Zip(titles, (url, title) => new WallpaperImage { ImageUrl = url, Title = title })
            .ToList();
    }
}

public class WallpaperImage
{
    public string ImageUrl { get; set; }
    public string Title { get; set; }
}
