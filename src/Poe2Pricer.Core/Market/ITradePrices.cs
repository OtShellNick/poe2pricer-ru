using Poe2Pricer.Core.Tablets;
using Poe2Pricer.Core.Waystones;

namespace Poe2Pricer.Core.Market;

// The lowest asking price on the trade site for items like the one read. Divine: the price
// of the cheapest listing that is not an outlier, in divine orbs; Listings: how many the
// search found; Query: what was asked (for the log and the diagnosis).
public sealed record TradePrice(decimal Divine, int Listings, DateTimeOffset At, string Query);

// Why the site was not asked, or did not answer: its limit would take too long to pass
// (Limit), it refused (Refused, 429), poe.ninja's rates are not loaded yet (NoRates).
// Unreachable: the last question failed (an error status other than 429, the network, an
// answer that was no JSON) and the site is left alone for a while (TradeClient.Down).
public enum NotAsked { None, Limit, Refused, NoRates, Unreachable }

// What a question to the trade site came to: a price; none (the site found no listing it
// could price: Price null, Why None); or not asked, with why and when to try again.
// "No listing" and "not asked" read alike before (review before 1.2.0): a busy site looked
// like an item nobody sells.
// Detail: what went wrong when the site was unreachable ("405", "timeout").
public sealed record TradeAnswer(TradePrice? Price, NotAsked Why = NotAsked.None, TimeSpan RetryIn = default, string? Detail = null)
{
    public static readonly TradeAnswer NoListing = new((TradePrice?)null);

    public bool Asked => Why == NotAsked.None;

    // Why the site was not asked, for the status line and the Waystones/Tablets tab.
    public string Reason() => Why switch
    {
        NotAsked.NoRates => Texts.Get("TradeNoRates"),
        NotAsked.Unreachable => Texts.Get("TradeUnreachable", Detail ?? "?", Math.Max(1, (int)Math.Ceiling(RetryIn.TotalSeconds))),
        _ => Texts.Get("TradeBusy", Math.Max(1, (int)Math.Ceiling(RetryIn.TotalSeconds))),
    };
}

// The trade site was not asked (TradeAnswer.Asked false), thrown where a price or nothing is
// all a caller can take (the profile price of the tabs); the message says why.
public sealed class TradeNotAskedException(string message) : Exception(message);

public interface ITradePrices
{
    // profile: the vocabulary of the question, null for the item itself (TradeQuery.ForReading);
    // tolerance: per cent (Tolerance). A failure of the site or the network comes back as
    // NotAsked.Unreachable (TradeClient); another source may still throw.
    Task<TradeAnswer> ForAsync(WaystoneReading reading, WaystoneProfile? profile, int tolerance, string league, CancellationToken token = default);

    Task<TradeAnswer> ForAsync(TabletReading reading, TabletProfile? profile, int tolerance, string league, CancellationToken token = default);
}
