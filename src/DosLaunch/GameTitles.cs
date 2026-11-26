using System.Text;
using System.Text.RegularExpressions;

namespace DosLaunch;

internal sealed record TitleCandidate(string Title, double Score, string Source);

/// <summary>
/// Derives searchable game names from folder and file names and decides whether two names mean the same game.
/// DOS file names are 8.3, so a candidate is also checked against its truncated form.
/// </summary>
internal static class GameTitles
{
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "start", "run", "play", "setup", "install", "installer", "main", "dos", "dosgames",
        "msdos", "ms-dos", "spiele", "programs", "programme", "bin", "oldgames", "retro", "launch", "new folder",
        "data", "files", "root", "temp", "downloads", "desktop", "documents", "dosbox", "pc", "windows",
        "unknown", "untitled", "no name", "disk1", "disk 1", "cd", "cdrom",
    };

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".txt", ".doc", ".diz", ".nfo", ".1st", ".me", ".lst", ".cfg", ".ini",
        ".zip", ".rar", ".7z", ".lha", ".arj", ".arc",
    };

    private static readonly Dictionary<string, string> RomanToDigit = new(StringComparer.Ordinal)
    {
        ["ii"] = "2",
        ["iii"] = "3",
        ["iv"] = "4",
        ["vi"] = "6",
        ["vii"] = "7",
        ["viii"] = "8",
        ["ix"] = "9",
        ["x"] = "10",
    };

    /// <summary>Words that qualify a release without turning it into another game.</summary>
    private static readonly HashSet<string> NeutralTokens = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "of", "and", "or", "in", "to", "for",
        "dos", "msdos", "pc", "ibm", "game", "games",
        "shareware", "demo", "full", "retail", "version", "v", "patch", "cracked", "crack",
        "alpha", "beta", "registered", "preview", "prototype", "press", "release",
        "disk", "disc", "cd", "rom", "iso", "install", "setup",
        "part", "episode", "chapter", "mission", "expansion", "trilogy", "series", "collection",
        "edition", "special", "deluxe", "complete", "alt", "controls", "wasd", "keyboard", "mouse", "sound",
    };

    private static readonly Regex Bracketed = new(@"[\(\[\{][^\)\]\}]*[\)\]\}]", RegexOptions.Compiled);
    private static readonly Regex PlatformPrefix = new(
        @"^\s*(?:ms[-\s]?dos|dos|ibm[-\s]?pc|pc)\s*[:\-–]\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingTags = new(
        @"\s*[-–]\s*(?:crack(?:ed)?|full|retail|shareware|demo|rip|iso|trainer|german|deutsch|english|usa|europe|eur|beta|preview)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VersionNumber = new(
        @"\bv?\d+(?:\.\d+)+[a-z]?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Separators = new(@"[_\-]+", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex TrailingArticle = new(
        @"^(?<body>.+),\s*(?<article>the|a|an)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Strips packaging noise such as "(1995)", "[!]", "v1.2" or a trailing "- CRACKED".</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string text = name.Trim();
        string extension = Path.GetExtension(text);
        if (KnownExtensions.Contains(extension) && text.Length > extension.Length)
        {
            text = text[..^extension.Length];
        }

        text = PlatformPrefix.Replace(text, " ");
        text = Bracketed.Replace(text, " ");
        text = TrailingTags.Replace(text, " ");
        text = Separators.Replace(text, " ");
        text = VersionNumber.Replace(text, " ");
        text = Whitespace.Replace(text, " ").Trim();
        text = text.Trim('-', '–', ':', ',', '.', '_').Trim();

        // GoodTools and scene files write "Great Fighter, The"; the catalogues use "The Great Fighter".
        Match article = TrailingArticle.Match(text);
        return article.Success ? $"{article.Groups["article"].Value} {article.Groups["body"].Value}" : text;
    }

    public static bool IsUsableTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) &&
        !IsGeneric(title) &&
        title.Length is >= 3 and <= 60 &&
        title.Count(char.IsLetter) >= 2 &&
        !title.All(character => char.IsDigit(character) || char.IsWhiteSpace(character));

    public static bool IsGeneric(string name)
    {
        string normalized = Whitespace.Replace(name.Trim(), " ");
        return GenericNames.Contains(normalized) ||
               normalized.All(character => char.IsDigit(character) || char.IsWhiteSpace(character));
    }

    public static List<TitleCandidate> Build(
        string executablePath,
        IEnumerable<string> siblingFileNames,
        string? fileIdDizTitle,
        string? readmeHeading,
        double fileIdDizScore = 0.8)
    {
        var candidates = new List<TitleCandidate>();
        var executableTruncations = new HashSet<string>(StringComparer.Ordinal);
        var otherExecutables = new List<string>();

        void RememberExecutable(string fileName)
        {
            string truncation = Truncate(Path.GetFileNameWithoutExtension(fileName));
            if (truncation.Length >= 3)
            {
                executableTruncations.Add(truncation);
            }
        }

        RememberExecutable(executablePath);
        foreach (string fileName in siblingFileNames)
        {
            string extension = Path.GetExtension(fileName);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".com", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            RememberExecutable(fileName);
            if (!Path.GetFileName(executablePath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                otherExecutables.Add(fileName);
            }
        }

        void Add(string? rawTitle, double score, string source)
        {
            string title = Clean(rawTitle);
            if (!IsUsableTitle(title))
            {
                return;
            }

            double boost = !source.Equals("executable name", StringComparison.Ordinal) &&
                           MatchesExecutableName(title, executableTruncations)
                ? 0.2
                : 0;
            var candidate = new TitleCandidate(title, Math.Min(score + boost, 1.0), source);
            int existing = candidates.FindIndex(item => item.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (existing < 0)
            {
                candidates.Add(candidate);
            }
            else if (candidates[existing].Score < candidate.Score)
            {
                candidates[existing] = candidate;
            }
        }

        string folder = Path.GetDirectoryName(executablePath) ?? string.Empty;
        Add(fileIdDizTitle, fileIdDizScore, "FILE_ID.DIZ");
        Add(Path.GetFileName(folder), 0.65, "game folder name");
        Add(readmeHeading, 0.5, "readme heading");
        Add(Path.GetFileNameWithoutExtension(executablePath), 0.55, "executable name");
        foreach (string other in otherExecutables.Take(6))
        {
            Add(Path.GetFileNameWithoutExtension(other), 0.3, "executable name");
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// True when an online hit really names the same game. Catalogue titles add qualifiers, so a hit that
    /// starts with the searched title counts. A hit that merely contains it ("Lords of Doom" for "Doom")
    /// only counts when the shared part is substantial.
    /// </summary>
    public static bool TitlesMatch(string expected, string candidate) => Similarity(expected, candidate) >= 0.8;

    internal static double Similarity(string left, string right)
    {
        string a = Normalize(left);
        string b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        double score = Math.Max(CoreScore(a, b), EditSimilarity(a, b) * 0.95);
        score = Math.Max(score, SubtitleScore(left, right, a, b));

        var setA = new HashSet<string>(SplitTokens(a), StringComparer.Ordinal);
        var setB = new HashSet<string>(SplitTokens(b), StringComparer.Ordinal);
        return HasNumberMismatch(setA, setB) ? score * 0.5 : score;
    }

    private static double CoreScore(string a, string b)
    {
        string[] tokensA = SplitTokens(a);
        string[] tokensB = SplitTokens(b);

        if (IsPrefix(tokensA, tokensB))
        {
            return PrefixScore(tokensB, tokensA);
        }

        if (IsPrefix(tokensB, tokensA))
        {
            return PrefixScore(tokensA, tokensB) - 0.05;
        }

        if (IsSuffix(tokensA, tokensB) || IsSuffix(tokensB, tokensA))
        {
            // "Keen 4" belongs to "Commander Keen 4", but "Doom" does not belong to "Lords of Doom".
            string[] shorter = tokensA.Length <= tokensB.Length ? tokensA : tokensB;
            string[] longer = tokensA.Length <= tokensB.Length ? tokensB : tokensA;
            return shorter.Length >= 2 && shorter.Length * 2 >= longer.Length ? 0.85 : 0.45;
        }

        var setA = new HashSet<string>(tokensA, StringComparer.Ordinal);
        var setB = new HashSet<string>(tokensB, StringComparer.Ordinal);
        return (double)setA.Intersect(setB).Count() / setA.Union(setB).Count() * 0.9;
    }

    /// <summary>
    /// A qualifier like "(Shareware)" or a subtitle after a colon is fine. A different name is not:
    /// "Doom 3D" and "Worms Armageddon" are other games, not editions of "Doom" and "Worms".
    /// </summary>
    private static double PrefixScore(string[] longer, string[] shorter)
    {
        if (!HasDistinctiveExtras(longer, shorter))
        {
            return 0.95;
        }

        return shorter.Length == 1 ? 0.55 : 0.7;
    }

    private static bool HasDistinctiveExtras(string[] longer, string[] shorter)
    {
        var known = new HashSet<string>(shorter, StringComparer.Ordinal);
        return longer.Any(token => !known.Contains(token) && !NeutralTokens.Contains(token));
    }

    /// <summary>"Ultima VII: The Black Gate" and "Ultima VII" are the same game; a colon marks a subtitle.</summary>
    private static double SubtitleScore(string left, string right, string normalizedLeft, string normalizedRight)
    {
        string? rightHead = Head(right);
        if (rightHead is not null && Normalize(rightHead) == normalizedLeft)
        {
            return 0.9;
        }

        string? leftHead = Head(left);
        if (leftHead is not null && Normalize(leftHead) == normalizedRight)
        {
            return 0.9;
        }

        return 0;
    }

    private static string? Head(string value)
    {
        int index = value.IndexOf(':');
        return index > 0 && index < value.Length - 1 ? value[..index] : null;
    }

    /// <summary>
    /// Picks the name shown to the user. A catalogue title that only adds a qualifier ("DOOM Alpha")
    /// should not replace the plain folder name; a title that adds a real word ("Commander Keen 4") should.
    /// </summary>
    public static string PreferDisplayTitle(string localTitle, string matchedTitle)
    {
        string[] local = SplitTokens(Normalize(localTitle));
        string[] matched = SplitTokens(Normalize(matchedTitle));

        if (local.Length >= matched.Length || !IsPrefix(local, matched) || HasDistinctiveExtras(matched, local))
        {
            return matchedTitle;
        }

        return localTitle;
    }

    private static bool IsPrefix(string[] shorter, string[] longer) =>
        shorter.Length <= longer.Length && shorter.SequenceEqual(longer.Take(shorter.Length));

    private static bool IsSuffix(string[] shorter, string[] longer) =>
        shorter.Length <= longer.Length && shorter.SequenceEqual(longer.Skip(longer.Length - shorter.Length));

    private static string[] SplitTokens(string value) =>
        Whitespace.Replace(value, " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Normalize(string value)
    {
        string cleaned = Clean(value).ToLowerInvariant();
        var builder = new StringBuilder(cleaned.Length);
        foreach (char character in cleaned)
        {
            if (char.IsLetterOrDigit(character) || character == ' ')
            {
                builder.Append(character);
            }
        }

        string[] tokens = SplitTokens(builder.ToString());
        for (int index = 0; index < tokens.Length; index++)
        {
            if (RomanToDigit.TryGetValue(tokens[index], out string? digit))
            {
                tokens[index] = digit;
            }
        }

        return string.Join(' ', tokens);
    }

    private static double EditSimilarity(string left, string right)
    {
        const int MaxLength = 64;
        string a = left.Length > MaxLength ? left[..MaxLength] : left;
        string b = right.Length > MaxLength ? right[..MaxLength] : right;

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int index = 0; index <= b.Length; index++)
        {
            previous[index] = index;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return 1.0 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }

    private static bool HasNumberMismatch(HashSet<string> left, HashSet<string> right)
    {
        foreach (string token in left.Union(right))
        {
            if (token.All(char.IsDigit) && !(left.Contains(token) && right.Contains(token)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesExecutableName(string title, HashSet<string> truncations)
    {
        string truncation = Truncate(title);
        if (truncation.Length < 5)
        {
            return false;
        }

        return truncations.Any(name =>
            name.Length >= 5 &&
            (name.Equals(truncation, StringComparison.Ordinal) ||
             name.StartsWith(truncation, StringComparison.Ordinal) ||
             truncation.StartsWith(name, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Guards a title guessed from a scene description: it must look like the folder or one of the programs,
    /// otherwise banner art and license text would turn into search queries.
    /// </summary>
    public static bool MatchesAnyFileName(string title, IEnumerable<string> fileNames)
    {
        string letters = LettersOnly(title);
        if (letters.Length < 3)
        {
            return false;
        }

        foreach (string fileName in fileNames)
        {
            string other = LettersOnly(Path.GetFileNameWithoutExtension(fileName));
            if (other.Length < 3)
            {
                continue;
            }

            if (letters.Equals(other, StringComparison.Ordinal) ||
                (other.Length >= 4 && letters.StartsWith(other, StringComparison.Ordinal)) ||
                EditSimilarity(letters, other) >= 0.8)
            {
                return true;
            }
        }

        return false;
    }

    private static string LettersOnly(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).Take(8).ToArray());
}
