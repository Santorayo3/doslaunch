using System.Buffers.Binary;

namespace DosLaunch.Tests;

public sealed class ExecutableInspectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public ExecutableInspectorTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void MzImageWithoutWindowsHeader_IsDosExecutable()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0), ".exe");

        Assert.True(ExecutableInspector.IsDosExecutable(path));
    }

    [Fact]
    public void MzImageWithLfanewBeyondEndOfFile_IsDosExecutable()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0x4000), ".exe");

        Assert.True(ExecutableInspector.IsDosExecutable(path));
    }

    [Theory]
    [InlineData("PE")]
    [InlineData("NE")]
    public void MzImageWithWindowsSignature_IsNotDosExecutable(string signature)
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0x40, signature), ".exe");

        Assert.False(ExecutableInspector.IsDosExecutable(path));
    }

    [Fact]
    public void DosExtenderExecutable_IsDosExecutable()
    {
        // DOS/4GW style games carry an "LE" signature but are still DOS programs.
        string path = WriteFile(CreateMzImage(eLfanew: 0x40, "LE"), ".exe");

        Assert.True(ExecutableInspector.IsDosExecutable(path));
    }

    [Fact]
    public void NonMzFile_IsNotDosExecutable()
    {
        string path = WriteFile("plain text, definitely not a program"u8.ToArray(), ".exe");

        Assert.False(ExecutableInspector.IsDosExecutable(path));
    }

    [Fact]
    public void RealWindowsExecutable_IsNotDosExecutable()
    {
        string path = Path.Combine(Environment.SystemDirectory, "notepad.exe");

        Assert.True(File.Exists(path));
        Assert.False(ExecutableInspector.IsDosExecutable(path));
    }

    [Fact]
    public void MissingFile_IsNotDosExecutable()
    {
        Assert.False(ExecutableInspector.IsDosExecutable(Path.Combine(_directory, "missing.exe")));
    }

    [Fact]
    public void ComFile_IsDosProgram()
    {
        // COM programs have no header at all, so the extension alone marks them as DOS.
        string path = WriteFile([0xE9, 0x00, 0x00], ".com");

        Assert.True(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void RenamedWindowsExecutableWithComExtension_IsNotDosProgram()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0x40, "PE"), ".com");

        Assert.False(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void RenamedDosExecutableWithComExtension_IsDosProgram()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0), ".com");

        Assert.True(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void WindowsExecutable_IsNotDosProgram()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0x40, "PE"), ".exe");

        Assert.False(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void DosExecutable_IsDosProgram()
    {
        string path = WriteFile(CreateMzImage(eLfanew: 0), ".exe");

        Assert.True(ExecutableInspector.IsDosProgram(path));
    }

    private static byte[] CreateMzImage(int eLfanew, string? signature = null)
    {
        byte[] header = new byte[0x40];
        header[0] = (byte)'M';
        header[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x3C), eLfanew);

        byte[] image = new byte[0x200];
        header.CopyTo(image, 0);

        if (signature is not null)
        {
            image[eLfanew] = (byte)signature[0];
            image[eLfanew + 1] = (byte)signature[1];
        }

        return image;
    }

    private string WriteFile(byte[] content, string extension)
    {
        string path = Path.Combine(_directory, $"sample{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }
}
