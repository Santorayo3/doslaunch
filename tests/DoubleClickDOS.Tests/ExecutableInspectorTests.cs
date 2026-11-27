using System.Buffers.Binary;
using System.Text;

namespace DoubleClickDOS.Tests;

public sealed class ExecutableInspectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DoubleClickDOS.Tests", Guid.NewGuid().ToString("N"));

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
    public void ProtectedModeDosImage_IsDosProgram()
    {
        // Borland's 16-bit protected-mode DOS programs are linked as NE images and import the
        // runtime manager they are shipped with (RTM.EXE), just like JD.EXE of "Judge Dredd".
        string path = WriteFile(CreateNeImage(["KERNEL", "KERNEL", "RTM"]), ".exe");

        Assert.True(ExecutableInspector.IsDosExecutable(path));
        Assert.True(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void ProtectedModeDosImageWithoutRuntimeManagerImport_IsDosProgram()
    {
        string path = WriteFile(CreateNeImage(["KERNEL"], stubText: "This PM STUB loads DPMI16BI.OVL"), ".exe");

        Assert.True(ExecutableInspector.IsDosProgram(path));
    }

    [Fact]
    public void WindowsNeImage_IsNotDosProgram()
    {
        string path = WriteFile(CreateNeImage(["KERNEL", "USER", "GDI"]), ".exe");

        Assert.False(ExecutableInspector.IsDosExecutable(path));
        Assert.False(ExecutableInspector.IsDosProgram(path));
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

    /// <summary>
    /// Builds a minimal 16-bit image whose head signature is "NE" and whose import table names the
    /// given modules. The NE header starts after a stub area, so <paramref name="stubText"/> lands in
    /// the DOS stub instead of the header.
    /// </summary>
    private static byte[] CreateNeImage(string[] modules, string? stubText = null)
    {
        const int neOffset = 0x100;
        const int moduleTable = 0x40; // relative to the NE header
        int importNames = moduleTable + modules.Length * 2;

        byte[] image = new byte[0x800];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), neOffset);

        Span<byte> ne = image.AsSpan(neOffset);
        ne[0] = (byte)'N';
        ne[1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(ne[0x1C..], 1); // segment count
        BinaryPrimitives.WriteUInt16LittleEndian(ne[0x1E..], (ushort)modules.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(ne[0x28..], moduleTable);
        BinaryPrimitives.WriteUInt16LittleEndian(ne[0x2A..], (ushort)importNames);
        ne[0x36] = 1; // target operating system: Windows

        // The imported names table has an unused zero entry at offset 0.
        int nameOffset = 1;
        for (int i = 0; i < modules.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(ne[(moduleTable + i * 2)..], (ushort)nameOffset);
            ne[importNames + nameOffset] = (byte)modules[i].Length;
            Encoding.ASCII.GetBytes(modules[i], ne[(importNames + nameOffset + 1)..]);
            nameOffset += 1 + modules[i].Length;
        }

        if (stubText is not null)
        {
            Encoding.ASCII.GetBytes(stubText, image.AsSpan(0x40));
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
