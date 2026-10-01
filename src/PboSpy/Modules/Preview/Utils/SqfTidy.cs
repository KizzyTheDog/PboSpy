using System.Text;

namespace PboSpy.Modules.Preview.Utils;

/// <summary>Re-indents a script by its brackets and drops the blank-line padding obfuscators add. Strings, comments and #lines are left alone.</summary>
internal static class SqfTidy
{
    public static string Tidy(string text)
    {
        var result = new StringBuilder();
        var depth = 0;
        var inBlockComment = false;
        var blank = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                // One blank line between blocks is kept, runs of them are not.
                if (++blank == 1 && result.Length > 0)
                {
                    result.Append('\n');
                }
                continue;
            }
            blank = 0;
            if (line.StartsWith('#') && !inBlockComment)
            {
                result.Append(line).Append('\n');
                continue;
            }
            var leadingClosers = line.TakeWhile(c => c is '}' or ']' or ')').Count();
            result.Append(new string(' ', Math.Max(0, depth - leadingClosers) * 4)).Append(line).Append('\n');
            depth = Math.Max(0, depth + Balance(line, ref inBlockComment));
        }
        return result.ToString().TrimEnd('\n') + "\n";
    }

    // Opening minus closing brackets outside strings and comments.
    private static int Balance(string line, ref bool inBlockComment)
    {
        var balance = 0;
        char quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (quote != '\0')
            {
                // SQF escapes a quote by doubling it, which this toggles out of and straight back into.
                if (c == quote)
                {
                    quote = '\0';
                }
                continue;
            }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '{' or '[' or '(':
                    balance++;
                    break;
                case '}' or ']' or ')':
                    balance--;
                    break;
            }
        }
        return balance;
    }
}
