namespace DosLaunch.Tests;

public sealed class DosBoxConfigFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public DosBoxConfigFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void GetConfigPath_PutsConfigNextToTheProgram()
    {
        string configPath = DosBoxConfigFile.GetConfigPath(@"C:\Spiele\Worms\WORMS.EXE");

        Assert.Equal(@"C:\Spiele\Worms\WORMS.doslaunch.conf", configPath);
    }

    [Fact]
    public void ResolveConfigPath_SeparatesProgramsThatShareABaseName()
    {
        // WILLY.EXE and WILLY.BAT cannot share one config: each autoexec starts a different program.
        string exeConfig = Path.Combine(_directory, "WILLY.doslaunch.conf");
        File.WriteAllText(exeConfig, $"# Program: {Path.Combine(_directory, "WILLY.EXE")}{Environment.NewLine}");

        Assert.Equal(exeConfig, DosBoxConfigFile.ResolveConfigPath(Path.Combine(_directory, "WILLY.EXE")));
        Assert.Equal(
            Path.Combine(_directory, "WILLY.BAT.doslaunch.conf"),
            DosBoxConfigFile.ResolveConfigPath(Path.Combine(_directory, "WILLY.BAT")));
    }

    [Fact]
    public void ResolveConfigPath_KeepsAConfigThatDoesNotNameItsProgram()
    {
        string configPath = Path.Combine(_directory, "WILLY.doslaunch.conf");
        File.WriteAllText(configPath, $"[autoexec]{Environment.NewLine}WILLY.EXE{Environment.NewLine}");

        Assert.Equal(configPath, DosBoxConfigFile.ResolveConfigPath(Path.Combine(_directory, "WILLY.BAT")));
    }

    [Fact]
    public void ResolveConfigPath_UsesThePlainNameWhileNoConfigExists()
    {
        Assert.Equal(
            Path.Combine(_directory, "WILLY.doslaunch.conf"),
            DosBoxConfigFile.ResolveConfigPath(Path.Combine(_directory, "WILLY.BAT")));
    }

    [Fact]
    public void Render_StartsFullscreenWithAspectCorrection()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBox);

        Assert.Contains("[sdl]", config);
        Assert.Contains("fullscreen=true", config);
        Assert.Contains("[render]", config);
        Assert.Contains("aspect=true", config);
    }

    [Fact]
    public void Render_ClosesDosBoxAfterTheProgramEnds()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBox);

        string[] lines = config.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("exit", lines[^1]);
        Assert.True(Array.IndexOf(lines, "DOOM.EXE") < lines.Length - 1);
    }

    [Fact]
    public void Render_MountsGameFolderAndDiscImage()
    {
        var profile = CreateProfile(new CdDrive("d", @"C:\Games\Doom\DOOM.cue", "cdrom", []));

        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], profile, null, DosBoxFlavor.DosBox);

        Assert.Contains("mount c \"C:\\Games\\Doom\"", config);
        Assert.Contains("imgmount d \"C:\\Games\\Doom\\DOOM.cue\" -t cdrom", config);
        Assert.Contains("DOOM.EXE", config);
    }

    [Fact]
    public void Render_MountsFolderAsCdRomWhenThereIsNoImage()
    {
        var profile = CreateProfile(new CdDrive("d", @"C:\Games\Doom", null, []));

        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], profile, null, DosBoxFlavor.DosBox);

        Assert.Contains("mount d \"C:\\Games\\Doom\" -t cdrom", config);
        Assert.DoesNotContain("imgmount", config);
    }

    [Fact]
    public void Render_CallsBatchFilesSoDosBoxStillQuitsAfterwards()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\START.BAT", [], CreateProfile(), null, DosBoxFlavor.DosBox);

        Assert.Contains("call START.BAT", config);
    }

    [Fact]
    public void Render_PassesProgramArguments()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", ["-nosound", "-warp"], CreateProfile(), null, DosBoxFlavor.DosBox);

        Assert.Contains("DOOM.EXE -nosound -warp", config);
    }

    [Fact]
    public void Render_QuotesTheProgramOnlyWhenItContainsSpaces()
    {
        // Classic DOSBox ignores a quoted command name, so plain names must stay unquoted.
        string plain = DosBoxConfigFile.Render(@"C:\Games\DOOM.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBox);
        string spaced = DosBoxConfigFile.Render(@"C:\Games\My Game.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBox);

        Assert.Contains("DOOM.EXE" + Environment.NewLine, plain);
        Assert.DoesNotContain("\"DOOM.EXE\"", plain);
        Assert.Contains("\"My Game.EXE\"", spaced);
    }

    [Fact]
    public void SyncMountPath_FollowsAMovedGameFolder()
    {
        string configPath = WriteConfig("[autoexec]\r\nmount c \"C:\\Games\\Old Location\"\r\nc:\r\n\"WORMS.EXE\"\r\n");

        DosBoxConfigFile.SyncMountPath(configPath, @"C:\Games\New Location");

        string content = File.ReadAllText(configPath);
        Assert.Contains("mount c \"C:\\Games\\New Location\"", content);
        Assert.DoesNotContain("Old Location", content);
        Assert.Contains("\"WORMS.EXE\"", content);
    }

    [Fact]
    public void SyncMountPath_UpdatesImagePathsInsideTheMovedFolder()
    {
        string configPath = WriteConfig(
            "[autoexec]\r\nmount c \"C:\\Games\\Old\"\r\nimgmount d \"C:\\Games\\Old\\disc.cue\" -t cdrom\r\nc:\r\n\"GAME.EXE\"\r\n");

        DosBoxConfigFile.SyncMountPath(configPath, @"C:\Games\New");

        string content = File.ReadAllText(configPath);
        Assert.Contains("imgmount d \"C:\\Games\\New\\disc.cue\" -t cdrom", content);
        Assert.DoesNotContain(@"C:\Games\Old", content);
    }

    [Fact]
    public void SyncMountPath_LeavesHandWrittenConfigsAlone()
    {
        const string original = "[autoexec]\r\n@echo off\r\ncd \\games\r\n";
        string configPath = WriteConfig(original);

        DosBoxConfigFile.SyncMountPath(configPath, @"C:\Games\New Location");

        Assert.Equal(original, File.ReadAllText(configPath));
    }

    [Fact]
    public void SyncLaunchOptions_UpgradesConfigsFromOlderVersions()
    {
        string configPath = WriteConfig(
            "[dosbox]\r\nmemsize=16\r\n\r\n[autoexec]\r\nmount c \"C:\\Games\\Doom\"\r\nc:\r\n\"DOOM.EXE\"\r\n");

        DosBoxConfigFile.SyncLaunchOptions(configPath, "GAME.EXE", DosBoxFlavor.DosBox);

        string content = File.ReadAllText(configPath);
        Assert.Contains("fullscreen=true", content);
        Assert.Contains("aspect=true", content);
        Assert.Equal("exit", content.TrimEnd().Split('\n')[^1].Trim());
        // new sections must not end up after [autoexec]
        Assert.True(content.IndexOf("[sdl]", StringComparison.Ordinal) < content.IndexOf("[autoexec]", StringComparison.Ordinal));
    }

    [Fact]
    public void SyncLaunchOptions_KeepsExistingValuesAndDoesNotDuplicate()
    {
        // every managed key present, with deliberate values that must survive
        string configPath = WriteConfig(
            "[sdl]\r\nfullscreen=false\r\nfullresolution=desktop\r\noutput=surface\r\n\r\n" +
            "[render]\r\naspect=true\r\nscaler=normal2x\r\n\r\n[autoexec]\r\nc:\r\nexit\r\n");
        string before = File.ReadAllText(configPath);

        DosBoxConfigFile.SyncLaunchOptions(configPath, "GAME.EXE", DosBoxFlavor.DosBox);

        Assert.Equal(before, File.ReadAllText(configPath));
    }


    [Fact]
    public void SyncLaunchOptions_RemovesQuotesAroundTheProgramName()
    {
        string configPath = WriteConfig(
            "[sdl]\r\nfullscreen=true\r\n\r\n[render]\r\naspect=true\r\n\r\n[autoexec]\r\nmount c \"C:\\Games\"\r\nc:\r\n\"GAME.EXE\"\r\nexit\r\n");

        DosBoxConfigFile.SyncLaunchOptions(configPath, "GAME.EXE", DosBoxFlavor.DosBox);

        string content = File.ReadAllText(configPath);
        Assert.DoesNotContain("\"GAME.EXE\"", content);
        Assert.Contains("GAME.EXE" + "\r\n", content);
    }


    [Fact]
    public void Render_UsesSharpFullscreenScalingForDosBoxX()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBoxX);

        Assert.Contains("output=openglnb", config);   // OpenGL nearest
        Assert.Contains("aspect=nearest", config);    // nearest neighbour aspect correction
        Assert.Contains("scaler=none", config);
        Assert.Contains("dpi aware=true", config);    // stops Windows from rescaling the output
    }

    [Fact]
    public void Render_UsesSharpFullscreenScalingForClassicDosBox()
    {
        string config = DosBoxConfigFile.Render(@"C:\Games\Doom\DOOM.EXE", [], CreateProfile(), null, DosBoxFlavor.DosBox);

        Assert.Contains("output=ddraw", config);      // surface cannot scale to fullscreen at all
        Assert.Contains("aspect=true", config);       // classic DOSBox has no filter choice
        Assert.Contains("scaler=none", config);
        Assert.DoesNotContain("dpi aware", config);
    }

    [Fact]
    public void SyncLaunchOptions_AddsScalingOptionsToOlderConfigs()
    {
        string configPath = WriteConfig("[dosbox]\r\nmemsize=16\r\n\r\n[autoexec]\r\nmount c \"C:\\Games\"\r\nc:\r\nGAME.EXE\r\n");

        DosBoxConfigFile.SyncLaunchOptions(configPath, "GAME.EXE", DosBoxFlavor.DosBoxX);

        string content = File.ReadAllText(configPath);
        Assert.Contains("output=openglnb", content);
        Assert.Contains("aspect=nearest", content);
        Assert.Contains("scaler=none", content);
        Assert.Contains("dpi aware=true", content);
        // keys must land inside their sections, not after [autoexec]
        Assert.True(content.IndexOf("dpi aware=true", StringComparison.Ordinal) < content.IndexOf("[autoexec]", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_DocumentsTheEstimatedGameAndYear()
    {
        var identity = new GameIdentity(
            "Prince of Persia",
            1989,
            false,
            GameEra.UpTo1989,
            GameConfidence.High,
            [
                new YearEvidence(1989, 0.9, false, "wikidata", "Wikidata lists \"Prince of Persia\" for DOS (1989)"),
                new YearEvidence(1989, 0.85, false, "archive.org", "archive.org MS-DOS collection lists \"Prince of Persia\" (1989)"),
            ],
            HardwareHints.Empty);

        string config = DosBoxConfigFile.Render(
            @"C:\Games\Prince\PRINCE.EXE", [], CreateProfile(), identity, DosBoxFlavor.DosBox);

        Assert.Contains("# Game:    Prince of Persia - estimated 1989", config);
        Assert.Contains("# Why:     high confidence", config);
        Assert.Contains("Wikidata lists", config);
        Assert.Contains("# Preset:  1981-1989 era", config);
    }

    [Fact]
    public void Render_ExplainsWhenTheYearIsUnknown()
    {
        string config = DosBoxConfigFile.Render(
            @"C:\Games\Prince\PRINCE.EXE", [], CreateProfile(), GameIdentity.Unknown(), DosBoxFlavor.DosBox);

        Assert.Contains("# Game:    unknown program - unknown", config);
        Assert.Contains("no release year found", config);
        Assert.Contains("conservative defaults", config);
    }

    private static GameProfile CreateProfile(CdDrive? cd = null) => new()
    {
        MemoryMb = 16,
        CpuType = "auto",
        Cycles = "auto",
        SoundBlaster = "sb16",
        Cd = cd ?? new CdDrive("d", @"C:\Games\Doom", null, []),
        Notes = ["generated by test"],
    };

    private string WriteConfig(string content)
    {
        string path = Path.Combine(_directory, "GAME.doslaunch.conf");
        File.WriteAllText(path, content);
        return path;
    }
}





