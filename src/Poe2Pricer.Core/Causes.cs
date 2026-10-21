namespace Poe2Pricer.Core;

public static class Causes
{
    // What lies under an exception, for the log: the messages of its inner exceptions, each
    // once, joined by " ← ". The program's own message ("poe.ninja: not reachable") says what
    // failed; this says why ("No such host is known", "The SSL connection could not be
    // established", "HttpClient.Timeout of 20 seconds elapsing"), which tells a firewall from
    // a missing network or a slow answer (a report on Reddit, 21.10.2026). Empty when there
    // is nothing under it.
    public static string Of(Exception e)
    {
        var seen = new List<string>();
        for (var inner = e is AggregateException a ? a.GetBaseException().InnerException : e.InnerException; inner is not null; inner = inner.InnerException)
        {
            string message = inner.Message.Trim();
            if (message.Length > 0 && !seen.Contains(message))
                seen.Add(message);
        }
        return seen.Count == 0 ? "" : " ← " + string.Join(" ← ", seen);
    }
}
