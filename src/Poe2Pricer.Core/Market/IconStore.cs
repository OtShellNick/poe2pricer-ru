using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using OpenCvSharp;

namespace Poe2Pricer.Core.Market;

// Item art from poe.ninja (GGG material: fetched at run time and kept on disk, not shipped).
// Stored under %LOCALAPPDATA%\poe2pricer\icons\<name>.png, loaded once per run.
public sealed partial class IconStore : IDisposable
{
    private readonly string folder;
    private readonly HttpClient client;
    private readonly ConcurrentDictionary<string, Mat?> loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim fetching = new(1, 1);

    // fetch: false for the golden-master check and the tests — only what is on disk counts,
    // nothing is downloaded into the frozen data.
    private readonly bool fetch;

    // handler: for tests, answers instead of the image server.
    public IconStore(string? folder = null, bool fetch = true, HttpMessageHandler? handler = null)
    {
        this.folder = folder ?? Path.Combine(Settings.Folder, "icons");
        this.fetch = fetch;
        client = new HttpClient(handler ?? new HttpClientHandler()) { BaseAddress = new Uri("https://web.poecdn.com"), Timeout = Tuning.Control.RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"poe2pricer/{AppInfo.Version} (local, private)");
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex Unsafe();

    private string FileOf(string name) => Path.Combine(folder, Unsafe().Replace(name, "_") + ".png");

    public bool Has(string name) => loaded.TryGetValue(name, out var m) ? m is not null : File.Exists(FileOf(name));

    // BGRA as poe.ninja delivers it; null when not on disk.
    public Mat? Get(string name)
    {
        if (loaded.TryGetValue(name, out var there))
            return there;
        // One at a time: two callers at once would each read the file, and one picture would
        // be left over.
        lock (reading)
            return loaded.GetOrAdd(name, Read);
    }

    private readonly Lock reading = new();

    private Mat? Read(string n)
    {
        var file = FileOf(n);
        if (!File.Exists(file))
            return null;
        var m = Cv2.ImRead(file, ImreadModes.Unchanged);
        if (m.Empty() || m.Channels() != 4)
        {
            m.Dispose();
            // A file that is there but no picture of the kind (cut off, no alpha): not
            // waited for in this run, and removed so that the next run fetches it anew —
            // otherwise every free stash slot would stay "icons loading" in every run.
            tried[n] = true;
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return null;
        }
        return m;
    }

    // Tried in this run: an image poe.ninja cannot deliver is not waited for again.
    private readonly ConcurrentDictionary<string, bool> tried = new(StringComparer.OrdinalIgnoreCase);

    // Without fetching nothing is waited for: what is not on disk will not come.
    public bool Tried(string name) => !fetch || tried.ContainsKey(name);

    // While fetching: images done and to do (for the status line).
    public event Action<int, int>? Loading;

    // A fetch brought images that were not there before: what was told without them (a stash
    // slot named by comparing with the art there was) is to be told anew.
    public event Action? Arrived;

    // Fetches what is missing, four at a time; one run at a time. Failures are skipped (the
    // slot then stays without a label) and tried again with the next call.
    public async Task EnsureAsync(IEnumerable<(string Name, string Path)> items, CancellationToken token = default)
    {
        var missing = items.Where(i => !File.Exists(FileOf(i.Name))).DistinctBy(i => i.Name).ToArray();
        if (missing.Length == 0)
            return;
        if (!fetch)
            return;
        await fetching.WaitAsync(token);
        int done = 0,
            arrived = 0;
        try
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No folder, no art: nothing is waited for (else stash slots stay "?").
                foreach (var item in missing)
                    tried[item.Name] = true;
                return;
            }
            await Parallel.ForEachAsync(
                missing,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (item, t) =>
                {
                    // Every item counts as tried and done, whatever happens to it (code review:
                    // one unexpected error left the rest waiting, and the count never finished).
                    try
                    {
                        var file = FileOf(item.Name);
                        if (File.Exists(file))
                            return; // fetched meanwhile by an overlapping run
                        var bytes = await client.GetByteArrayAsync(item.Path, t);
                        await File.WriteAllBytesAsync(file + ".part", bytes, t);
                        File.Move(file + ".part", file, overwrite: true);
                        // Under the lock of Get: a reader that has just seen "no file"
                        // stores that first and it is removed here, not the other way round —
                        // else the image would count as missing for the rest of the run.
                        lock (reading)
                            loaded.TryRemove(item.Name, out _);
                        Interlocked.Increment(ref arrived);
                    }
                    catch (Exception) when (!t.IsCancellationRequested) { }
                    finally
                    {
                        tried[item.Name] = true;
                        Loading?.Invoke(Interlocked.Increment(ref done), missing.Length);
                    }
                }
            );
        }
        finally
        {
            fetching.Release();
        }
        if (arrived > 0)
            Arrived?.Invoke();
    }

    public void Dispose()
    {
        client.Dispose();
        foreach (var m in loaded.Values)
            m?.Dispose();
    }
}
