namespace DoubleClickDOS.Tests;

public sealed class ProgramFileAssociationTests
{
    [Fact]
    public void BuildCommand_PassesSelectedFileToLauncher()
    {
        string command = ProgramFileAssociation.BuildCommand(@"C:\Program Files\DoubleClickDOS\DoubleClickDOS.exe");

        Assert.Equal(@"""C:\Program Files\DoubleClickDOS\DoubleClickDOS.exe"" --launch ""%1"" %*", command);
    }
}
