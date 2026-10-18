using System.Text.Json.Nodes;

namespace Poe2Pricer.Core.Market;

// Prices as the overlay control sees them. Adapters: NinjaPrices (poe.ninja) and FrozenPrices
// (the golden-master check and tests). Step 6.5 replaces the poe.ninja side.
public interface IPrices
{
    // The book to price with; the very first load takes ~20 s. Faults: poe.ninja error.
    Task<PriceBook> GetAsync();

    // The book that is there now, null while the first load runs.
    PriceBook? Ready { get; }
}

// Prices that are there are used at once; a stale set is refreshed in the background and
// shows with the next check. Only the very first load is waited for (it takes ~20 s:
// 23 categories, one request per second). One load at a time: another league cancels the
// running load, and until its prices are there none are given out — better none than the
// wrong league's. "Neu laden" asks poe.ninja again even within MaxAge; the
// old prices stay until the new ones are there. Whoever waits for prices (the overlay) is not
// cancelled by a reload or another league: the wait ends with the next load that succeeds for
// the league then chosen, or with its error (code review).
public sealed class NinjaPrices : IPrices, IDisposable
{
    private readonly NinjaClient ninja;
    private readonly Lock gate = new(); // the overlay control and the app window both ask
    private string? league;
    private Task<PriceBook>? loading;
    private CancellationTokenSource? cancel;
    private TaskCompletionSource<PriceBook>? waiting; // callers while there are no prices
    private DateTimeOffset loadedAt;
    private volatile PriceBook? current;

    private readonly TimeSpan retryPause;

    public NinjaPrices(Settings settings, NinjaClient? client = null, TimeSpan? retryPause = null)
    {
        league = settings.League;
        ninja = client ?? new NinjaClient();
        this.retryPause = retryPause ?? Tuning.Control.PricesRetryPause;
    }

    // A refresh in the background failed; the old prices stay in use.
    public event Action<Exception>? Failed;

    public PriceBook? Ready => current;

    // true: another league, its prices are being loaded now.
    public bool Apply(Settings s)
    {
        lock (gate)
        {
            if (s.League == league)
                return false;
            league = s.League;
            current = null;
            Start(fresh: false, background: false);
            return true;
        }
    }

    public Task<PriceBook> GetAsync()
    {
        lock (gate)
        {
            if (current is { } book)
            {
                if (DateTimeOffset.UtcNow - loadedAt > NinjaClient.MaxAge && loading is not { IsCompleted: false })
                    Start(fresh: false, background: true);
                return Task.FromResult(book);
            }
            // A load that failed is not tried again at once: without a network every look of
            // the automatic scan would start one, a request and an error line each second.
            // Until the pause is over the callers get the error of the last one.
            if (loading is { IsFaulted: true } failed && DateTimeOffset.UtcNow - loadedAt < retryPause)
                return Task.FromException<PriceBook>(failed.Exception!.GetBaseException());
            if (loading is null || loading.IsCompleted)
                Start(fresh: false, background: false);
            waiting ??= new TaskCompletionSource<PriceBook>(TaskCreationOptions.RunContinuationsAsynchronously);
            return waiting.Task;
        }
    }

    // The load itself (for the status line): cancelled when replaced.
    public Task<PriceBook> ReloadAsync()
    {
        lock (gate)
            return Start(fresh: true, background: false);
    }

    // Under the gate. A load still running is cancelled: only one at a time.
    private Task<PriceBook> Start(bool fresh, bool background)
    {
        cancel?.Cancel();
        var source = new CancellationTokenSource();
        cancel = source;
        var token = source.Token;
        var wanted = league;
        loadedAt = DateTimeOffset.UtcNow;
        var task = Task.Run(
            async () =>
            {
                var book = await ninja.PricesAsync(wanted ?? NinjaClient.DefaultLeague(await ninja.LeaguesAsync(token)), token, fresh);
                lock (gate)
                {
                    token.ThrowIfCancellationRequested();
                    current = book;
                    waiting?.TrySetResult(book);
                    waiting = null;
                }
                return book;
            },
            token
        );
        _ = task.ContinueWith(
            t =>
            {
                var error = t.Exception!.GetBaseException();
                lock (gate)
                {
                    if (!ReferenceEquals(loading, t))
                        return; // replaced meanwhile
                    loadedAt = DateTimeOffset.UtcNow; // the pause before the next try counts from the failure
                    waiting?.TrySetException(error);
                    waiting = null;
                }
                if (background)
                    Failed?.Invoke(error);
            },
            TaskContinuationOptions.OnlyOnFaulted
        );
        loading = task;
        return task;
    }

    public void Dispose()
    {
        cancel?.Cancel();
        ninja.Dispose();
    }
}

// The poe.ninja cache files as NinjaClient wrote them, read once without any request
// (work/golden-fixtures/ninja/<league>/<category>.json).
public sealed class FrozenPrices : IPrices
{
    public FrozenPrices(string fixtures)
    {
        var league = Directory.GetDirectories(Path.Combine(fixtures, "ninja")).Single();
        var book = new PriceBook(Path.GetFileName(league));
        JsonNode Body(string type) =>
            JsonNode.Parse(JsonNode.Parse(File.ReadAllText(Path.Combine(league, type + ".json")))!["Body"]!.GetValue<string>())!;
        foreach (var type in NinjaClient.ExchangeTypes)
            book.AddExchange(Body(type), type);
        foreach (var type in NinjaClient.UniqueTypes)
            book.AddUniques(Body(type), type);
        Ready = book;
    }

    public PriceBook Ready { get; }

    PriceBook? IPrices.Ready => Ready;

    public Task<PriceBook> GetAsync() => Task.FromResult(Ready);
}
