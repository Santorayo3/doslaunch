namespace DosLaunch.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

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
}
