using System.Buffers.Binary;

namespace DosLaunch;

internal static class ExecutableInspector
{
    private const int DosHeaderSize = 0x40;
    private const int ELfanewOffset = 0x3C;

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
    /// A 16-bit DOS program is a plain "MZ" image. Windows executables (PE) and 16-bit Windows
    /// programs (NE) carry that signature at e_lfanew and are skipped, while DOS extenders ("LE",
    /// e.g. DOS/4GW games) are DOS programs and therefore handled by DOSBox as well.
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

            bool isWindowsImage =
                (signature[0] == (byte)'P' && signature[1] == (byte)'E') || // 32/64-bit Windows
                (signature[0] == (byte)'N' && signature[1] == (byte)'E');   // 16-bit Windows

            return !isWindowsImage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
