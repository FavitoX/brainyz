// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Brainyz.Core.Storage;

/// <summary>
/// Splits a SQL script into individual statements, aware of:
/// <list type="bullet">
///   <item>line comments <c>-- ...</c></item>
///   <item>block comments <c>/* ... */</c></item>
///   <item>string literals with <c>''</c> escape sequences</item>
///   <item>nestable <c>BEGIN … END</c> blocks (triggers) — inner <c>;</c> does NOT split</item>
/// </list>
/// </summary>
/// <remarks>
/// Required because Nelknet.LibSQL.Data 0.2.4 does not support multiple statements
/// in a single <c>CommandText</c>.
/// </remarks>
public static class SqlSplitter
{
    public static IEnumerable<string> Split(string sql)
    {
        var sb = new StringBuilder();
        int depth = 0;
        int i = 0;

        while (i < sql.Length)
        {
            char c = sql[i];

            // line comment
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(sql[i]); i++; }
                continue;
            }

            // block comment
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                sb.Append("/*"); i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    sb.Append(sql[i]); i++;
                }
                if (i + 1 < sql.Length) { sb.Append("*/"); i += 2; }
                continue;
            }

            // string literal with '' escape
            if (c == '\'')
            {
                sb.Append(c); i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        sb.Append('\''); i++;
                        if (i < sql.Length && sql[i] == '\'') { sb.Append('\''); i++; continue; }
                        break;
                    }
                    sb.Append(sql[i]); i++;
                }
                continue;
            }

            // whole-word match for BEGIN / END
            if (IsIdentStart(c))
            {
                int start = i;
                while (i < sql.Length && IsIdentPart(sql[i])) i++;
                var word = sql.Substring(start, i - start);
                sb.Append(word);
                if (word.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)) depth++;
                else if (word.Equals("END", StringComparison.OrdinalIgnoreCase) && depth > 0) depth--;
                continue;
            }

            if (c == ';' && depth == 0)
            {
                sb.Append(';');
                var stmt = sb.ToString().Trim();
                if (!IsEmptyOrOnlyComments(stmt)) yield return stmt;
                sb.Clear();
                i++;
                continue;
            }

            sb.Append(c); i++;
        }

        var last = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last) && !IsEmptyOrOnlyComments(last)) yield return last;
    }

    private static bool IsIdentStart(char ch) => char.IsLetter(ch) || ch == '_';
    private static bool IsIdentPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsEmptyOrOnlyComments(string s)
    {
        foreach (var line in s.Split('\n'))
        {
            var t = line.TrimStart();
            if (t.Length == 0) continue;
            if (t.StartsWith("--")) continue;
            if (t == ";") continue;
            return false;
        }
        return true;
    }
}
