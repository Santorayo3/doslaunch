namespace DoubleClickDOS;

/// <summary>
/// Works out which game an executable belongs to and roughly when it was released. Local evidence from the
/// game folder is always collected; online sources only run when enabled and no usable cached result exists.
/// </summary>
internal static class GameInfoLookup
{
    private const int ArchiveCandidateLimit = 3;
    private const int WikidataCandidateLimit = 2;

    public static GameIdentity TryResolve(string executablePath, bool allowOnline)
    {
        GameIdentityCache cache = GameIdentityCache.Load();
        string key = GameIdentityCache.KeyFor(executablePath);
        if (cache.TryGet(key, allowOnline, out GameIdentity? cached) && cached is not null)
        {
            Log.Write($"Game identity for {Path.GetFileName(executablePath)} reused from cache: {Describe(cached)}.");
            return cached;
        }

        LocalEvidence local = LocalGameEvidence.Collect(executablePath);
        var evidence = new List<YearEvidence>(local.Years);
        string? title = local.Titles.Count > 0 ? local.Titles[0].Title : null;

        bool usedOnline = false;
        if (local.HasAuthoritativeIdentity)
        {
            // A Total DOS Collection style file_id.diz already names the game and its year.
            Log.Write("The game description contains its name and year, so no online lookup is needed.");
        }
        else if (allowOnline && local.Titles.Count > 0)
        {
            OnlineLookup online = LookUpOnline(local.Titles);
            // Only remember the online attempt when it actually produced something; throttled or offline
            // runs must be retried the next time a config is generated.
            usedOnline = online.Evidence.Count > 0;
            evidence.AddRange(online.Evidence);
            if (online.Title is not null)
            {
                title = title is null ? online.Title : GameTitles.PreferDisplayTitle(title, online.Title);
            }
        }

        GameIdentity identity = GameEraEstimator.Estimate(title, evidence, local.Hints);
        Log.Write($"Game identity for {Path.GetFileName(executablePath)}: {Describe(identity)}.");
        foreach (YearEvidence hint in identity.Evidence)
        {
            Log.Write($"  evidence: {hint.Detail}");
        }

        cache.Store(key, identity, usedOnline);
        cache.Save();
        return identity;
    }

    private static string Describe(GameIdentity identity) =>
        $"\"{identity.Title ?? "unknown game"}\", {identity.YearText}, {identity.Era.Describe()} era, " +
        $"{identity.Confidence.ToString().ToLowerInvariant()} confidence";

    private static OnlineLookup LookUpOnline(IReadOnlyList<TitleCandidate> titles)
    {
        var evidence = new List<YearEvidence>();

        try
        {
            // A few seconds on the first launch are acceptable in exchange for a second opinion.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken token = timeout.Token;

            GameMatch? archive = FindInArchive(titles, token);
            GameMatch? wikidata = FindInWikidata(titles, token);

            if (archive is not null && wikidata is null)
            {
                // The archive title may carry edition suffixes ("DOOM Alpha"), so ask Wikidata for it too.
                wikidata = WikidataSource.Find(archive.Title, token);
            }

            if (archive is null && wikidata is { PlatformConfirmed: true })
            {
                archive = ArchiveOrgSource.Find(wikidata.Title, token);
            }

            string? title = AddEvidence(archive, wikidata, evidence);

            if (evidence.Count == 0)
            {
                title = AddWikipediaFallback(titles, evidence, token) ?? title;
            }

            return new OnlineLookup(evidence, title);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Write($"Online game info lookup failed: {ex.Message}");
            return new OnlineLookup(evidence, null);
        }
    }

    private static GameMatch? FindInArchive(IReadOnlyList<TitleCandidate> titles, CancellationToken token)
    {
        foreach (TitleCandidate candidate in titles.Take(ArchiveCandidateLimit))
        {
            GameMatch? match = ArchiveOrgSource.Find(candidate.Title, token);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static GameMatch? FindInWikidata(IReadOnlyList<TitleCandidate> titles, CancellationToken token)
    {
        foreach (TitleCandidate candidate in titles.Take(WikidataCandidateLimit))
        {
            GameMatch? match = WikidataSource.Find(candidate.Title, token);
            if (match is { PlatformConfirmed: true })
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Wikidata only counts when it confirms the DOS platform or when archive.org already identified the
    /// same game, so the year can never come from a console or Amiga release alone.
    /// </summary>
    private static string? AddEvidence(GameMatch? archive, GameMatch? wikidata, List<YearEvidence> evidence)
    {
        string? title = null;
        if (archive is not null)
        {
            evidence.Add(ToEvidence(archive));
            title = archive.Title;
        }

        if (wikidata is not null &&
            (wikidata.PlatformConfirmed || archive is not null) &&
            (title is null || GameTitles.Similarity(title, wikidata.Title) >= 0.8))
        {
            evidence.Add(ToEvidence(wikidata));

            // The Wikidata label is the canonical name; archive.org titles often carry edition suffixes.
            title = wikidata.PlatformConfirmed ? wikidata.Title : (title ?? wikidata.Title);
        }

        return title;
    }

    private static string? AddWikipediaFallback(
        IReadOnlyList<TitleCandidate> titles,
        List<YearEvidence> evidence,
        CancellationToken token)
    {
        foreach (TitleCandidate candidate in titles.Take(WikidataCandidateLimit))
        {
            GameMatch? match = WikipediaSource.Find(candidate.Title, token);
            if (match is null)
            {
                continue;
            }

            evidence.Add(ToEvidence(match));
            return match.Title;
        }

        return null;
    }

    private static YearEvidence ToEvidence(GameMatch match) =>
        new(match.Year, match.Weight, IsLowerBound: false, match.Source, match.Detail);

    private sealed record OnlineLookup(IReadOnlyList<YearEvidence> Evidence, string? Title);
}
