using System.Globalization;

namespace DriverSFTrafficEditor.Core;

// Imports literal keys from vehicleStats = { [62] = { ... }, ... }.
// This is a text reader, never a Lua interpreter. Nested keys, comments and
// strings cannot become IDs; computed keys are refused rather than guessed.
public static class VehicleStatsInput
{
    private readonly record struct Token(string Text, bool Word = false, bool Number = false);

    public static uint[] Parse(string source)
    {
        var tokens = Lex(source);
        var roots = Enumerable.Range(0, Math.Max(0, tokens.Count - 2)).Where(i =>
            tokens[i].Word && tokens[i].Text == "vehicleStats" && tokens[i+1].Text == "=" && tokens[i+2].Text == "{").ToArray();
        if (roots.Length != 1) throw new FormatException("Expected one literal vehicleStats = { ... } table. Computed tables cannot be imported.");
        int start = roots[0] + 2, depth = 1, end = -1;
        var ids = new HashSet<uint>();
        for (int i = start + 1; i < tokens.Count; i++)
        {
            string text = tokens[i].Text;
            if (depth == 1 && text == "[")
            {
                if (i > start + 1 && tokens[i-1].Text is not "," and not ";")
                    throw new FormatException("A vehicle ID key must be a direct record in the literal vehicleStats table.");
                if (i + 4 >= tokens.Count || !tokens[i+1].Number || tokens[i+2].Text != "]" ||
                    tokens[i+3].Text != "=" || tokens[i+4].Text != "{")
                    throw new FormatException("Each vehicleStats row must have a literal numeric key, for example [62] = { ... }.");
                string number = tokens[i+1].Text;
                bool hex = number.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                if (!uint.TryParse(hex ? number[2..] : number, hex ? NumberStyles.HexNumber : NumberStyles.None,
                        CultureInfo.InvariantCulture, out uint id) || id is 0 or > ushort.MaxValue)
                    throw new FormatException($"Vehicle ID '{number}' must be an integer from 1 through 65535.");
                ids.Add(id);
            }
            if (text == "{") depth++;
            if (text == "}" && --depth == 0) { end = i; break; }
        }
        if (end < 0) throw new FormatException("The vehicleStats table is not closed.");
        if (ids.Count == 0) throw new FormatException("No literal vehicle IDs were found in vehicleStats.");
        // Additional assignments may add/remove/replace IDs. Do not silently
        // claim that the literal root is the complete inventory in that case.
        for (int i = end + 1; i < tokens.Count; i++)
            if (tokens[i].Word && tokens[i].Text == "vehicleStats" &&
                !(tokens[i-1].Text == "return" && tokens.Skip(i+1).All(t => t.Text == ";")))
                throw new FormatException("Additional vehicleStats assignments/references follow the literal table. Import an expanded literal table or enter the numeric IDs manually.");
        return ids.ToArray();
    }

    private static List<Token> Lex(string text)
    {
        var tokens = new List<Token>(); int at = 0;
        bool LongOpen(int p, out int width, out string close)
        {
            width = 0; close = "";
            if (p >= text.Length || text[p] != '[') return false;
            int next = p+1; while (next < text.Length && text[next] == '=') next++;
            if (next >= text.Length || text[next] != '[') return false;
            width = next-p+1; close = "]" + new string('=', next-p-1) + "]"; return true;
        }
        void SkipLong(int p, int width, string close)
        {
            int finish = text.IndexOf(close, p+width, StringComparison.Ordinal);
            if (finish < 0) throw new FormatException("Unterminated Lua long string/comment.");
            at = finish+close.Length;
        }
        while (at < text.Length)
        {
            char ch = text[at];
            if (char.IsWhiteSpace(ch) || ch == '\uFEFF') { at++; continue; }
            if (ch == '-' && at+1 < text.Length && text[at+1] == '-')
            {
                if (LongOpen(at+2, out int width, out string close)) SkipLong(at+2, width, close);
                else { at += 2; while (at < text.Length && text[at] is not '\n' and not '\r') at++; }
                continue;
            }
            if (ch is '\'' or '"')
            {
                char quote = ch; at++; bool closed = false;
                while (at < text.Length)
                {
                    char c = text[at++];
                    if (c == '\\') { if (at < text.Length) at++; continue; }
                    if (c == quote) { closed = true; break; }
                }
                if (!closed) throw new FormatException("Unterminated Lua quoted string.");
                tokens.Add(new Token("<string>")); continue;
            }
            if (LongOpen(at, out int longWidth, out string longClose))
            { SkipLong(at, longWidth, longClose); tokens.Add(new Token("<string>")); continue; }
            if (char.IsLetter(ch) || ch == '_')
            {
                int start = at++; while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
                tokens.Add(new Token(text[start..at], Word: true)); continue;
            }
            if (char.IsDigit(ch))
            {
                int start = at++; while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '.')) at++;
                tokens.Add(new Token(text[start..at], Number: true)); continue;
            }
            tokens.Add(new Token(ch.ToString())); at++;
        }
        return tokens;
    }
}
