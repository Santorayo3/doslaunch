using System.Text;

namespace DosLaunch;

/// <summary>
/// Extracts printable ASCII runs from binaries. Copyright notices, compiler build stamps and toolchain
/// strings survive zipping and copying, unlike the filesystem dates that used to be used.
/// </summary>
internal static class BinaryStrings
{
    private const int MaxStringLength = 400;

    public static List<string> FromFile(string path, long maxBytes, int minLength, int maxStrings)
    {
        var result = new List<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= maxBytes)
            {
                Scan(Read(stream, (int)stream.Length), result, minLength, maxStrings);
            }
            else
            {
                // Packed games keep their strings either in the stub at the start or in the appended data.
                long half = maxBytes / 2;
                Scan(Read(stream, (int)half), result, minLength, maxStrings);
                if (result.Count < maxStrings)
                {
                    stream.Seek(-half, SeekOrigin.End);
                    Scan(Read(stream, (int)half), result, minLength, maxStrings);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read strings from {path}: {ex.Message}");
        }

        return result;
    }

    internal static void Scan(ReadOnlySpan<byte> bytes, List<string> result, int minLength, int maxStrings)
    {
        var builder = new StringBuilder();
        foreach (byte value in bytes)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                if (builder.Length < MaxStringLength)
                {
                    builder.Append((char)value);
                }
            }
            else
            {
                Flush();
            }

            if (result.Count >= maxStrings)
            {
                return;
            }
        }

        Flush();

        void Flush()
        {
            if (builder.Length >= minLength && HasEnoughLetters(builder))
            {
                result.Add(builder.ToString());
            }

            builder.Clear();
        }
    }

    private static bool HasEnoughLetters(StringBuilder text)
    {
        int letters = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsLetter(text[index]) && ++letters >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] Read(Stream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int chunk = stream.Read(buffer, read, count - read);
            if (chunk <= 0)
            {
                break;
            }

            read += chunk;
        }

        return read == count ? buffer : buffer[..read];
    }
}
