using System.Text.Json;

namespace DosLaunch;

/// <summary>
/// Remembers resolved game identities, keyed by executable path. Configs are only generated once, so the
/// cache mainly avoids repeating the lookup when a config file was deleted.
/// </summary>
internal sealed class GameIdentityCache
{
    private const int CurrentVersion = 2;
    private static readonly TimeSpan RetryUnresolvedAfter = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    internal static string CachePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DosLaunch",
        "gameinfo.json");

    private readonly Dictionary<string, CachedIdentity> _entries;

    private GameIdentityCache(Dictionary<string, CachedIdentity> entries) => _entries = entries;

    public static string KeyFor(string executablePath)
    {
        try
        {
            return Path.GetFullPath(executablePath).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return executablePath.ToLowerInvariant();
        }
    }

    public static GameIdentityCache Load()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return new GameIdentityCache(new Dictionary<string, CachedIdentity>(StringComparer.OrdinalIgnoreCase));
            }

            string json = File.ReadAllText(CachePath);
            using JsonDocument probe = JsonDocument.Parse(json);
            if (probe.RootElement.ValueKind != JsonValueKind.Object ||
                !probe.RootElement.TryGetProperty("version", out _))
            {
                // The old cache keyed entries by game title and came from the unreliable heuristics.
                Log.Write("Discarding the game info cache written by an older version.");
                return new GameIdentityCache(new Dictionary<string, CachedIdentity>(StringComparer.OrdinalIgnoreCase));
            }

            CacheDocument? document = JsonSerializer.Deserialize<CacheDocument>(json, Options);
            if (document?.Entries is null)
            {
                return new GameIdentityCache(new Dictionary<string, CachedIdentity>(StringComparer.OrdinalIgnoreCase));
            }

            return new GameIdentityCache(new Dictionary<string, CachedIdentity>(document.Entries, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read the game info cache: {ex.Message}");
            return new GameIdentityCache(new Dictionary<string, CachedIdentity>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool TryGet(string key, bool allowOnline, out GameIdentity? identity)
    {
        identity = null;
        if (!_entries.TryGetValue(key, out CachedIdentity? entry) ||
            !Enum.TryParse(entry.Era, out GameEra era) ||
            !Enum.TryParse(entry.Confidence, out GameConfidence confidence))
        {
            return false;
        }

        if (era == GameEra.Unknown)
        {
            bool stale = entry.CheckedAt.ToUniversalTime() + RetryUnresolvedAfter < DateTime.UtcNow;
            if (stale || (!entry.UsedOnline && allowOnline))
            {
                return false;
            }
        }

        identity = new GameIdentity(
            entry.Title,
            entry.Year,
            entry.YearIsLowerBound,
            era,
            confidence,
            entry.Evidence ?? [],
            new HardwareHints(entry.MinimumMemoryMb, entry.RequiresProtectedMode));
        return true;
    }

    public void Store(string key, GameIdentity identity, bool usedOnline)
    {
        _entries[key] = new CachedIdentity
        {
            Title = identity.Title,
            Year = identity.Year,
            YearIsLowerBound = identity.YearIsLowerBound,
            Era = identity.Era.ToString(),
            Confidence = identity.Confidence.ToString(),
            MinimumMemoryMb = identity.Hints.MinimumMemoryMb,
            RequiresProtectedMode = identity.Hints.RequiresProtectedMode,
            Evidence = [.. identity.Evidence],
            CheckedAt = DateTime.UtcNow,
            UsedOnline = usedOnline,
        };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var document = new CacheDocument
            {
                Version = CurrentVersion,
                Entries = new Dictionary<string, CachedIdentity>(_entries, StringComparer.OrdinalIgnoreCase),
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(document, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not write the game info cache: {ex.Message}");
        }
    }

    private sealed class CacheDocument
    {
        public int Version { get; set; }

        public Dictionary<string, CachedIdentity> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CachedIdentity
    {
        public string? Title { get; set; }

        public int? Year { get; set; }

        public bool YearIsLowerBound { get; set; }

        public string Era { get; set; } = nameof(GameEra.Unknown);

        public string Confidence { get; set; } = nameof(GameConfidence.None);

        public int? MinimumMemoryMb { get; set; }

        public bool RequiresProtectedMode { get; set; }

        public List<YearEvidence> Evidence { get; set; } = [];

        public DateTime CheckedAt { get; set; }

        public bool UsedOnline { get; set; }
    }
}
