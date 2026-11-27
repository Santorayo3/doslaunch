namespace DoubleClickDOS;

/// <summary>Years that make sense for a DOS program. Anything outside this window is not release evidence.</summary>
internal static class DosYears
{
    public const int Min = 1981;
    public const int Max = 2000;

    public static bool Contains(int year) => year is >= Min and <= Max;
}

/// <summary>
/// Hardware eras that map to the DOSBox presets in <see cref="GameProfileBuilder"/>. Buckets are used
/// instead of exact years because US/EU release differences and CD re-releases never change the settings.
/// </summary>
internal enum GameEra
{
    Unknown = 0,
    UpTo1989,
    UpTo1992,
    UpTo1996,
    Later,
}

internal enum GameConfidence
{
    None = 0,
    Low,
    Medium,
    High,
}

internal static class GameEraExtensions
{
    public static GameEra FromYear(int? year) => year switch
    {
        null => GameEra.Unknown,
        <= 1989 => GameEra.UpTo1989,
        <= 1992 => GameEra.UpTo1992,
        <= 1996 => GameEra.UpTo1996,
        _ => GameEra.Later,
    };

    public static string Describe(this GameEra era) => era switch
    {
        GameEra.UpTo1989 => "1981-1989",
        GameEra.UpTo1992 => "1990-1992",
        GameEra.UpTo1996 => "1993-1996",
        GameEra.Later => "1997 or later",
        _ => "unknown",
    };
}

/// <summary>A single dated hint. Lower bounds only guarantee that the game is not older than the year.</summary>
internal sealed record YearEvidence(int Year, double Weight, bool IsLowerBound, string Source, string Detail);

/// <summary>Hardware facts mentioned by the game's own documentation.</summary>
internal sealed record HardwareHints(int? MinimumMemoryMb, bool RequiresProtectedMode)
{
    public static HardwareHints Empty { get; } = new(null, false);

    public HardwareHints Merge(HardwareHints other)
    {
        int? memory = (MinimumMemoryMb, other.MinimumMemoryMb) switch
        {
            (int left, int right) => Math.Max(left, right),
            (int left, null) => left,
            (null, int right) => right,
            _ => null,
        };

        return new HardwareHints(memory, RequiresProtectedMode || other.RequiresProtectedMode);
    }
}

/// <summary>What Double-Click DOS believes about a program: name, approximate release year and the reasons for it.</summary>
internal sealed record GameIdentity(
    string? Title,
    int? Year,
    bool YearIsLowerBound,
    GameEra Era,
    GameConfidence Confidence,
    IReadOnlyList<YearEvidence> Evidence,
    HardwareHints Hints)
{
    public static GameIdentity Unknown(HardwareHints? hints = null) =>
        new(null, null, false, GameEra.Unknown, GameConfidence.None, [], hints ?? HardwareHints.Empty);

    /// <summary>"estimated 1989", "estimated 1994 or later" or "unknown".</summary>
    public string YearText => Year switch
    {
        null => "unknown",
        int year when YearIsLowerBound => $"estimated {year} or later",
        int year => $"estimated {year}",
    };
}
