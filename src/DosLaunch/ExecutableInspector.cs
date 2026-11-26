using System.Buffers.Binary;

namespace DosLaunch;

internal static class ExecutableInspector
{
    private const int DosHeaderSize = 0x40;
    private const int ELfanewOffset = 0x3C;

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
