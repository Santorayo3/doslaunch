using System.Net.Http;
using System.Text.Json;

namespace DosLaunch;

/// <summary>A release year found online, together with how trustworthy the finding is.</summary>
internal sealed record GameMatch(string Title, int Year, double Weight, string Source, string Detail)
{
    /// <summary>True when the source itself confirms the game exists on DOS, not just that it is a game.</summary>
    public bool PlatformConfirmed { get; init; }
}

internal static class GameInfoHttp
{
    private static readonly HttpClient Client = CreateClient();

    public static JsonDocument GetJson(string url, CancellationToken token)
    {
        JsonDocument? document = TryGetJson(url, token);
        return document ?? throw new HttpRequestException($"The server returned 404 for {url}.");
    }

    public static JsonDocument? TryGetJson(string url, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage response = Client.GetAsync(url, token).GetAwaiter().GetResult();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            // Wikimedia throttles bursts; one short retry keeps a first launch usable.
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt == 0)
            {
                TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                if (delay > TimeSpan.FromSeconds(2))
                {
                    delay = TimeSpan.FromSeconds(2);
                }

                Task.Delay(delay, token).GetAwaiter().GetResult();
                continue;
            }

            response.EnsureSuccessStatusCode();
            string body = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
            return JsonDocument.Parse(body);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DosLaunch/1.0 (https://github.com/Santorayo3/doslaunch)");
        return client;
    }
}

/// <summary>
/// archive.org's MS-DOS software library. The collection is DOS only, so a hit both identifies the game
/// and proves the platform. This is the primary source because its metadata carries a curated year.
/// </summary>
internal static class ArchiveOrgSource
{
    public static GameMatch? Find(string title, CancellationToken token)
    {
        try
        {
            string query = $"title:({EscapeLucene(title)}) AND collection:softwarelibrary_msdos";
            string url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(query) +
                         "&fl%5B%5D=title&fl%5B%5D=year&fl%5B%5D=date&rows=25&page=1&output=json";

            using JsonDocument document = GameInfoHttp.GetJson(url, token);
            if (!document.RootElement.TryGetProperty("response", out JsonElement response) ||
                !response.TryGetProperty("docs", out JsonElement docs) ||
                docs.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            GameMatch? best = null;
            foreach (JsonElement documentEntry in docs.EnumerateArray())
            {
                string? rawTitle = documentEntry.TryGetProperty("title", out JsonElement titleElement)
                    ? titleElement.GetString()
                    : null;
                int? year = ReadYear(documentEntry);
                if (rawTitle is null || year is null)
                {
                    continue;
                }

                double similarity = GameTitles.Similarity(title, rawTitle);
                if (similarity < 0.8)
                {
                    continue;
                }

                var match = new GameMatch(
                    DisplayTitle(rawTitle),
                    year.Value,
                    0.85 * similarity,
                    "archive.org",
                    $"archive.org MS-DOS collection lists \"{rawTitle}\" ({year.Value})")
                {
                    PlatformConfirmed = true,
                };

                if (best is null || match.Weight > best.Weight)
                {
                    best = match;
                }
            }

            return best;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or JsonException or InvalidOperationException)
        {
            Log.Write($"archive.org lookup for \"{title}\" failed: {ex.Message}");
            return null;
        }
    }

    private static string DisplayTitle(string rawTitle)
    {
        string cleaned = GameTitles.Clean(rawTitle);
        return cleaned.Length > 0 ? cleaned : rawTitle;
    }

    private static int? ReadYear(JsonElement documentEntry)    {
        if (documentEntry.TryGetProperty("year", out JsonElement yearElement))
        {
            if (yearElement.ValueKind == JsonValueKind.Number && yearElement.TryGetInt32(out int numeric) &&
                DosYears.Contains(numeric))
            {
                return numeric;
            }

            if (yearElement.ValueKind == JsonValueKind.String &&
                int.TryParse(yearElement.GetString(), out int parsed) &&
                DosYears.Contains(parsed))
            {
                return parsed;
            }
        }

        if (documentEntry.TryGetProperty("date", out JsonElement dateElement) &&
            dateElement.ValueKind == JsonValueKind.String &&
            dateElement.GetString() is { Length: >= 4 } date &&
            int.TryParse(date[..4], out int fromDate) &&
            DosYears.Contains(fromDate))
        {
            return fromDate;
        }

        return null;
    }

    /// <summary>Solr treats punctuation as query syntax, so the title is reduced to plain search terms.</summary>
    private static string EscapeLucene(string title)
    {
        var builder = new System.Text.StringBuilder(title.Length);
        foreach (char character in title)
        {
            builder.Append(character is '+' or '-' or '&' or '|' or '!' or '(' or ')' or '{' or '}' or '[' or ']'
                or '^' or '"' or '~' or '*' or '?' or ':' or '\\' or '/'
                ? ' '
                : character);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Wikidata: structured publication dates (P577) and platforms (P400). Only items that list a DOS platform
/// count as confirmation, otherwise the year could belong to the Amiga or console release of the same game.
/// </summary>
internal static class WikidataSource
{
    private const string MsDosItem = "Q47604";
    private const string DosItem = "Q170434";
    private const string VideoGameItem = "Q7889";

    public static GameMatch? Find(string title, CancellationToken token)
    {
        try
        {
            List<SearchHit> hits = Search(title, token);
            List<SearchHit> candidates = hits
                .Where(hit => hit.Description.Contains("game", StringComparison.OrdinalIgnoreCase) &&
                              GameTitles.Similarity(title, hit.Label) >= 0.8)
                .OrderByDescending(hit => MentionsDosEraYear(hit.Description))
                .ThenByDescending(hit => GameTitles.Similarity(title, hit.Label))
                .ThenBy(hit => hit.Label.Length)
                .Take(4)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            string ids = Uri.EscapeDataString(string.Join('|', candidates.Select(candidate => candidate.Id)));
            string url = "https://www.wikidata.org/w/api.php?action=wbgetentities&props=claims|descriptions&format=json&ids=" + ids;
            using JsonDocument document = GameInfoHttp.GetJson(url, token);
            if (!document.RootElement.TryGetProperty("entities", out JsonElement entities))
            {
                return null;
            }

            foreach (SearchHit candidate in candidates)
            {
                if (!entities.TryGetProperty(candidate.Id, out JsonElement entity))
                {
                    continue;
                }

                int? year = ClaimYears(entity, "P577")
                    .Where(DosYears.Contains)
                    .OrderBy(value => value)
                    .Select(value => (int?)value)
                    .FirstOrDefault();
                if (year is null)
                {
                    continue;
                }

                bool isVideoGame = ClaimItemIds(entity, "P31").Contains(VideoGameItem) ||
                                   candidate.Description.Contains("video game", StringComparison.OrdinalIgnoreCase);
                if (!isVideoGame)
                {
                    continue;
                }

                bool dosPlatform = ClaimItemIds(entity, "P400").Any(id => id is MsDosItem or DosItem);
                return new GameMatch(
                    candidate.Label,
                    year.Value,
                    dosPlatform ? 0.9 : 0.6,
                    "wikidata",
                    dosPlatform
                        ? $"Wikidata lists \"{candidate.Label}\" for DOS ({year.Value})"
                        : $"Wikidata lists \"{candidate.Label}\" ({year.Value}), DOS platform not confirmed")
                {
                    PlatformConfirmed = dosPlatform,
                };
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or JsonException or InvalidOperationException)
        {
            Log.Write($"Wikidata lookup for \"{title}\" failed: {ex.Message}");
            return null;
        }
    }

    private static List<SearchHit> Search(string title, CancellationToken token)
    {
        string url = "https://www.wikidata.org/w/api.php?action=wbsearchentities&type=item&language=en&uselang=en&limit=8&format=json&search=" +
                     Uri.EscapeDataString(title);
        using JsonDocument document = GameInfoHttp.GetJson(url, token);
        var hits = new List<SearchHit>();
        if (!document.RootElement.TryGetProperty("search", out JsonElement search) || search.ValueKind != JsonValueKind.Array)
        {
            return hits;
        }

        foreach (JsonElement hit in search.EnumerateArray())
        {
            string? id = GetString(hit, "id");
            string? label = GetString(hit, "label");
            if (id is null || label is null)
            {
                continue;
            }

            hits.Add(new SearchHit(id, label, GetString(hit, "description") ?? string.Empty));
        }

        return hits;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Wikidata descriptions look like "1993 video game", which is the best hint for ranking hits.</summary>
    private static bool MentionsDosEraYear(string description)
    {
        foreach (string token in description.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string digits = new(token.Where(char.IsDigit).ToArray());
            if (digits.Length == 4 && int.TryParse(digits, out int year) && DosYears.Contains(year))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ClaimItemIds(JsonElement entity, string property)
    {
        if (!entity.TryGetProperty("claims", out JsonElement claims) ||
            !claims.TryGetProperty(property, out JsonElement statements) ||
            statements.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement statement in statements.EnumerateArray())
        {
            if (statement.TryGetProperty("mainsnak", out JsonElement snak) &&
                snak.TryGetProperty("datavalue", out JsonElement dataValue) &&
                dataValue.TryGetProperty("value", out JsonElement value) &&
                value.TryGetProperty("id", out JsonElement id) &&
                id.GetString() is { Length: > 0 } itemId)
            {
                yield return itemId;
            }
        }
    }

    private static IEnumerable<int> ClaimYears(JsonElement entity, string property)
    {
        if (!entity.TryGetProperty("claims", out JsonElement claims) ||
            !claims.TryGetProperty(property, out JsonElement statements) ||
            statements.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement statement in statements.EnumerateArray())
        {
            if (statement.TryGetProperty("mainsnak", out JsonElement snak) &&
                snak.TryGetProperty("datavalue", out JsonElement dataValue) &&
                dataValue.TryGetProperty("value", out JsonElement value) &&
                value.TryGetProperty("time", out JsonElement time) &&
                time.GetString() is { Length: >= 5 } timestamp &&
                timestamp[0] is '+' or '-' &&
                int.TryParse(timestamp[1..5], out int year))
            {
                yield return year;
            }
        }
    }

    private sealed record SearchHit(string Id, string Label, string Description);
}

/// <summary>
/// Last resort when neither structured source knows the game. A Wikipedia hit is not platform verified,
/// so it only contributes weak evidence and never confirms the title by itself.
/// </summary>
internal static class WikipediaSource
{
    private const string SummaryUrl = "https://en.wikipedia.org/api/rest_v1/page/summary/";
    private const string SearchUrl =
        "https://en.wikipedia.org/w/api.php?action=query&list=search&srlimit=4&format=json&formatversion=2&srsearch=";

    public static GameMatch? Find(string title, CancellationToken token)
    {
        try
        {
            foreach (string candidate in SearchTitles(title, token))
            {
                if (!GameTitles.TitlesMatch(title, candidate))
                {
                    continue;
                }

                PageSummary? page = FetchSummary(candidate, token);
                if (page is null || !LooksLikeGame(page))
                {
                    continue;
                }

                int? year = GameTextEvidence.ExtractYear(page.Description) ?? GameTextEvidence.ExtractYear(page.Extract);
                if (year is null)
                {
                    continue;
                }

                return new GameMatch(
                    page.Title,
                    year.Value,
                    0.5,
                    "wikipedia",
                    $"Wikipedia summary \"{page.Title}\" ({year.Value})")
                {
                    PlatformConfirmed = false,
                };
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                                       or JsonException or InvalidOperationException)
        {
            Log.Write($"Wikipedia lookup for \"{title}\" failed: {ex.Message}");
            return null;
        }
    }

    private static List<string> SearchTitles(string title, CancellationToken token)
    {
        var titles = new List<string>();
        string url = SearchUrl + Uri.EscapeDataString(title + " video game");
        using JsonDocument document = GameInfoHttp.GetJson(url, token);

        if (document.RootElement.TryGetProperty("query", out JsonElement query) &&
            query.TryGetProperty("search", out JsonElement search) &&
            search.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement hit in search.EnumerateArray())
            {
                if (hit.TryGetProperty("title", out JsonElement hitTitle) &&
                    hitTitle.GetString() is { Length: > 0 } candidateTitle)
                {
                    titles.Add(candidateTitle);
                }
            }
        }

        return titles;
    }

    private static PageSummary? FetchSummary(string pageTitle, CancellationToken token)
    {
        string url = SummaryUrl + Uri.EscapeDataString(pageTitle.Replace(' ', '_')) + "?redirect=true";
        using JsonDocument? document = GameInfoHttp.TryGetJson(url, token);
        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;

        return new PageSummary(
            GetString(root, "title") ?? pageTitle,
            GetString(root, "description"),
            GetString(root, "extract"));
    }

    /// <summary>Filters out articles about something else, e.g. the plain "Worms" animal article.</summary>
    private static bool LooksLikeGame(PageSummary page)
    {
        string text = $"{page.Description} {page.Extract}";
        string head = text.Length > 400 ? text[..400] : text;
        return head.Contains("game", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record PageSummary(string Title, string? Description, string? Extract);
}
