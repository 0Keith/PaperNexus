using Newtonsoft.Json.Linq;
using PaperNexus.Core;

namespace PaperNexus;

internal class HttpWallpaperSourceService
{
    private readonly ILogger<HttpWallpaperSourceService> _logger;

    // A single long-lived HttpClient shared across all calls to avoid socket exhaustion.
    // HttpClient is thread-safe for concurrent requests; creating one per call drains the
    // ephemeral port pool because disposed clients leave sockets in TIME_WAIT for ~4 min.
    // The service is registered as a singleton, so this instance lives for the process lifetime.
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public HttpWallpaperSourceService(ILogger<HttpWallpaperSourceService> logger)
    {
        _logger = logger.ThrowIfNull();
    }

    // Fetches the JSON feed from the wallpaper source URL and returns all images
    // extracted via the source's configured JPath expressions.
    // The optional cancellationToken is forwarded to the HTTP call so callers
    // can cancel in-flight requests (e.g. when the Test dialog is closed mid-request).
    public async Task<List<WallpaperImage>> GetImagesAsync(WallpaperSource source, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting images from source '{Name}': {Url}", source.Name, source.Url);
        var watch = Stopwatch.StartNew();
        using var getResponse = await _client.GetAsync(source.Url, cancellationToken);
        if (!getResponse.IsSuccessStatusCode)
        {
            var msg = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)getResponse.StatusCode} {getResponse.StatusCode} from '{source.Url}': {msg}");
        }

        var json = await getResponse.Content.ReadAsStringAsync(cancellationToken);
        var images = ParseImages(source, json);
        _logger.LogInformation("Got {Count} image(s) from '{Name}' in {Elapsed}", images.Count, source.Name, watch.Elapsed);
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
