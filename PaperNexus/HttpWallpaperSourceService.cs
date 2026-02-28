using Excogitated.Core;

namespace Excogitated.WallpaperNexus;

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
        var response = JsonConvert.DeserializeObject<List<WallpaperImage>>(json);
        if (response is null)
        {
            throw new Exception($"Source '{source.Name}' did not return valid json: " + json);
        }
        _logger.LogInformation("Complete: " + new { watch.Elapsed });
        return response;
    }
}

public class WallpaperImage
{
    public string ImageUrl { get; set; }
    public string Title { get; set; }
}
