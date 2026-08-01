namespace PaperNexus.Core;

// Records which easter eggs have been triggered, so the checklist can show progress.
// Every trigger routes through here rather than writing settings itself, which keeps the
// discovery bookkeeping in one place and out of the views.
public static class EasterEggProgress
{
    // Serialises concurrent records: two eggs firing close together would otherwise both
    // load settings, each add only their own id, and the second save would drop the first.
    private static readonly SemaphoreSlim _gate = new(1, 1);

    // Returns true when this is the first time the egg has been found, so the caller can
    // celebrate a new discovery differently from a repeat.
    public static async Task<bool> RecordAsync(string eggId)
    {
        if (string.IsNullOrWhiteSpace(eggId))
            return false;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await WallpaperNexusSettings.LoadAsync().ConfigureAwait(false);
            if (settings.DiscoveredEasterEggs.Contains(eggId, StringComparer.OrdinalIgnoreCase))
                return false;

            settings.DiscoveredEasterEggs.Add(eggId);
            await settings.SaveAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // Losing a checklist tick is not worth interrupting the animation the user is
            // currently watching.
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // The catalog paired with whether each entry has been found, in catalog order.
    public static IReadOnlyList<(EasterEggDefinition Egg, bool Found)> Build(IEnumerable<string> discoveredIds)
    {
        var discovered = new HashSet<string>(discoveredIds, StringComparer.OrdinalIgnoreCase);
        return EasterEggCatalog.All
            .Select(egg => (egg, discovered.Contains(egg.Id)))
            .ToList();
    }

    public static int CountFound(IEnumerable<string> discoveredIds) =>
        Build(discoveredIds).Count(entry => entry.Found);
}
