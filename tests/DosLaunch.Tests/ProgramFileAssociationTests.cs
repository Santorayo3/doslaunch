namespace DosLaunch.Tests;

public sealed class ProgramFileAssociationTests
{
    [Fact]
    public void BuildCommand_PassesSelectedFileToLauncher()
    {
        string command = ProgramFileAssociation.BuildCommand(@"C:\Program Files\DosLaunch\DosLaunch.exe");

        Assert.Equal(@"""C:\Program Files\DosLaunch\DosLaunch.exe"" --launch ""%1"" %*", command);
    }
}
