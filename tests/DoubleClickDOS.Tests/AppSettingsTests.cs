namespace DoubleClickDOS.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DoubleClickDOS.Tests", Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void UnconfiguredDosBox_DoesNotResolve()
    {
        Assert.Null(new AppSettings().ResolveDosBoxExecutable());
    }

    [Fact]
    public void FolderContainingDosBox_ResolvesToExecutable()
    {
        string executable = Path.Combine(_directory, "dosbox.exe");
        File.WriteAllBytes(executable, []);

        Assert.Equal(executable, new AppSettings { DosBoxPath = _directory }.ResolveDosBoxExecutable());
    }

    [Fact]
    public void FolderContainingDosBoxX_PrefersDosBoxX()
    {
        string classic = Path.Combine(_directory, "dosbox.exe");
        string dosBoxX = Path.Combine(_directory, "dosbox-x.exe");
        File.WriteAllBytes(classic, []);
        File.WriteAllBytes(dosBoxX, []);

        Assert.Equal(dosBoxX, new AppSettings { DosBoxPath = _directory }.ResolveDosBoxExecutable());
    }

    [Fact]
    public void SelectedExecutable_IsUsedDirectly()
    {
        string executable = Path.Combine(_directory, "dosbox.exe");
        File.WriteAllBytes(executable, []);

        Assert.Equal(executable, new AppSettings { DosBoxPath = executable }.ResolveDosBoxExecutable());
    }

    [Fact]
    public void MissingPath_DoesNotResolve()
    {
        Assert.Null(new AppSettings { DosBoxPath = Path.Combine(_directory, "missing") }.ResolveDosBoxExecutable());
    }

    [Fact]
    public void UnconfiguredMunt_DoesNotResolve()
    {
        Assert.Null(new AppSettings().ResolveMuntExecutable());
    }

    [Fact]
    public void FolderContainingMunt_ResolvesToExecutable()
    {
        string executable = Path.Combine(_directory, "mt32emu-qt.exe");
        File.WriteAllBytes(executable, []);

        Assert.Equal(executable, new AppSettings { MuntPath = _directory }.ResolveMuntExecutable());
    }

    [Fact]
    public void FolderContainingBothMuntExecutables_PrefersTheQtApp()
    {
        string legacy = Path.Combine(_directory, "munt.exe");
        string qt = Path.Combine(_directory, "mt32emu-qt.exe");
        File.WriteAllBytes(legacy, []);
        File.WriteAllBytes(qt, []);

        Assert.Equal(qt, new AppSettings { MuntPath = _directory }.ResolveMuntExecutable());
    }

    [Fact]
    public void SelectedMuntExecutable_IsUsedDirectly()
    {
        string executable = Path.Combine(_directory, "munt.exe");
        File.WriteAllBytes(executable, []);

        Assert.Equal(executable, new AppSettings { MuntPath = executable }.ResolveMuntExecutable());
    }

    [Fact]
    public void MissingMuntPath_DoesNotResolve()
    {
        Assert.Null(new AppSettings { MuntPath = Path.Combine(_directory, "missing") }.ResolveMuntExecutable());
    }

    [Fact]
    public void MigrateLegacyValues_TakesOverTheDosBoxXPath()
    {
        var settings = new AppSettings { DosBoxXPath = @"C:\DOSBox-X\dosbox-x.exe" };

        settings.MigrateLegacyValues();

        Assert.Equal(@"C:\DOSBox-X\dosbox-x.exe", settings.DosBoxPath);
        Assert.Null(settings.DosBoxXPath);
    }

    [Fact]
    public void MigrateLegacyValues_KeepsCustomArgumentsButDropsFastlaunch()
    {
        var settings = new AppSettings { DosBoxXArguments = "-fastlaunch -fullscreen" };

        settings.MigrateLegacyValues();

        Assert.Equal("-fullscreen", settings.ExtraArguments);
        Assert.Equal(["-fullscreen"], settings.GetExtraArguments());
        Assert.Null(settings.DosBoxXArguments);
    }

    [Fact]
    public void Load_TakesOverTheSettingsFileOfEarlierVersions()
    {
        string legacyPath = Path.Combine(_directory, "legacy", "settings.json");
        string currentPath = Path.Combine(_directory, "current", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, """{ "DosBoxPath": "C:\\DOSBox-X\\dosbox-x.exe" }""");

        string originalCurrentPath = AppSettings.SettingsFilePath;
        string originalLegacyPath = AppSettings.LegacySettingsFilePath;
        try
        {
            AppSettings.SettingsFilePath = currentPath;
            AppSettings.LegacySettingsFilePath = legacyPath;

            AppSettings settings = AppSettings.Load();

            Assert.Equal(@"C:\DOSBox-X\dosbox-x.exe", settings.DosBoxPath);
            Assert.True(File.Exists(currentPath));
        }
        finally
        {
            AppSettings.SettingsFilePath = originalCurrentPath;
            AppSettings.LegacySettingsFilePath = originalLegacyPath;
        }
    }
}
