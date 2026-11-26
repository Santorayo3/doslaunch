namespace DosLaunch.Tests;

public sealed class ExeFileAssociationTests
{
    [Fact]
    public void BuildCommand_PassesSelectedFileToLauncher()
    {
        string command = ExeFileAssociation.BuildCommand(@"C:\Program Files\DosLaunch\DosLaunch.exe");

        Assert.Equal(@"""C:\Program Files\DosLaunch\DosLaunch.exe"" --launch ""%1"" %*", command);
    }
}
