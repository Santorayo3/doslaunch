namespace DosLaunch.Tests;

public sealed class GameEraEstimatorTests
{
    [Fact]
    public void Estimate_WithoutEvidenceIsUnknown()
    {
        GameIdentity identity = GameEraEstimator.Estimate("Doom", [], HardwareHints.Empty);

        Assert.Equal(GameEra.Unknown, identity.Era);
        Assert.Null(identity.Year);
        Assert.Equal(GameConfidence.None, identity.Confidence);
        Assert.Equal("unknown", identity.YearText);
    }

    [Fact]
    public void Estimate_TwoAgreeingSourcesGiveHighConfidence()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            "Prince of Persia",
            [
                new YearEvidence(1989, 0.9, false, "wikidata", "Wikidata lists it for DOS (1989)"),
                new YearEvidence(1989, 0.85, false, "archive.org", "archive.org lists it (1989)"),
            ],
            HardwareHints.Empty);

        Assert.Equal(GameEra.UpTo1989, identity.Era);
        Assert.Equal(1989, identity.Year);
        Assert.False(identity.YearIsLowerBound);
        Assert.Equal(GameConfidence.High, identity.Confidence);
    }

    [Fact]
    public void Estimate_CopyrightOnlyGivesALowerBound()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            null,
            [new YearEvidence(1992, 0.6, true, "copyright (text)", "README.TXT: copyright 1992")],
            HardwareHints.Empty);

        Assert.Equal(GameEra.UpTo1992, identity.Era);
        Assert.Equal(1992, identity.Year);
        Assert.True(identity.YearIsLowerBound);
        Assert.Equal("estimated 1992 or later", identity.YearText);
    }

    [Fact]
    public void Estimate_ConflictingSourcesLowerTheConfidence()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            null,
            [
                new YearEvidence(1989, 0.9, false, "wikidata", "1989"),
                new YearEvidence(1990, 0.85, false, "archive.org", "1990"),
            ],
            HardwareHints.Empty);

        Assert.Equal(GameEra.UpTo1989, identity.Era);
        Assert.NotEqual(GameConfidence.High, identity.Confidence);
    }

    [Fact]
    public void Estimate_LowerBoundRemovesOlderEras()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            null,
            [
                new YearEvidence(1988, 0.85, false, "wikipedia", "1988"),
                new YearEvidence(1995, 0.6, true, "copyright (executable)", "copyright 1995"),
            ],
            HardwareHints.Empty);

        Assert.Equal(GameEra.UpTo1996, identity.Era);
        Assert.Equal(1995, identity.Year);
        Assert.True(identity.YearIsLowerBound);
    }

    [Fact]
    public void Estimate_TiePrefersTheNewerEra()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            null,
            [
                new YearEvidence(1989, 0.8, false, "wikipedia", "1989"),
                new YearEvidence(1997, 0.8, false, "archive.org", "1997"),
            ],
            HardwareHints.Empty);

        Assert.Equal(GameEra.Later, identity.Era);
    }

    [Fact]
    public void Estimate_NewerCopyrightDoesNotOverrideAnExplicitYear()
    {
        // Descent: release note and Wikidata say 1995, an installer copyright says 1997.
        GameIdentity identity = GameEraEstimator.Estimate(
            "Descent",
            [
                new YearEvidence(1995, 0.5, false, "release note", "README.TXT: mentions 1995"),
                new YearEvidence(1995, 0.9, false, "wikidata", "Wikidata lists it for DOS (1995)"),
                new YearEvidence(1997, 0.6, true, "copyright (text)", "README.TXT: copyright 1997"),
            ],
            HardwareHints.Empty);

        Assert.Equal(GameEra.UpTo1996, identity.Era);
        Assert.Equal(1995, identity.Year);
        Assert.False(identity.YearIsLowerBound);
    }

    [Fact]
    public void Estimate_KeepsTheLatestCopyrightOfOneSource()
    {
        GameIdentity identity = GameEraEstimator.Estimate(
            null,
            [
                new YearEvidence(1991, 0.6, true, "copyright (executable)", "engine copyright 1991"),
                new YearEvidence(1995, 0.6, true, "copyright (executable)", "game copyright 1995"),
            ],
            HardwareHints.Empty);

        Assert.Equal(1995, identity.Year);
        Assert.Single(identity.Evidence);
    }
}
