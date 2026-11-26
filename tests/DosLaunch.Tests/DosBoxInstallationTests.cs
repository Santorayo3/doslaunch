namespace DosLaunch.Tests;

public sealed class DosBoxInstallationTests
{
    [Theory]
    [InlineData(@"C:\DOSBox-X\dosbox-x.exe", DosBoxFlavor.DosBoxX)]
    [InlineData(@"C:\DOSBox-X\dosbox-x-sdl2.exe", DosBoxFlavor.DosBoxX)]
    [InlineData(@"C:\DOSBox\DOSBox.exe", DosBoxFlavor.DosBox)]
    [InlineData(@"C:\DOSBox\svn\dosbox.exe", DosBoxFlavor.DosBox)]
    [InlineData(@"C:\DOSBox\svn_daun\dosbox_x64.exe", DosBoxFlavor.DosBox)]
    public void DetectFlavor_TellsTheBuildsApart(string path, DosBoxFlavor expected)
    {
        Assert.Equal(expected, DosBoxInstallation.DetectFlavor(path));
    }
}
