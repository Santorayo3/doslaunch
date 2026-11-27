using System.Text;

namespace DoubleClickDOS.Tests;

public sealed class ProgramFileAssociationTests
{
    [Fact]
    public void BuildCommand_PassesSelectedFileToLauncher()
    {
        string command = ProgramFileAssociation.BuildCommand(@"C:\Program Files\DoubleClickDOS\DoubleClickDOS.exe");

        Assert.Equal(@"""C:\Program Files\DoubleClickDOS\DoubleClickDOS.exe"" --launch ""%1"" %*", command);
    }

    [Fact]
    public void RestoreScript_DeletesEveryInterceptedCommandKey()
    {
        byte[] script = File.ReadAllBytes(FindRepositoryFile("Restore-File-Associations.reg"));

        // Regedit only recognises a registry script when the header is the very first thing in the file.
        Assert.Equal((byte)'W', script[0]);

        string text = Encoding.ASCII.GetString(script);
        foreach (string programClass in ProgramFileAssociation.ProgramClasses)
        {
            Assert.Contains(
                $@"[-HKEY_CURRENT_USER\Software\Classes\{programClass}\shell\open\command]",
                text,
                StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{fileName} was not found in any parent folder of {AppContext.BaseDirectory}.");
    }
}
