namespace DoubleClickDOS;

internal sealed record LocalEvidence(
    IReadOnlyList<YearEvidence> Years,
    HardwareHints Hints,
    IReadOnlyList<TitleCandidate> Titles,
    bool HasAuthoritativeIdentity);

/// <summary>
/// Collects everything a game folder can tell about itself without a network: dated text, build stamps
/// and hardware requirements in the executables, plus possible game names.
/// </summary>
internal static class LocalGameEvidence
{
    private const int MaxTextFiles = 40;
    private const int MaxTextBytes = 256 * 1024;
    private const int MaxBinaryFiles = 3;
    private const long MaxBinaryBytes = 2 * 1024 * 1024;
    private const int MaxScannedEntries = 4000;
    private const int MaxScannedSubfolders = 20;
    private const int MinStringLength = 5;
    private const int MaxStrings = 8000;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".diz", ".nfo", ".doc", ".cfg", ".ini", ".bat", ".1st", ".me", ".lst", ".bbs", ".ans",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".ovl", ".rtl", ".dll",
    };

    public static LocalEvidence Collect(string executablePath)
    {
        string folder = Path.GetDirectoryName(executablePath) ?? string.Empty;
        List<string> files = GetFiles(folder);
        var textFiles = new List<string>();
        var binaryFiles = new List<string>();

        void Classify(string path)
        {
            string extension = Path.GetExtension(path);
            if (TextExtensions.Contains(extension))
            {
                textFiles.Add(path);
            }
            else if (BinaryExtensions.Contains(extension))
            {
                binaryFiles.Add(path);
            }
        }

        foreach (string path in files.Take(MaxScannedEntries))
        {
            Classify(path);
        }

        foreach (string subfolder in GetFolders(folder).Take(MaxScannedSubfolders))
        {
            foreach (string path in GetFiles(subfolder))
            {
                Classify(path);
            }
        }

        var years = new List<YearEvidence>();
        HardwareHints hints = HardwareHints.Empty;
        string? dizTitle = null;
        double dizTitleScore = 0.8;
        string? readmeHeading = null;
        bool authoritative = false;

        var alignmentNames = new List<string> { Path.GetFileName(folder) };
        alignmentNames.AddRange(files.Select(Path.GetFileName).OfType<string>());

        List<string> selectedTextFiles = textFiles
            .OrderBy(TextPriority)
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Take(MaxTextFiles)
            .ToList();

        foreach (string path in selectedTextFiles)
        {
            string text = ReadText(path, MaxTextBytes);
            if (text.Length == 0)
            {
                continue;
            }

            string origin = Path.GetFileName(path);
            years.AddRange(GameTextEvidence.YearEvidenceFrom(text, origin, fromExecutable: false));
            hints = hints.Merge(GameTextEvidence.HintsFrom(text));

            if (IsSceneDescription(origin))
            {
                DizInfo diz = GameTextEvidence.ReadDiz(text);
                if (diz.HasAuthoritativeIdentity)
                {
                    authoritative = true;
                }

                if (diz.Title is { } structuredTitle && dizTitle is null)
                {
                    dizTitle = structuredTitle;

                    // A curated name beats even a folder name that matches the start program.
                    dizTitleScore = diz.HasAuthoritativeIdentity ? 1.0 : 0.9;
                }

                if (diz.FieldYear is int fieldYear)
                {
                    years.Add(new YearEvidence(
                        fieldYear,
                        0.9,
                        IsLowerBound: false,
                        "file_id.diz fields",
                        $"{origin}: Year field says {fieldYear}"));
                }

                if (diz.FileNameYear is int fileNameYear)
                {
                    years.Add(new YearEvidence(
                        fileNameYear,
                        0.8,
                        IsLowerBound: false,
                        "file_id.diz name",
                        $"{origin}: file name says {fileNameYear}"));
                }

                if (diz.FieldYear is null && diz.FileNameYear is null)
                {
                    years.AddRange(GameTextEvidence.SceneYears(text, origin));
                }

                if (diz.Title is null &&
                    GameTextEvidence.SceneTitle(text) is { } sceneTitle &&
                    GameTitles.MatchesAnyFileName(sceneTitle, alignmentNames) &&
                    dizTitle is null)
                {
                    dizTitle = sceneTitle;
                    dizTitleScore = 0.9;
                }
            }
            else if (readmeHeading is null && IsReadme(origin))
            {
                readmeHeading = GameTextEvidence.ReadmeHeading(text);
            }
        }

        foreach (string path in BinaryScanOrder(executablePath, binaryFiles).Take(MaxBinaryFiles))
        {
            List<string> strings = BinaryStrings.FromFile(path, MaxBinaryBytes, MinStringLength, MaxStrings);
            if (strings.Count == 0)
            {
                continue;
            }

            string text = string.Join('\n', strings);
            years.AddRange(GameTextEvidence.YearEvidenceFrom(text, $"{Path.GetFileName(path)} strings", fromExecutable: true));
            hints = hints.Merge(GameTextEvidence.HintsFrom(text));
        }

        if (authoritative)
        {
            // The description is curated data; executable copyrights are often engine, installer or
            // re-release years and must not override it.
            years = years
                .Where(item => item.Source is "file_id.diz fields" or "file_id.diz name" or "scene description")
                .ToList();
        }

        List<TitleCandidate> titles = GameTitles.Build(
            executablePath,
            files.Select(Path.GetFileName).OfType<string>(),
            dizTitle,
            readmeHeading,
            dizTitleScore);

        Log.Write(
            $"Local game evidence for {Path.GetFileName(executablePath)}: " +
            $"{years.Count} year hints, {titles.Count} title candidates, {selectedTextFiles.Count} text files scanned" +
            $"{(authoritative ? ", file_id.diz identified the game" : string.Empty)}.");

        return new LocalEvidence(years, hints, titles, authoritative);
    }

    private static bool IsSceneDescription(string fileName) =>
        fileName.EndsWith(".diz", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase);

    private static int TextPriority(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Equals("file_id.diz", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith("readme", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".diz" or ".nfo" => 2,
            ".txt" => 3,
            ".1st" or ".me" => 4,
            ".bat" => 5,
            ".cfg" or ".ini" => 6,
            _ => 7,
        };
    }

    private static bool IsReadme(string fileName) =>
        fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("manual", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("instruct", StringComparison.OrdinalIgnoreCase);

    private static List<string> BinaryScanOrder(string executablePath, List<string> binaries)
    {
        var ordered = new List<string>();
        if (binaries.Any(path => path.Equals(executablePath, StringComparison.OrdinalIgnoreCase)))
        {
            ordered.Add(executablePath);
        }

        ordered.AddRange(binaries
            .Where(path => !path.Equals(executablePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".exe" => 0,
                ".com" => 1,
                ".ovl" => 2,
                ".rtl" => 3,
                _ => 4,
            })
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));

        return ordered;
    }

    private static string ReadText(string path, int maxBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int length = (int)Math.Min(stream.Length, maxBytes);
            var buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int chunk = stream.Read(buffer, read, length - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            return AsciiText(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read {path}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string AsciiText(ReadOnlySpan<byte> bytes)
    {
        var characters = new char[bytes.Length];
        for (int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            characters[index] = value is (>= 0x20 and <= 0x7E) or (byte)'\n' or (byte)'\r' or (byte)'\t'
                ? (char)value
                : ' ';
        }

        return new string(characters);
    }

    private static List<string> GetFiles(string folder)
    {
        try
        {
            return [.. Directory.GetFiles(folder)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not scan {folder} for game evidence: {ex.Message}");
            return [];
        }
    }

    private static string[] GetFolders(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not scan {folder} for subfolders: {ex.Message}");
            return [];
        }
    }
}
