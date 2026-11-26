namespace DosLaunch.Tests;

public sealed class LocalGameEvidenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public LocalGameEvidenceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Collect_UsesTheFileIdDizMetadata()
    {
        File.WriteAllText(
            Path.Combine(_folder, "file_id.diz"),
            "Alpine Tram Ride v1.3 (1989)(Merit Software) [Educational].zip\n" +
            "Name:Alpine Tram Ride\nVersion:1.3\nYear:1989\nPublisher:Merit Software\n");
        string executable = AddFile("LTI.EXE");

        LocalEvidence evidence = LocalGameEvidence.Collect(executable);

        Assert.True(evidence.HasAuthoritativeIdentity);
        Assert.Equal("Alpine Tram Ride", evidence.Titles[0].Title);
        Assert.Contains(evidence.Years, item => item is { Year: 1989, IsLowerBound: false, Source: "file_id.diz fields" });
        Assert.Contains(evidence.Years, item => item is { Year: 1989, Source: "file_id.diz name" });

        GameIdentity identity = GameEraEstimator.Estimate(evidence.Titles[0].Title, evidence.Years, evidence.Hints);

        Assert.Equal(1989, identity.Year);
        Assert.Equal(GameEra.UpTo1989, identity.Era);
        Assert.Equal(GameConfidence.High, identity.Confidence);
    }

    [Fact]
    public void Collect_AcceptsAValidatedSceneTitle()
    {
        File.WriteAllText(Path.Combine(_folder, "file_id.diz"), "SKUNNY KART v1.3 Multiplayer - High Speed 3D\n");
        string executable = AddFile("SKUNKART.EXE");

        LocalEvidence evidence = LocalGameEvidence.Collect(executable);

        Assert.False(evidence.HasAuthoritativeIdentity);
        Assert.Equal("SKUNNY KART", evidence.Titles[0].Title);
    }

    [Fact]
    public void Collect_IgnoresUnrelatedBannerText()
    {
        File.WriteAllText(Path.Combine(_folder, "file_id.diz"), "THIS IS PIRATED SOFTWARE!\nAll copying is illegal.\n");
        string executable = AddFile("BASH123.EXE");

        LocalEvidence evidence = LocalGameEvidence.Collect(executable);

        Assert.DoesNotContain(evidence.Titles, item => item.Title.Contains("PIRATED", StringComparison.OrdinalIgnoreCase));
    }

    private string AddFile(string fileName)
    {
        string path = Path.Combine(_folder, fileName);
        File.WriteAllBytes(path, [0x4D, 0x5A, 0, 0]);
        return path;
    }
}
