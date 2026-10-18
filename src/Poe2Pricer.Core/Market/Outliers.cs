namespace Poe2Pricer.Core.Market;

// Colours prices relative to the current roll: Tukey fences on log prices (1.5 x IQR).
// Items below the "hide under X ex" floor are left out before this is computed.
public static class Outliers
{
    public enum Level { Low, Normal, High }

    public static Level[] Classify(IReadOnlyList<decimal> exalted)
    {
        if (exalted.Count < 4)
            return exalted.Select(_ => Level.Normal).ToArray();
        var logs = exalted.Select(p => Math.Log((double)Math.Max(p, 0.01m))).ToArray();
        var sorted = logs.Order().ToArray();
        double q1 = Quantile(sorted, .25),
            q3 = Quantile(sorted, .75),
            fence = 1.5 * (q3 - q1);
        return logs.Select(l => l > q3 + fence ? Level.High : l < q1 - fence ? Level.Low : Level.Normal).ToArray();
    }

    private static double Quantile(double[] sorted, double q)
    {
        double pos = (sorted.Length - 1) * q;
        int i = (int)pos;
        return i + 1 < sorted.Length ? sorted[i] + (pos - i) * (sorted[i + 1] - sorted[i]) : sorted[i];
    }
}
