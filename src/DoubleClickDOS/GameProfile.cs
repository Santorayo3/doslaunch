using System.Text.RegularExpressions;

namespace DoubleClickDOS;

/// <summary>DOSBox settings chosen for one DOS program.</summary>
internal sealed class GameProfile
{
    public required int MemoryMb { get; init; }

    public required string CpuType { get; init; }

    public required string Cycles { get; init; }

    public required string SoundBlaster { get; init; }

    /// <summary>CD-ROM drive exposed to the game.</summary>
    public required CdDrive Cd { get; init; }

    /// <summary>Explanations for the choices, written into the config file as comments.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>CD-ROM drive of a generated config: either a disc image or a folder mounted as CD.</summary>
internal sealed record CdDrive(string Letter, string Target, string? ImageType, IReadOnlyList<string> OtherImages)
{
    public bool IsImage => ImageType is not null;
}

/// <summary>Derives a DOSBox profile from the contents of the program's folder and the game's release year.</summary>
internal static class GameProfileBuilder
{
    internal const string CdDriveLetter = "d";

    private const int MaxScannedEntries = 4000;
    private const int MaxScannedSubfolders = 20;

    private static readonly Regex CdFolderName =
        new(@"^(cd|cdrom|cd-rom|disc|disk|gamecd|media)\d*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> DosExtenderFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dos4gw.exe", "dos4gw", "dos32a.exe", "dos32a", "cwsdpmi.exe", "cwsdpmi", "pmode.exe", "pmodex", "rtm.exe",
    };

    private static readonly HashSet<string> SetupFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "setup.exe", "install.exe", "setsound.exe", "sndsetup.exe",
    };

    public static GameProfile Create(string executablePath, GameIdentity identity)
    {
        var notes = new List<string>();
        string folder = Path.GetDirectoryName(executablePath)!;
        FolderContents contents = ScanFolder(folder);

        (int memoryMb, string cpuType, string cycles, string soundBlaster) = Preset(identity.Era);

        if (identity.Year is null)
        {
            notes.Add("Release year unknown, so conservative defaults are used.");
        }
        else
        {
            notes.Add($"Release year {identity.YearText} ({identity.Era.Describe()} era), " +
                      "so era-appropriate hardware is configured.");
        }

        if (identity.Hints.MinimumMemoryMb is int minimumMemory && minimumMemory > memoryMb)
        {
            notes.Add($"The game's documentation mentions at least {minimumMemory} MB of RAM, so memsize is raised.");
            memoryMb = minimumMemory;
        }

        if (identity.Hints.RequiresProtectedMode)
        {
            notes.Add("The game's documentation requires a 386 or better, so at least 16 MB is configured.");
            memoryMb = Math.Max(memoryMb, 16);
        }

        if (contents.HasDosExtender)
        {
            notes.Add("A DOS extender (protected mode) is present: at least a 386 with 16 MB is configured.");
            memoryMb = Math.Max(memoryMb, 16);
        }

        if (contents.HasSetupProgram)
        {
            notes.Add("The folder has a setup program; run it if graphics or sound need adjusting.");
        }

        return new GameProfile
        {
            MemoryMb = memoryMb,
            CpuType = cpuType,
            Cycles = cycles,
            SoundBlaster = soundBlaster,
            Cd = BuildCdDrive(folder, contents, notes),
            Notes = notes,
        };
    }

    /// <summary>
    /// 2 MB keeps EMS/XMS available in the oldest bucket. A slow 386 is used because classic DOSBox has
    /// no 286 CPU type, which keeps the generated config portable between builds.
    /// </summary>
    private static (int MemoryMb, string CpuType, string Cycles, string SoundBlaster) Preset(GameEra era) => era switch
    {
        GameEra.UpTo1989 => (2, "386", "3000", "sb1"),
        GameEra.UpTo1992 => (4, "386", "auto", "sbpro2"),
        GameEra.UpTo1996 => (16, "auto", "auto", "sb16"),
        GameEra.Later => (32, "auto", "auto", "sb16"),
        _ => (16, "auto", "auto", "sb16"),
    };

    /// <summary>Prefers a disc image, then a disc-like subfolder, then the game folder itself.</summary>
    private static CdDrive BuildCdDrive(string folder, FolderContents contents, List<string> notes)
    {
        string drive = CdDriveLetter.ToUpperInvariant();

        if (contents.CdImages.Count > 0)
        {
            string image = contents.CdImages[0];
            string imageType = image.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ? "cdrom" : "iso";
            string[] others = contents.CdImages.Skip(1).ToArray();

            notes.Add($"CD image \"{Path.GetFileName(image)}\" is mounted as drive {drive}:. " +
                      "Change the letter or remove the imgmount line in [autoexec] if the game expects otherwise.");

            if (others.Length > 0)
            {
                string names = string.Join(", ", others.Take(3).Select(Path.GetFileName));
                notes.Add($"Other disc images found: {names}. Edit the imgmount line to swap discs.");
            }

            return new CdDrive(CdDriveLetter, image, imageType, others);
        }

        if (contents.CdFolder is { } cdFolder)
        {
            notes.Add($"Subfolder \"{Path.GetFileName(cdFolder)}\" is mounted as CD-ROM drive {drive}:.");
            return new CdDrive(CdDriveLetter, cdFolder, null, []);
        }

        notes.Add($"The game folder is mounted as CD-ROM drive {drive}: " +
                  "so games that look for their disc content find the files.");
        return new CdDrive(CdDriveLetter, folder, null, []);
    }

    private static FolderContents ScanFolder(string folder)
    {
        bool hasDosExtender = false;
        bool hasSetupProgram = false;
        var cdImages = new List<string>();
        string? cdFolder = null;
        int scanned = 0;

        void Inspect(string path)
        {
            string name = Path.GetFileName(path);
            if (DosExtenderFiles.Contains(name))
            {
                hasDosExtender = true;
            }
            else if (SetupFileNames.Contains(name))
            {
                hasSetupProgram = true;
            }
            else if (IsDiscImage(name))
            {
                cdImages.Add(path);
            }
        }

        foreach (string path in GetFiles(folder))
        {
            if (++scanned > MaxScannedEntries)
            {
                break;
            }

            Inspect(path);
        }

        string[] subfolders = GetFolders(folder);
        for (int index = 0; index < subfolders.Length && index < MaxScannedSubfolders && scanned <= MaxScannedEntries; index++)
        {
            string subfolder = subfolders[index];
            if (cdFolder is null && CdFolderName.IsMatch(Path.GetFileName(subfolder)))
            {
                cdFolder = subfolder;
            }

            foreach (string path in GetFiles(subfolder))
            {
                if (++scanned > MaxScannedEntries)
                {
                    break;
                }

                if (IsDiscImage(Path.GetFileName(path)))
                {
                    cdImages.Add(path);
                }
            }
        }

        // Images from the program's own folder win, and CUE sheets win because they carry the audio tracks.
        string programFolder = folder;
        cdImages.Sort((left, right) =>
        {
            int byFolder = (Path.GetDirectoryName(left) == programFolder ? 0 : 1)
                .CompareTo(Path.GetDirectoryName(right) == programFolder ? 0 : 1);
            if (byFolder != 0)
            {
                return byFolder;
            }

            int byType = (left.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .CompareTo(right.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            return byType != 0 ? byType : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        });

        return new FolderContents(hasDosExtender, hasSetupProgram, cdImages, cdFolder);
    }

    private static bool IsDiscImage(string fileName) =>
        fileName.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);

    private static List<string> GetFiles(string folder)
    {
        try
        {
            return [.. Directory.GetFiles(folder)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not scan {folder} for game hints: {ex.Message}");
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

    private sealed record FolderContents(
        bool HasDosExtender,
        bool HasSetupProgram,
        List<string> CdImages,
        string? CdFolder);
}
