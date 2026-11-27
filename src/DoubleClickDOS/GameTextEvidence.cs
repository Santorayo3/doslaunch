using System.Text.RegularExpressions;

namespace DoubleClickDOS;

/// <summary>Structured metadata of a Total DOS Collection style description file.</summary>
/// <param name="Title">From the "Name:" field, or the GoodTools file name line.</param>
/// <param name="FieldYear">From the "Year:" field.</param>
/// <param name="FileNameYear">From the year in the GoodTools file name line.</param>
/// <param name="Publisher">From the "Publisher:" field.</param>
internal sealed record DizInfo(string? Title, int? FieldYear, int? FileNameYear, string? Publisher)
{
    public int? Year => FieldYear ?? FileNameYear;

    public bool HasAuthoritativeIdentity => Title is not null && Year is not null;
}

/// <summary>
/// Reads dated and hardware related hints out of text files and printable strings. Filesystem dates are
/// deliberately not used: copying or unzipping a game rewrites them, while text written by the authors does not.
/// </summary>
internal static class GameTextEvidence
{
    private const int MaxInspectedChars = 2 * 1024 * 1024;
    private const int MaxHintsPerKind = 3;

    private const string YearPattern = @"(198[1-9]|199\d|2000)";

    private static readonly Regex Copyright = new(
        @"(?:©|\(c\)|copyright|copr\.)\s*:?\s*(?:©\s*|\(c\)\s*)*" + YearPattern +
        @"(?:\s*[-–—,/]\s*" + YearPattern + @")?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReleaseNote = new(
        @"\b(?:released|published|release date|first released|erschienen|veröffentlicht)\b[^\n.!?]{0,60}?" + YearPattern + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildStampMonthFirst = new(
        @"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*[\s_/,-]+\d{1,2}(?:st|nd|rd|th)?[\s_/,-]+" +
        YearPattern + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BuildStampDayFirst = new(
        @"\b\d{1,2}(?:st|nd|rd|th)?[\s_/,-]+(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*[\s_/,-]+" +
        YearPattern + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MemoryRequirement = new(
        @"(?:(?:requires?|needs?|minimum|at least)[^\n.!?]{0,40}?(\d{1,3})\s*(?:mb|megabytes?)\b" +
        @"|\b(\d{1,3})\s*(?:mb|megabytes?)\s+(?:of\s+)?(?:ram|memory)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProtectedModeRequirement = new(
        @"(?:requires?|needs?|minimum|at least)[^\n.!?]{0,40}?\b(?:386|486|pentium)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyYear = new(@"\b" + YearPattern + @"\b", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex DizFileName = new(
        @"^(?<title>.+?)\(\s*(?<year>198[1-9]|199\d|2000)\s*\)(?<rest>.*?)\.(?:zip|rar|7z|lha|arj|arc)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MetadataLine = new(
        @"^(?<key>[A-Za-z][A-Za-z0-9 /_\-\.]{0,24})\s*:\s*(?<value>\S.*)$",
        RegexOptions.Compiled);

    private static readonly Regex ApostropheYear = new(@"['’](?<two>[6-9]\d)\b", RegexOptions.Compiled);
    private static readonly Regex ShortDate = new(@"\b\d{1,2}[./]\d{1,2}[./](?<two>[6-9]\d)\b", RegexOptions.Compiled);

    private static readonly Regex SceneNoise = new(
        @"\b(?:this is|not shareware|pirated|illegal)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SceneTitleCut = new(
        @"\s+(?:from|by|shareware|version|multiplayer|update)\b.*$" +
        @"|\s*[-–=]{3,}\s*.*$" +
        @"|\s+'\d{2}\b.*$" +
        @"|\s*\(\s*c\s*\).*$" +
        @"|\s+\(?(?:198[1-9]|199\d|2000)\b.*$" +
        @"|\s+v?\d+(?:\.\d+)+\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lower bounds from the toolchain that built the program; they rule out earlier eras.</summary>
    private static readonly (string Fingerprint, int Year)[] Toolchains =
    [
        ("DOS/4GW", 1991), ("DOS/4G", 1991), ("DOS32A", 1996), ("CWSDPMI", 1994),
        ("WATCOM C/C++", 1993), ("WATCOM C++", 1993), ("DJGPP", 1994), ("SCI32", 1993),
        ("InstallShield", 1990), ("PKLITE", 1990), ("LZEXE", 1989),
    ];

    /// <summary>First plausible DOS era year in the text, used for short summaries and snippets.</summary>
    public static int? ExtractYear(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Length > 400 ? text[..400] : text;
        Match match = AnyYear.Match(trimmed);
        return match.Success && int.TryParse(match.Groups[1].Value, out int year) ? year : null;
    }

    public static List<YearEvidence> YearEvidenceFrom(string text, string origin, bool fromExecutable)
    {
        var evidence = new List<YearEvidence>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return evidence;
        }

        string excerpt = text.Length > MaxInspectedChars ? text[..MaxInspectedChars] : text;

        // Copyright notices only prove "not older than"; the newest year in a range is the useful one.
        var copyrightYears = new List<int>();
        foreach (Match match in Copyright.Matches(excerpt))
        {
            if (!int.TryParse(match.Groups[1].Value, out int first) || !DosYears.Contains(first))
            {
                continue;
            }

            int year = first;
            string detail = $"{origin}: copyright {match.Groups[1].Value}";
            if (match.Groups[2].Success &&
                int.TryParse(match.Groups[2].Value, out int last) &&
                DosYears.Contains(last))
            {
                year = Math.Max(first, last);
                detail = $"{origin}: copyright {match.Groups[1].Value}-{match.Groups[2].Value}";
            }

            if (copyrightYears.Count >= MaxHintsPerKind)
            {
                break;
            }

            copyrightYears.Add(year);
            evidence.Add(new YearEvidence(
                year,
                0.6,
                IsLowerBound: true,
                fromExecutable ? "copyright (executable)" : "copyright (text)",
                detail));
        }

        var releaseYears = new HashSet<int>();
        foreach (Match match in ReleaseNote.Matches(excerpt))
        {
            if (releaseYears.Count >= MaxHintsPerKind)
            {
                break;
            }

            if (int.TryParse(match.Groups[1].Value, out int year) && releaseYears.Add(year))
            {
                evidence.Add(new YearEvidence(
                    year,
                    0.5,
                    IsLowerBound: false,
                    "release note",
                    $"{origin}: mentions a release year of {year}"));
            }
        }

        var stampYears = new HashSet<int>();
        foreach (Regex pattern in new[] { BuildStampMonthFirst, BuildStampDayFirst })
        {
            foreach (Match match in pattern.Matches(excerpt))
            {
                if (stampYears.Count >= MaxHintsPerKind)
                {
                    break;
                }

                if (int.TryParse(match.Groups[1].Value, out int year) && stampYears.Add(year))
                {
                    evidence.Add(new YearEvidence(
                        year,
                        0.7,
                        IsLowerBound: false,
                        "build stamp",
                        $"{origin}: build stamp \"{SingleLine(match.Value)}\""));
                }
            }
        }

        if (fromExecutable)
        {
            var toolchainYears = new HashSet<int>();
            foreach ((string fingerprint, int year) in Toolchains)
            {
                if (text.Contains(fingerprint, StringComparison.OrdinalIgnoreCase) && toolchainYears.Add(year))
                {
                    evidence.Add(new YearEvidence(
                        year,
                        0.4,
                        IsLowerBound: true,
                        "toolchain",
                        $"built with {fingerprint}, which exists since {year}"));
                }
            }
        }

        return evidence;
    }

    public static HardwareHints HintsFrom(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return HardwareHints.Empty;
        }

        string excerpt = text.Length > MaxInspectedChars ? text[..MaxInspectedChars] : text;

        int? memory = null;
        foreach (Match match in MemoryRequirement.Matches(excerpt))
        {
            string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!int.TryParse(value, out int megabytes) || megabytes is < 1 or > 64)
            {
                continue;
            }

            // "requires 20 MB free disk space" is not a memory requirement.
            int tail = Math.Min(match.Index + match.Length + 24, excerpt.Length);
            string after = excerpt[(match.Index + match.Length)..tail];
            if (after.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
                after.Contains("hard drive", StringComparison.OrdinalIgnoreCase) ||
                after.Contains("space", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            memory = Math.Max(memory ?? 0, megabytes);
        }

        bool protectedMode = ProtectedModeRequirement.IsMatch(excerpt) ||
                             text.Contains("DOS/4GW", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("DOS32A", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("CWSDPMI", StringComparison.OrdinalIgnoreCase);

        return new HardwareHints(memory, protectedMode);
    }

    /// <summary>
    /// Parses Total DOS Collection style descriptions ("Name:…", "Year:…", "Publisher:…") and the
    /// GoodTools file name line above them ("Alpine Tram Ride v1.3 (1989)(Merit Software) […].zip").
    /// </summary>
    public static DizInfo ReadDiz(string text)
    {
        string? name = null;
        int? fieldYear = null;
        string? publisher = null;
        string? fileNameTitle = null;
        int? fileNameYear = null;

        int inspected = 0;
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().Trim('\uFEFF', '\0', '\u001A', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            if (++inspected > 40)
            {
                break;
            }

            if (fileNameTitle is null)
            {
                Match fileName = DizFileName.Match(line);
                if (fileName.Success)
                {
                    fileNameTitle = fileName.Groups["title"].Value.Trim();
                    if (int.TryParse(fileName.Groups["year"].Value, out int nameYear))
                    {
                        fileNameYear = nameYear;
                    }

                    continue;
                }
            }

            Match metadata = MetadataLine.Match(line);
            if (!metadata.Success)
            {
                continue;
            }

            string key = metadata.Groups["key"].Value.Trim().ToLowerInvariant();
            string value = metadata.Groups["value"].Value.Trim();
            switch (key)
            {
                case "name" or "title" or "game" or "game name":
                    name ??= value;
                    break;
                case "year" or "date" or "release year" or "released":
                    if (fieldYear is null && ExtractYear(value) is int year)
                    {
                        fieldYear = year;
                    }

                    break;
                case "publisher" or "published by" or "company":
                    publisher ??= value;
                    break;
            }
        }

        return new DizInfo(name ?? fileNameTitle, fieldYear, fileNameYear, publisher);
    }

    /// <summary>
    /// Release year from the description's first line, which is the only place a scene DIZ mentions it.
    /// Handles a plain year, an apostrophe year ("BAD BLOOD '94") and a short date ("11.08.96").
    /// </summary>
    public static List<YearEvidence> SceneYears(string text, string origin)
    {
        var evidence = new List<YearEvidence>();
        string? firstLine = text.Split('\n')
            .Select(line => line.Trim().Trim('\uFEFF', '\0', '\u001A', ' ', '\t'))
            .FirstOrDefault(line => line.Length >= 3);
        if (firstLine is null)
        {
            return evidence;
        }

        Match fourDigit = AnyYear.Match(firstLine);
        if (fourDigit.Success && int.TryParse(fourDigit.Groups[1].Value, out int year))
        {
            // A release year stated in the description outranks a compiler build stamp from the same era.
            evidence.Add(new YearEvidence(year, 0.75, false, "scene description", $"{origin}: mentions {year}"));
        }

        foreach (Regex pattern in new[] { ApostropheYear, ShortDate })
        {
            Match shortYear = pattern.Match(firstLine);
            if (shortYear.Success &&
                int.TryParse(shortYear.Groups["two"].Value, out int two) &&
                1900 + two is int fullYear &&
                DosYears.Contains(fullYear))
            {
                evidence.Add(new YearEvidence(fullYear, 0.55, false, "scene description", $"{origin}: mentions '{two}"));
            }
        }

        return evidence;
    }

    /// <summary>Strips banner art and marketing suffixes from a scene description's title line.</summary>
    public static string? SceneTitle(string text)
    {
        string? firstLine = text.Split('\n')
            .Select(line => line.Trim().Trim('\uFEFF', '\0', '\u001A'))
            .FirstOrDefault(line => line.Length >= 3);
        if (firstLine is null || firstLine.Length > 100 || SceneNoise.IsMatch(firstLine))
        {
            return null;
        }

        string title = TrimArt(firstLine);
        title = SceneTitleCut.Replace(title, " ").Trim();
        title = TrimArt(title);
        return title.Length >= 3 ? title : null;
    }

    private static string TrimArt(string value) =>
        value.Trim(' ', '-', '=', '_', '|', '/', '\\', '.', '*', '#', '~', '^', '+', '>', '<', ':', ',', ';', '\'', '"', '!');

    /// <summary>First headline of a README, which usually is the game's name.</summary>
    public static string? ReadmeHeading(string text)
    {
        foreach (string rawLine in text.Split('\n').Take(25))
        {
            string line = rawLine.Trim().Trim('-', '=', '*', '#', ' ', '\t').Trim();
            if (line.Length is < 3 or > 60 || line.Count(char.IsLetter) < 2)
            {
                continue;
            }

            if (IsNoiseLine(line))
            {
                continue;
            }

            return line;
        }

        return null;
    }

    private static bool IsNoiseLine(string line) =>
        line.Contains("readme", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("install", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("note", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("copy", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("table of contents", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("http", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
        line.Contains('@');

    private static string SingleLine(string value) =>
        Whitespace.Replace(value.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
}
