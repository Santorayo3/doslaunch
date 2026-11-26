namespace DosLaunch.Tests;

public sealed class GameInfoLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public GameInfoLookupTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("Worms is a 2D artillery tactical video game developed by Team17 and released in 1995.", 1995)]
    [InlineData("1995 video game", 1995)]
    [InlineData("A 1988 adventure game by Sierra.", 1988)]
    [InlineData("No year in this text at all.", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractYear_FindsReleaseYear(string? text, int? expected)
    {
        Assert.Equal(expected, GameTextEvidence.ExtractYear(text));
    }

    [Fact]
    public void ExtractYear_IgnoresYearsOutsideTheDosEra()
    {
        Assert.Null(GameTextEvidence.ExtractYear("Remastered in 2016 for modern systems."));
        Assert.Null(GameTextEvidence.ExtractYear("Published in 2001 by someone."));
    }

    [Fact]
    public void Build_PrefersTheFileIdDizTitleOverTheFolderName()
    {
        string executable = CreateExecutable("Worms (1995)", "WORMS.EXE");

        List<TitleCandidate> titles = GameTitles.Build(executable, ["WORMS.EXE"], "Worms", null);

        Assert.Equal("Worms", titles[0].Title);
        Assert.Equal("FILE_ID.DIZ", titles[0].Source);
    }

    [Fact]
    public void Build_FallsBackToTheExecutableNameForGenericFolders()
    {
        string executable = CreateExecutable("DOS", "Commander Keen 4.exe");

        List<TitleCandidate> titles = GameTitles.Build(executable, ["Commander Keen 4.exe"], null, null);

        Assert.Equal("Commander Keen 4", titles[0].Title);
    }

    [Fact]
    public void Build_ReturnsNothingWhenEveryNameIsGeneric()
    {
        string executable = CreateExecutable("Games", "GAME.EXE");

        Assert.Empty(GameTitles.Build(executable, ["GAME.EXE"], null, null));
    }

    [Fact]
    public void Build_DoesNotTrustNamesWithOnlyNumbers()
    {
        string executable = CreateExecutable("1995", "1995.EXE");

        Assert.Empty(GameTitles.Build(executable, ["1995.EXE"], null, null));
    }

    [Theory]
    [InlineData("Worms", "Worms (1995 video game)", true)]
    [InlineData("Prince of Persia", "Prince of Persia (1989 video game)", true)]
    [InlineData("Prince of Persia", "The Prince of Persia", true)]
    [InlineData("Keen 4", "Commander Keen 4", true)]
    [InlineData("Ultima 7", "Ultima VII: The Black Gate", true)]
    [InlineData("Heroes of Might and Magic II", "Heroes of Might and Magic II: The Succession Wars", true)]
    [InlineData("Prince of Persia", "Prince of Persia 2: The Shadow and the Flame", false)]
    [InlineData("Doom", "Doom II", false)]
    [InlineData("Doom", "Lords of Doom", false)]
    [InlineData("Doom 3", "Hugo 3 - Jungle of Doom", false)]
    [InlineData("Test Game", "Mixtape (video game)", false)]
    public void TitlesMatch_RejectsOtherGames(string expected, string candidate, bool matches)
    {
        Assert.Equal(matches, GameTitles.TitlesMatch(expected, candidate));
    }

    [Theory]
    [InlineData("Doom", "DOOM Alpha", "Doom")]
    [InlineData("Keen 4", "Commander Keen 4", "Commander Keen 4")]
    [InlineData("Prince of Persia", "Prince of Persia", "Prince of Persia")]
    public void PreferDisplayTitle_IgnoresCatalogueQualifiers(string local, string matched, string expected)
    {
        Assert.Equal(expected, GameTitles.PreferDisplayTitle(local, matched));
    }

    [Theory]
    [InlineData("Worms (1995)", "Worms")]
    [InlineData("MS-DOS: Prince of Persia (Hercules Graphics and GameBlaster)", "Prince of Persia")]
    [InlineData("Wolfenstein 3D - CRACKED", "Wolfenstein 3D")]
    [InlineData("Commander_Keen_4_v1.4", "Commander Keen 4")]
    [InlineData("Alpine Tram Ride v1.3 (1989)(Merit Software) [Educational].zip", "Alpine Tram Ride")]
    [InlineData("Great Fighter, The", "The Great Fighter")]
    public void Clean_RemovesPackagingNoise(string raw, string expected)
    {
        Assert.Equal(expected, GameTitles.Clean(raw));
    }

    [Theory]
    [InlineData("SKUNNY KART", new[] { "SKUNKART.EXE" }, true)]
    [InlineData("BAD BLOOD", new[] { "BADBLOOD.EXE" }, true)]
    [InlineData("THIS IS PIRATED SOFTWARE", new[] { "BASH123.EXE" }, false)]
    public void MatchesAnyFileName_RequiresAlignment(string title, string[] fileNames, bool expected)
    {
        Assert.Equal(expected, GameTitles.MatchesAnyFileName(title, fileNames));
    }

    private string CreateExecutable(string folderName, string fileName)
    {
        string folder = Path.Combine(_root, folderName);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, []);
        return path;
    }
}
