namespace DosLaunch.Tests;

public sealed class GameProfileBuilderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public GameProfileBuilderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(1988, 2, "386", "sb1")]
    [InlineData(1991, 4, "386", "sbpro2")]
    [InlineData(1995, 16, "auto", "sb16")]
    [InlineData(1998, 32, "auto", "sb16")]
    public void Create_PicksHardwareFromReleaseYear(int year, int expectedMemory, string expectedCpu, string expectedSoundBlaster)
    {
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), Identity(year));

        Assert.Equal(expectedMemory, profile.MemoryMb);
        Assert.Equal(expectedCpu, profile.CpuType);
        Assert.Equal(expectedSoundBlaster, profile.SoundBlaster);
    }

    [Fact]
    public void Create_UsesDefaultsWhenReleaseYearIsUnknown()
    {
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.Equal(16, profile.MemoryMb);
        Assert.Equal("auto", profile.CpuType);
        Assert.Equal("sb16", profile.SoundBlaster);
    }

    [Fact]
    public void Create_DetectsDosExtenderAndRaisesProtectedModeRequirements()
    {
        AddFile("DOS4GW.EXE");
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), Identity(1991));

        Assert.Equal(16, profile.MemoryMb);
        Assert.Equal("386", profile.CpuType);
    }

    [Fact]
    public void Create_MountsTheGameFolderAsCdRomWhenThereIsNoImage()
    {
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.False(profile.Cd.IsImage);
        Assert.Equal("d", profile.Cd.Letter);
        Assert.Equal(_folder, profile.Cd.Target);
    }

    [Fact]
    public void Create_MountsCueSheetAsCdRomImage()
    {
        AddFile("disc.cue");
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.True(profile.Cd.IsImage);
        Assert.Equal("cdrom", profile.Cd.ImageType);
        Assert.Equal(Path.Combine(_folder, "disc.cue"), profile.Cd.Target);
        Assert.Empty(profile.Cd.OtherImages);
    }

    [Fact]
    public void Create_MountsIsoWithIsoType()
    {
        AddFile("disc.iso");
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.Equal("iso", profile.Cd.ImageType);
    }

    [Fact]
    public void Create_PrefersImageOverDiscSubfolder()
    {
        string discFolder = Path.Combine(_folder, "CD");
        Directory.CreateDirectory(discFolder);
        File.WriteAllBytes(Path.Combine(discFolder, "DATA.DAT"), []);
        AddFile("game.cue");

        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.Equal(Path.Combine(_folder, "game.cue"), profile.Cd.Target);
    }

    [Fact]
    public void Create_MountsDiscSubfolderWhenThereIsNoImage()
    {
        string discFolder = Path.Combine(_folder, "CD");
        Directory.CreateDirectory(discFolder);
        File.WriteAllBytes(Path.Combine(discFolder, "DATA.DAT"), []);

        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.False(profile.Cd.IsImage);
        Assert.Equal(discFolder, profile.Cd.Target);
    }

    [Fact]
    public void Create_ReportsAdditionalDiscImages()
    {
        AddFile("disc1.cue");
        AddFile("disc2.cue");

        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), GameIdentity.Unknown());

        Assert.Equal(Path.Combine(_folder, "disc1.cue"), profile.Cd.Target);
        Assert.Single(profile.Cd.OtherImages);
        Assert.EndsWith("disc2.cue", profile.Cd.OtherImages[0]);
    }

    [Fact]
    public void Create_UsesTheEraOfAnIdentityAndDocumentsIt()
    {
        GameProfile profile = GameProfileBuilder.Create(AddFile("GAME.EXE"), Identity(1988));

        Assert.Equal(2, profile.MemoryMb);
        Assert.Equal("386", profile.CpuType);
        Assert.Equal("sb1", profile.SoundBlaster);
        Assert.Contains(profile.Notes, note => note.Contains("1981-1989"));
    }

    [Fact]
    public void Create_RaisesMemoryWhenTheDocumentationRequiresIt()
    {
        GameProfile profile = GameProfileBuilder.Create(
            AddFile("GAME.EXE"),
            Identity(1991) with { Hints = new HardwareHints(8, false) });

        Assert.Equal(8, profile.MemoryMb);
        Assert.Contains(profile.Notes, note => note.Contains("8 MB"));
    }

    [Fact]
    public void Create_KeepsTheLargerPresetWhenTheHintIsSmaller()
    {
        GameProfile profile = GameProfileBuilder.Create(
            AddFile("GAME.EXE"),
            Identity(1995) with { Hints = new HardwareHints(2, false) });

        Assert.Equal(16, profile.MemoryMb);
    }

    private static GameIdentity Identity(int year) => new(
        "Test Game",
        year,
        false,
        GameEraExtensions.FromYear(year),
        GameConfidence.Medium,
        [],
        HardwareHints.Empty);

    private string AddFile(string fileName)
    {
        string path = Path.Combine(_folder, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }
}

