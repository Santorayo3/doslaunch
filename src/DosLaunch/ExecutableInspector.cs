using System.Buffers.Binary;
using System.Text;

namespace DosLaunch;

internal static class ExecutableInspector
{
    private const int DosHeaderSize = 0x40;
    private const int ELfanewOffset = 0x3C;

    private const int NeHeaderSize = 0x40;
    private const int NeModuleCountOffset = 0x1E;
    private const int NeModuleTableOffset = 0x28;
    private const int NeImportNamesOffset = 0x2A;
    private const int MaxNeModules = 64;
    private const int MaxNeModuleNameLength = 32;
    private const int MaxStubSize = 64 * 1024;

    private static ReadOnlySpan<byte> DosRuntimeManagerModule => "RTM"u8;
    private static ReadOnlySpan<byte> ProtectedModeStubMarker => "PM STUB"u8;

    /// <summary>
    /// Whether the file has to run inside DOSBox: a 16-bit DOS executable, a COM program (raw code
    /// without any header) or a batch file that starts one of those.
    /// </summary>
    public static bool IsDosProgram(string path)
    {
        string extension = Path.GetExtension(path);

        if (extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
        {
            // COM programs are raw code, so only a renamed EXE image can be recognised by its header.
            bool? hasDosHeader = StartsWithMzSignature(path);
            return hasDosHeader is not true || IsDosExecutable(path);
        }

        if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return BatchFileInspector.StartsDosProgram(path);
        }

        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && IsDosExecutable(path);
    }

    /// <summary><c>null</c> when the file could not be read, so the extension decides.</summary>
    private static bool? StartsWithMzSignature(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[2];
            if (stream.Read(signature) < 2)
            {
                return false;
            }

            return signature[0] == (byte)'M' && signature[1] == (byte)'Z';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// A 16-bit DOS program is a plain "MZ" image. Windows executables (PE) are skipped, 16-bit
    /// Windows programs (NE) are skipped as well unless they are protected-mode DOS programs, and
    /// DOS extenders ("LE", e.g. DOS/4GW games) are DOS programs and therefore handled by DOSBox too.
    /// </summary>
    public static bool IsDosExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            Span<byte> header = stackalloc byte[DosHeaderSize];
            stream.ReadExactly(header);

            if (header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return false;
            }

            int eLfanew = BinaryPrimitives.ReadInt32LittleEndian(header[ELfanewOffset..]);
            if (eLfanew <= 0 || eLfanew > stream.Length - 2)
            {
                return true;
            }

            stream.Position = eLfanew;
            Span<byte> signature = stackalloc byte[2];
            stream.ReadExactly(signature);

            if (signature[0] == (byte)'P' && signature[1] == (byte)'E')
            {
                return false; // 32/64-bit Windows
            }

            if (signature[0] == (byte)'N' && signature[1] == (byte)'E')
            {
                return IsProtectedModeDosImage(stream, eLfanew); // 16-bit Windows, or DOS in disguise
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Borland's 16-bit protected-mode DOS programs are linked into "NE" images just like 16-bit
    /// Windows programs, so the head signature alone cannot tell them apart. They are recognised by
    /// the runtime manager ("RTM") they import and by their "PM STUB" DOS stub, which loads the DOS
    /// extenders DPMI16BI.OVL and RTM.EXE.
    /// </summary>
    private static bool IsProtectedModeDosImage(Stream stream, int neOffset)
    {
        if (neOffset < 0 || neOffset + NeHeaderSize > stream.Length)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[NeHeaderSize];
        stream.Position = neOffset;
        stream.ReadExactly(header);

        return ImportsDosRuntimeManager(stream, neOffset, header) ||
               HasProtectedModeStub(stream, neOffset);
    }

    private static bool ImportsDosRuntimeManager(Stream stream, int neOffset, ReadOnlySpan<byte> neHeader)
    {
        ushort moduleCount = BinaryPrimitives.ReadUInt16LittleEndian(neHeader[NeModuleCountOffset..]);
        ushort moduleTable = BinaryPrimitives.ReadUInt16LittleEndian(neHeader[NeModuleTableOffset..]);
        ushort importNames = BinaryPrimitives.ReadUInt16LittleEndian(neHeader[NeImportNamesOffset..]);

        // Only a plausible import table can name the runtime manager; anything else stays Windows.
        if (moduleCount is 0 or > MaxNeModules || moduleTable == 0 || importNames == 0)
        {
            return false;
        }

        long moduleRefsOffset = neOffset + moduleTable;
        long importNamesOffset = neOffset + importNames;
        if (moduleRefsOffset + moduleCount * 2 > stream.Length || importNamesOffset >= stream.Length)
        {
            return false;
        }

        Span<byte> moduleRefs = stackalloc byte[MaxNeModules * 2];
        moduleRefs = moduleRefs[..(moduleCount * 2)];
        stream.Position = moduleRefsOffset;
        stream.ReadExactly(moduleRefs);

        Span<byte> name = stackalloc byte[MaxNeModuleNameLength];
        for (int i = 0; i < moduleRefs.Length; i += 2)
        {
            long nameOffset = importNamesOffset + BinaryPrimitives.ReadUInt16LittleEndian(moduleRefs[i..]);
            if (nameOffset >= stream.Length - 1)
            {
                continue;
            }

            stream.Position = nameOffset;
            int length = stream.ReadByte();
            if (length is <= 0 or > MaxNeModuleNameLength || nameOffset + 1 + length > stream.Length)
            {
                continue;
            }

            Span<byte> moduleName = name[..length];
            stream.ReadExactly(moduleName);
            if (Ascii.EqualsIgnoreCase(moduleName, DosRuntimeManagerModule))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The DOS stub of those programs announces itself with "PM STUB".</summary>
    private static bool HasProtectedModeStub(Stream stream, int neOffset)
    {
        int stubLength = (int)Math.Min(neOffset - DosHeaderSize, MaxStubSize);
        if (stubLength <= 0)
        {
            return false;
        }

        byte[] stub = new byte[stubLength];
        stream.Position = DosHeaderSize;
        stream.ReadExactly(stub);

        return stub.AsSpan().IndexOf(ProtectedModeStubMarker) >= 0;
    }
}
