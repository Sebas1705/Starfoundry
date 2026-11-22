using System.Text;

namespace Starfoundry.Ui;

/// <summary>
/// A tiny reader and writer for the INI-like text of the game's own files (<c>progress.cfg</c>, <c>sandbox_presets.cfg</c>): <c>[section]</c> headers,
/// <c>key=value</c> lines, <c>;</c> and <c>#</c> comments, values optionally in double quotes with <c>\"</c>, <c>\\</c> and <c>\n</c> escapes. The
/// same shape as Godot's ConfigFile, but engine-free so parsing is tested. Garbage lines are skipped, never fatal.
/// </summary>
public static class CfgText
{
    public sealed class Section
    {
        public Section(string name) => Name = name;
        public string Name { get; }
        public List<KeyValuePair<string, string>> Pairs { get; } = new();

        public string? Get(string key)
        {
            foreach (var p in Pairs) if (p.Key == key) return p.Value;
            return null;
        }
    }

    public static List<Section> Parse(string? text)
    {
        var sections = new List<Section>();
        Section? current = null;
        foreach (string raw in (text ?? "").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                if (close < 0) continue;
                current = new Section(Unquote(line[1..close].Trim()));
                sections.Add(current);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            if (current is null) { current = new Section(""); sections.Add(current); }
            current.Pairs.Add(new(line[..eq].Trim(), Unquote(line[(eq + 1)..].Trim())));
        }
        return sections;
    }

    /// <summary>Quotes a value so that any text survives a round trip.</summary>
    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    public static string Unquote(string s)
    {
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;
        var sb = new StringBuilder();
        for (int i = 1; i < s.Length - 1; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length - 1)
            {
                i++;
                sb.Append(s[i] == 'n' ? '\n' : s[i]);
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
