using System.Text;

namespace DoubleClickDOS;

/// <summary>
/// Tells whether a batch file starts a DOS program. Batch files are used by DOS games as well as by
/// Windows tooling, so the commands are read and every program they reference is checked.
/// </summary>
internal static class BatchFileInspector
{
    private const int MaxBytes = 128 * 1024;
    private const int MaxCheckedPrograms = 32;

    /// <summary>True when the batch file runs a DOS program and therefore needs DOSBox.</summary>
    public static bool StartsDosProgram(string batchPath)
    {
        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(batchPath));
            if (folder is null)
            {
                return false;
            }

            var scan = new Scan(folder);
            foreach (string line in ReadText(batchPath).Split(['\r', '\n']))
            {
                if (scan.ProcessLine(line))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] buffer = new byte[(int)Math.Min(stream.Length, MaxBytes)];
        stream.ReadExactly(buffer);
        return Encoding.Latin1.GetString(buffer);
    }

    /// <summary>Walks the commands of one batch file while keeping track of the folder they run in.</summary>
    private sealed class Scan
    {
        private readonly string _batchFolder;
        private string _currentFolder;
        private int _checkedPrograms;

        public Scan(string batchFolder)
        {
            _batchFolder = batchFolder;
            _currentFolder = batchFolder;
        }

        public bool ProcessLine(string rawLine)
        {
            string line = rawLine.Trim().TrimStart('@').TrimStart();
            if (line.Length == 0)
            {
                return false;
            }

            List<string> tokens = Tokenize(line);
            if (tokens.Count == 0)
            {
                return false;
            }

            string command = tokens[0].ToLowerInvariant();
            if (command.StartsWith("::", StringComparison.Ordinal) || command is "rem" or "echo" or "title")
            {
                return false;
            }

            if (TryChangeFolder(command, tokens))
            {
                return false;
            }

            foreach (string token in tokens)
            {
                if (IsDosProgram(token))
                {
                    return true;
                }
            }

            return false;
        }

        private string[] ProgramNames(string token)
        {
            string name = token;
            int cut = name.IndexOfAny(['<', '>', '|']);
            if (cut >= 0)
            {
                name = name[..cut];
            }

            name = name.Trim();
            if (name.Length == 0 || name[0] is '/' or '-' or ':' or '=')
            {
                return [];
            }

            // Variables, wildcards and switches never name a single existing program.
            if (name.IndexOfAny(['%', '*', '?']) >= 0)
            {
                return [];
            }

            string extension = Path.GetExtension(name);
            if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".com", StringComparison.OrdinalIgnoreCase))
            {
                return [name];
            }

            // DOS appends the extension when a command is typed without one.
            return extension.Length == 0 ? [name + ".exe", name + ".com"] : [];
        }

        private bool IsDosProgram(string token)
        {
            if (_checkedPrograms >= MaxCheckedPrograms)
            {
                return false;
            }

            foreach (string name in ProgramNames(token))
            {
                if (Resolve(name) is not { } path)
                {
                    continue;
                }

                _checkedPrograms++;
                if (ExecutableInspector.IsDosProgram(path))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryChangeFolder(string command, List<string> tokens)
        {
            if (command is "cd" or "chdir")
            {
                if (tokens.Count > 1)
                {
                    SetFolder(tokens[1]);
                }

                return true;
            }

            // "cd.." and "cd\games" work without a separating space.
            if (command.Length > 2 && command.StartsWith("cd", StringComparison.Ordinal) && command[2] is '.' or '\\' or '/')
            {
                SetFolder(tokens[0][2..]);
                return true;
            }

            return false;
        }

        private void SetFolder(string target)
        {
            string? path = FullPath(target);
            if (path is not null)
            {
                _currentFolder = path;
            }
        }

        private string? Resolve(string name)
        {
            string path = MakeRooted(name.Replace('/', '\\'));

            if (Path.IsPathRooted(path))
            {
                return File.Exists(path) ? path : null;
            }

            string inCurrentFolder = Path.Combine(_currentFolder, path);
            if (File.Exists(inCurrentFolder))
            {
                return inCurrentFolder;
            }

            // A "cd" that DOSBox resolves differently than the host still leaves the file next to the batch.
            string inBatchFolder = Path.Combine(_batchFolder, path);
            return File.Exists(inBatchFolder) ? inBatchFolder : null;
        }

        private string? FullPath(string target)
        {
            string path = target.Replace('/', '\\').Trim();
            if (path.Length == 0)
            {
                return null;
            }

            path = MakeRooted(path);
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(_currentFolder, path);
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>Turns "\GAMES" into "C:\GAMES", the root of the drive the batch lives on.</summary>
        private string MakeRooted(string path) =>
            path.StartsWith('\\') && !path.StartsWith("\\\\", StringComparison.Ordinal)
                ? (Path.GetPathRoot(_batchFolder) ?? string.Empty) + path.TrimStart('\\')
                : path;

        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            int index = 0;

            while (index < line.Length)
            {
                if (char.IsWhiteSpace(line[index]))
                {
                    index++;
                    continue;
                }

                if (line[index] == '"')
                {
                    int closing = line.IndexOf('"', index + 1);
                    tokens.Add(closing < 0 ? line[(index + 1)..] : line[(index + 1)..closing]);
                    index = closing < 0 ? line.Length : closing + 1;
                    continue;
                }

                int start = index;
                while (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] != '"')
                {
                    index++;
                }

                tokens.Add(line[start..index]);
            }

            return tokens;
        }
    }
}
