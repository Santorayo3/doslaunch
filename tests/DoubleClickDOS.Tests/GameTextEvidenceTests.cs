namespace DoubleClickDOS.Tests;

public sealed class GameTextEvidenceTests
{
    [Fact]
    public void CopyrightYear_IsALowerBound()
    {
        List<YearEvidence> evidence = GameTextEvidence.YearEvidenceFrom(
            "Copyright (C) 1993 id Software",
            "DOOM.EXE strings",
            fromExecutable: true);

        YearEvidence hint = Assert.Single(evidence);
        Assert.Equal(1993, hint.Year);
        Assert.True(hint.IsLowerBound);
        Assert.Contains("DOOM.EXE strings", hint.Detail);
    }

    [Fact]
    public void CopyrightRange_UsesTheNewestYear()
    {
        List<YearEvidence> evidence = GameTextEvidence.YearEvidenceFrom(
            "Copyright 1989-1993 Someone",
            "README.TXT",
            fromExecutable: false);

        YearEvidence hint = Assert.Single(evidence);
        Assert.Equal(1993, hint.Year);
    }

    [Fact]
    public void ExplicitReleaseYear_IsExactEvidence()
    {
        List<YearEvidence> evidence = GameTextEvidence.YearEvidenceFrom(
            "The game was released in 1995 by Team17.",
            "README.TXT",
            fromExecutable: false);

        YearEvidence hint = evidence.First(item => item.Source == "release note");
        Assert.Equal(1995, hint.Year);
        Assert.False(hint.IsLowerBound);
    }

    [Theory]
    [InlineData("Compiled Feb 12 1994", 1994)]
    [InlineData("Build date: 12 Feb 1994", 1994)]
    public void BuildStamp_IsExactEvidence(string text, int expected)
    {
        List<YearEvidence> evidence = GameTextEvidence.YearEvidenceFrom(text, "GAME.EXE strings", fromExecutable: true);

        YearEvidence hint = Assert.Single(evidence, item => item.Source == "build stamp");
        Assert.Equal(expected, hint.Year);
        Assert.False(hint.IsLowerBound);
    }

    [Theory]
    [InlineData("Copyright 2019 Remaster Studios")]
    [InlineData("Requires Windows 2000 or newer")]
    [InlineData("Call 555-1995 for support")]
    public void YearsOutsideTheDosEra_AreIgnored(string text)
    {
        Assert.DoesNotContain(
            GameTextEvidence.YearEvidenceFrom(text, "README.TXT", fromExecutable: false),
            item => item.Year is < DosYears.Min or > DosYears.Max);
    }

    [Fact]
    public void MemoryRequirement_IsDetected()
    {
        HardwareHints hints = GameTextEvidence.HintsFrom("Requires 8 MB RAM and VGA graphics.");

        Assert.Equal(8, hints.MinimumMemoryMb);
    }

    [Fact]
    public void DiskSpaceRequirement_IsNotTreatedAsMemory()
    {
        Assert.Null(GameTextEvidence.HintsFrom("Requires 20 MB free disk space.").MinimumMemoryMb);
    }

    [Fact]
    public void ProtectedModeRequirement_IsDetected()
    {
        Assert.True(GameTextEvidence.HintsFrom("This game requires a 486 or better.").RequiresProtectedMode);
    }

    [Fact]
    public void ToolchainStrings_RuleOutEarlierYears()
    {
        List<YearEvidence> evidence = GameTextEvidence.YearEvidenceFrom(
            "DOS/4GW Professional protected mode runtime",
            "GAME.EXE strings",
            fromExecutable: true);

        YearEvidence hint = Assert.Single(evidence, item => item.Source == "toolchain");
        Assert.Equal(1991, hint.Year);
        Assert.True(hint.IsLowerBound);
    }

    [Fact]
    public void ReadmeHeading_SkipsNoiseLines()
    {
        const string readme = "README\n======\n\nPrince of Persia\n\n(c) 1989 Broderbund\n";

        Assert.Equal("Prince of Persia", GameTextEvidence.ReadmeHeading(readme));
    }

    [Fact]
    public void ReadDiz_ReadsTotalDosCollectionMetadata()
    {
        const string diz =
            "Alpine Tram Ride v1.3 (1989)(Merit Software) [Educational].zip\n" +
            "Name:Alpine Tram Ride\nVersion:1.3\nLanguage/Country:English\nFlags:\nYear:1989\n" +
            "Publisher:Merit Software\nGenre(s):Educational\n";

        DizInfo info = GameTextEvidence.ReadDiz(diz);

        Assert.Equal("Alpine Tram Ride", info.Title);
        Assert.Equal(1989, info.FieldYear);
        Assert.Equal(1989, info.FileNameYear);
        Assert.Equal("Merit Software", info.Publisher);
        Assert.True(info.HasAuthoritativeIdentity);
    }

    [Fact]
    public void ReadDiz_FallsBackToTheGoodToolsFileName()
    {
        DizInfo info = GameTextEvidence.ReadDiz(
            "Great Fighter, The (1995)(SigmaTek Entertainment, Samsung) [Action].zip\n");

        Assert.Equal("Great Fighter, The", info.Title);
        Assert.Null(info.FieldYear);
        Assert.Equal(1995, info.FileNameYear);
        Assert.True(info.HasAuthoritativeIdentity);
    }

    [Fact]
    public void ReadDiz_WithoutNameAndYearIsNotAuthoritative()
    {
        DizInfo info = GameTextEvidence.ReadDiz("THIS IS PIRATED SOFTWARE!\nNo metadata here.\n");

        Assert.Null(info.Title);
        Assert.Null(info.Year);
        Assert.False(info.HasAuthoritativeIdentity);
    }

    [Theory]
    [InlineData("BAD BLOOD '94 FROM ORIGIN SYSTEMS .... [1/1]", 1994)]
    [InlineData("--= JAZZ JACKRABBIT HOLIDAY HARE 1995 =--", 1995)]
    [InlineData("    TWORK Shareware Version v1.7 - 11.08.96", 1996)]
    public void SceneYears_ReadsTheFirstLine(string text, int expected)
    {
        List<YearEvidence> evidence = GameTextEvidence.SceneYears(text + "\n", "file_id.diz");

        Assert.Contains(evidence, item => item.Year == expected);
    }

    [Theory]
    [InlineData("SKUNNY KART v1.3 Multiplayer - High Speed 3D", "SKUNNY KART")]
    [InlineData("ZZT 3.2 from EPIC MEGAGAMES", "ZZT")]
    [InlineData("BAD BLOOD '94 FROM ORIGIN SYSTEMS .... [1/1]", "BAD BLOOD")]
    [InlineData("--= JAZZ JACKRABBIT HOLIDAY HARE 1995 =--", "JAZZ JACKRABBIT HOLIDAY HARE")]
    [InlineData("    TWORK Shareware Version v1.7 - 11.08.96", "TWORK")]
    public void SceneTitle_StripsBannersAndVersions(string line, string expected)
    {
        Assert.Equal(expected, GameTextEvidence.SceneTitle(line + "\n"));
    }

    [Fact]
    public void SceneTitle_RejectsLicenceText()
    {
        Assert.Null(GameTextEvidence.SceneTitle("THIS IS PIRATED SOFTWARE!\n"));
    }
}
