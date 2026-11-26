using System.Buffers.Binary;

namespace DosLaunch.Tests;

public sealed class BatchFileInspectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DosLaunch.Tests", Guid.NewGuid().ToString("N"));

    public BatchFileInspectorTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void BatchThatStartsADosExecutable_StartsDosProgram()
    {
        WriteDosExecutable("DOOM.EXE");
        string batch = WriteBatch("START.BAT", "@echo off\r\nDOOM.EXE -warp");

        Assert.True(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatStartsAWindowsExecutable_DoesNotStartDosProgram()
    {
        WriteWindowsExecutable("TOOL.EXE");
        string batch = WriteBatch("build.bat", "TOOL.EXE /quiet");

        Assert.False(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatStartsAComProgram_StartsDosProgram()
    {
        // COM programs are raw code, so there is no header to recognise them by.
        WriteBinary("GAME.COM", [0xE9, 0x00, 0x00]);
        string batch = WriteBatch("START.BAT", "@echo off\r\nchcp 850 >nul\r\nGAME.COM");

        Assert.True(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatStartsAProgramWithoutExtension_StartsDosProgram()
    {
        WriteDosExecutable("KEEN.EXE");
        string batch = WriteBatch("START.BAT", "KEEN");

        Assert.True(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatChangesIntoASubfolder_StartsDosProgram()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "game"));
        WriteDosExecutable(@"game\GAME.EXE");
        string batch = WriteBatch("START.BAT", "@echo off\r\ncd game\r\nGAME.EXE");

        Assert.True(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatOnlyMentionsTheProgramInComments_DoesNotStartDosProgram()
    {
        WriteDosExecutable("DOOM.EXE");
        string batch = WriteBatch("build.bat", "rem start DOOM.EXE later\r\n:: DOOM.EXE\r\necho DOOM.EXE");

        Assert.False(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void BatchThatStartsAMissingProgram_DoesNotStartDosProgram()
    {
        string batch = WriteBatch("START.BAT", "MISSING.EXE");

        Assert.False(BatchFileInspector.StartsDosProgram(batch));
    }

    [Fact]
    public void MissingBatchFile_DoesNotStartDosProgram()
    {
        Assert.False(BatchFileInspector.StartsDosProgram(Path.Combine(_directory, "missing.bat")));
    }

    private void WriteDosExecutable(string name)
    {
        // A pure DOS program is an "MZ" image without a Windows signature at e_lfanew.
        byte[] image = new byte[0x200];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        WriteBinary(name, image);
    }

    private void WriteWindowsExecutable(string name)
    {
        byte[] image = new byte[0x200];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 0x40);
        image[0x40] = (byte)'P';
        image[0x41] = (byte)'E';
        WriteBinary(name, image);
    }

    private void WriteBinary(string name, byte[] content) =>
        File.WriteAllBytes(Path.Combine(_directory, name), content);

    private string WriteBatch(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
