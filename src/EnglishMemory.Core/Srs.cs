namespace EnglishMemory.Core;

// FSRS-6 equations, default parameters from open-spaced-repetition/py-fsrs (MIT).
// Deterministic scheduling: no fuzz; learning and relearning steps are 1 / 10 minutes.
public static class Srs
{
    private static readonly double[] W = [.212, 1.2931, 2.3065, 8.2956, 6.4133, .8334, 3.0194, .001, 1.8722, .1666, .796, 1.4835, .0614, .2629, 1.6483, .6014, 1.8729, .5425, .0912, .0658, .1542];
    public static double Retrievability(double stability, double days) => stability <= 0 ? 0 : Math.Pow(1 + (Math.Pow(.9, -1 / W[20]) - 1) * Math.Max(0, days) / stability, -W[20]);
    private static double InitialDifficulty(int g) => W[4] - Math.Exp(W[5] * (g - 1)) + 1;
    public static (double Stability, double Difficulty, DateTimeOffset Due) Schedule(double s, double d, DateTimeOffset? last, Rating rating, DateTimeOffset now, double retention = .9)
    {
        if (!Enum.IsDefined(rating)) throw new ArgumentOutOfRangeException(nameof(rating));
        if (retention is < .7 or > .97) throw new ArgumentOutOfRangeException(nameof(retention));
        int g = (int)rating;
        double days = last.HasValue ? Math.Max(0, (now - last.Value).TotalDays) : 0;
        if (s <= 0 || last is null) { s = W[g - 1]; d = InitialDifficulty(g); }
        else
        {
            var r = Retrievability(s, days);
            if (days < 1) s *= Math.Max(g == 1 ? 0 : 1, Math.Exp(W[17] * (g - 3 + W[18])) * Math.Pow(s, -W[19]));
            else if (g == 1) s = Math.Min(W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp((1 - r) * W[14]), s / Math.Exp(W[17] * W[18]));
            else s *= 1 + Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp((1 - r) * W[10]) - 1) * (g == 2 ? W[15] : 1) * (g == 4 ? W[16] : 1);
            d = W[7] * InitialDifficulty(4) + (1 - W[7]) * (d - W[6] * (g - 3) * (10 - d) / 9);
        }
        s = Math.Max(.001, s); d = Math.Clamp(d, 1, 10);
        var interval = Math.Clamp(Math.Round(s / (Math.Pow(.9, -1 / W[20]) - 1) * (Math.Pow(retention, -1 / W[20]) - 1)), 1, 36500);
        var due = g == 1 ? now.AddMinutes(1) : g == 2 && days < 1 ? now.AddMinutes(10) : now.AddDays(interval);
        return (s, d, due);
    }
}
