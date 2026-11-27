namespace DoubleClickDOS;

/// <summary>
/// Combines independent year hints into an era bucket and an approximate year. Votes are weighted, so
/// an explicit database year beats a copyright notice, and one outlier cannot overrule two agreeing sources.
/// </summary>
internal static class GameEraEstimator
{
    private const double MediumWeight = 0.55;
    private const double HighWeight = 1.4;
    private const double ConflictRatio = 0.8;

    /// <summary>
    /// A copyright notice can be newer than the release (engine, installer or re-release), so it only rules
    /// out eras that are more than this many years older.
    /// </summary>
    private const int LowerBoundToleranceYears = 2;

    public static GameIdentity Estimate(string? title, IEnumerable<YearEvidence> evidence, HardwareHints hints)
    {
        List<YearEvidence> items = Deduplicate(evidence);
        if (items.Count == 0)
        {
            return new GameIdentity(title, null, false, GameEra.Unknown, GameConfidence.None, [], hints);
        }

        // A copyright notice only proves "not older than", so every bucket stays possible; the notice votes
        // for its own era and the answer is reported as "estimate or later".
        GameEra EraOf(YearEvidence item) => GameEraExtensions.FromYear(item.Year);

        var weights = new Dictionary<GameEra, double>();
        foreach (YearEvidence item in items)
        {
            GameEra era = EraOf(item);
            weights[era] = weights.GetValueOrDefault(era) + item.Weight;
        }

        // "Not before 1993" rules out clearly older buckets, but stays tolerant: a 1997 installer copyright
        // must not override an explicit 1995 release year.
        int minimumYear = items.Where(item => item.IsLowerBound).Select(item => item.Year).DefaultIfEmpty(0).Max();
        if (minimumYear > 0)
        {
            int oldestAllowed = Math.Max(DosYears.Min, minimumYear - LowerBoundToleranceYears);
            GameEra minimumEra = GameEraExtensions.FromYear(oldestAllowed);
            foreach (GameEra older in weights.Keys.Where(era => era < minimumEra && era != GameEra.Unknown).ToList())
            {
                weights.Remove(older);
            }
        }

        if (weights.Count == 0)
        {
            return new GameIdentity(title, null, false, GameEra.Unknown, GameConfidence.None, [], hints);
        }

        // Ties go to the newer era: too slow settings still run, too small ones crash.
        KeyValuePair<GameEra, double> best = weights
            .OrderByDescending(pair => pair.Value)
            .ThenByDescending(pair => pair.Key)
            .First();

        double total = weights.Values.Sum();
        double runnerUp = weights.Where(pair => pair.Key != best.Key).Select(pair => pair.Value).DefaultIfEmpty(0).Max();
        int sources = items.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        GameConfidence confidence =
            total >= HighWeight && sources >= 2 ? GameConfidence.High
            : total >= MediumWeight ? GameConfidence.Medium
            : GameConfidence.Low;

        if (runnerUp >= best.Value * ConflictRatio)
        {
            confidence = Lower(confidence);
        }

        (int year, bool isLowerBound) = PickYear(items, best.Key, EraOf);
        return new GameIdentity(title, year, isLowerBound, best.Key, confidence, items, hints);
    }

    private static (int Year, bool IsLowerBound) PickYear(
        List<YearEvidence> items,
        GameEra era,
        Func<YearEvidence, GameEra> eraOf)
    {
        List<YearEvidence> inEra = items.Where(item => eraOf(item) == era).ToList();

        YearEvidence? exact = inEra
            .Where(item => !item.IsLowerBound)
            .OrderByDescending(item => item.Weight)
            .ThenBy(item => item.Year)
            .FirstOrDefault();
        if (exact is not null)
        {
            return (exact.Year, false);
        }

        YearEvidence bound = inEra
            .OrderByDescending(item => item.Weight)
            .ThenByDescending(item => item.Year)
            .First();
        return (bound.Year, true);
    }

    /// <summary>
    /// Keeps the most useful hint per source: the latest copyright year is the closest to the release,
    /// while the earliest explicit release date wins for exact evidence.
    /// </summary>
    private static List<YearEvidence> Deduplicate(IEnumerable<YearEvidence> evidence) =>
        evidence
            .Where(item => DosYears.Contains(item.Year) && item.Weight > 0)
            .GroupBy(item => (item.Source, item.IsLowerBound))
            .Select(group => group.Key.IsLowerBound
                ? group.OrderByDescending(item => item.Year).ThenByDescending(item => item.Weight).First()
                : group.OrderByDescending(item => item.Weight).ThenBy(item => item.Year).First())
            .ToList();

    private static GameConfidence Lower(GameConfidence confidence) => confidence switch
    {
        GameConfidence.High => GameConfidence.Medium,
        GameConfidence.Medium => GameConfidence.Low,
        _ => GameConfidence.None,
    };
}
