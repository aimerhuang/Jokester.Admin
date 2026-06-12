namespace jokester.admin.Configuration;

public static class DotEnvConfiguration
{
    public static void LoadToEnvironment(params string[] searchDirectories)
    {
        foreach (var directory in ExpandSearchDirectories(searchDirectories))
        {
            var path = Path.Combine(directory, ".env");
            if (File.Exists(path))
            {
                LoadFile(path);
            }
        }
    }

    private static void LoadFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = Unquote(line[(separatorIndex + 1)..].Trim());
            if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static IEnumerable<string> ExpandSearchDirectories(IEnumerable<string> searchDirectories)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(candidate);
            }
            catch
            {
                continue;
            }

            while (directory is not null)
            {
                var fullName = directory.FullName;
                if (visited.Add(fullName))
                {
                    yield return fullName;
                }

                directory = directory.Parent;
            }
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
