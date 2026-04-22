// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

#:package Nelknet.LibSQL.Data@0.2.4

// Runtime verification of schema.sql against LibSQL.
// Usage (from the repository root):  dotnet run tools/schema-check.cs

using System.Data;
using System.Globalization;
using System.Text;
using Nelknet.LibSQL.Data;

var repoRoot = Directory.GetCurrentDirectory();
var schemaPath = Path.Combine(repoRoot, "schema.sql");
var dbPath = Path.Combine(repoRoot, "tools", "schema-check.tmp.db");

if (!File.Exists(schemaPath))
{
    Console.Error.WriteLine($"schema.sql not found at {schemaPath}");
    Console.Error.WriteLine("Run this script from the repository root.");
    return 2;
}

foreach (var f in new[] { dbPath, dbPath + "-journal", dbPath + "-wal", dbPath + "-shm" })
    if (File.Exists(f)) File.Delete(f);

Console.WriteLine($"schema : {schemaPath}");
Console.WriteLine($"db     : {dbPath}\n");

var schemaText = await File.ReadAllTextAsync(schemaPath);
var statements = SplitStatements(schemaText).ToList();
Console.WriteLine($"statements to execute: {statements.Count}\n");

await using var conn = new LibSQLConnection($"Data Source={dbPath}");
await conn.OpenAsync();

// 1. Apply schema statement by statement.
Console.Write("[1/4] Executing schema.sql... ");
int i = 0;
try
{
    foreach (var stmt in statements)
    {
        i++;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = stmt;
        // Reader instead of NonQuery so PRAGMAs that return a row don't choke.
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) { /* drain */ }
    }
    Console.WriteLine($"OK ({statements.Count} executed)");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL on statement #{i}");
    Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine($"  SQL (first 200 chars):\n  {statements[i - 1][..Math.Min(200, statements[i - 1].Length)]}");
    return 1;
}

// 2. Verify expected objects exist in sqlite_master.
Console.Write("[2/4] Verifying objects... ");
string[] expected = [
    "projects", "project_remotes",
    "decisions", "alternatives", "principles", "notes",
    "tags", "decision_tags", "principle_tags", "note_tags",
    "links",
    "decision_embeddings", "principle_embeddings", "note_embeddings",
    "decisions_fts", "principles_fts", "notes_fts",
    "decisions_history", "principles_history", "notes_history",
    "_meta"
];
var missing = new List<string>();
foreach (var name in expected)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = @name";
    AddParam(cmd, "@name", name);
    var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    if (count == 0) missing.Add(name);
}
if (missing.Count > 0)
{
    Console.WriteLine($"FAIL\n  Missing: {string.Join(", ", missing)}");
    return 1;
}
Console.WriteLine($"OK ({expected.Length} objects)");

// 3. Sanity insert: project + decision + alternative + principle + link.
Console.Write("[3/4] Insert project/decision/alternative/principle/link... ");
try
{
    using var tx = conn.BeginTransaction();

    const string projectId   = "01JABCDEFGHIJKLMNOPQRSTUVW";
    const string decisionId  = "01JABCDEFGHIJKLMNOPQRSTUV0";
    const string altId       = "01JABCDEFGHIJKLMNOPQRSTUV1";
    const string principleId = "01JABCDEFGHIJKLMNOPQRSTUV2";
    const string linkId      = "01JABCDEFGHIJKLMNOPQRSTUV3";

    Exec(conn, tx,
        "INSERT INTO projects (id, slug, name) VALUES (@id, @slug, @name)",
        ("@id", projectId), ("@slug", "brainyz"), ("@name", "brainyz"));

    Exec(conn, tx,
        "INSERT INTO decisions (id, project_id, title, decision, rationale, confidence) " +
        "VALUES (@id, @pid, @title, @dec, @rat, @conf)",
        ("@id", decisionId), ("@pid", projectId),
        ("@title", "Use LibSQL"),
        ("@dec", "Embedded LibSQL with native vectors"),
        ("@rat", "SQLite superset with native vector datatype"),
        ("@conf", "high"));

    Exec(conn, tx,
        "INSERT INTO alternatives (id, decision_id, title, why_rejected) " +
        "VALUES (@id, @did, @title, @why)",
        ("@id", altId), ("@did", decisionId),
        ("@title", "Plain SQLite"),
        ("@why", "Would require external extensions for vector support"));

    Exec(conn, tx,
        "INSERT INTO principles (id, project_id, title, statement) " +
        "VALUES (@id, @pid, @title, @st)",
        ("@id", principleId), ("@pid", projectId),
        ("@title", "Operational simplicity"),
        ("@st", "One file, one process, zero services"));

    Exec(conn, tx,
        "INSERT INTO links (id, from_type, from_id, to_type, to_id, relation_type) " +
        "VALUES (@id, 'principle', @fid, 'decision', @tid, 'derived_from')",
        ("@id", linkId), ("@fid", principleId), ("@tid", decisionId));

    tx.Commit();
    Console.WriteLine("OK");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL\n  {ex.GetType().Name}: {ex.Message}");
    return 1;
}

// 4. F32_BLOB(768) insert + vector_distance_cos query.
Console.Write("[4/4] Insert embedding F32_BLOB(768) + vector query... ");
try
{
    var vec = BuildVectorLiteral(768);

    await using (var insert = conn.CreateCommand())
    {
        insert.CommandText = """
            INSERT INTO decision_embeddings (decision_id, model, dim, vector, content_hash)
            VALUES (@did, @model, 768, vector32(@vec), @hash)
            """;
        AddParam(insert, "@did",   "01JABCDEFGHIJKLMNOPQRSTUV0");
        AddParam(insert, "@model", "nomic-embed-text:v1.5");
        AddParam(insert, "@vec",   vec);
        AddParam(insert, "@hash",  "dummyhash");
        await insert.ExecuteNonQueryAsync();
    }

    await using (var query = conn.CreateCommand())
    {
        query.CommandText = """
            SELECT decision_id,
                   vector_distance_cos(vector, vector32(@q)) AS dist
            FROM decision_embeddings
            ORDER BY dist ASC
            LIMIT 1
            """;
        AddParam(query, "@q", vec);
        await using var rdr = await query.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
        {
            var id = rdr.GetString(0);
            var dist = rdr.GetDouble(1);
            Console.WriteLine($"OK (nearest: {id}, dist={dist:F6})");
        }
        else
        {
            Console.WriteLine("FAIL: vector query returned 0 rows");
            return 1;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL\n  {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("  (possible cause: this libsql build does not expose vector32() / vector_distance_cos)");
    return 1;
}

Console.WriteLine("\n✓ Schema v0.1 OK against LibSQL");
return 0;

// ───────── helpers ─────────

static string BuildVectorLiteral(int dim)
{
    var sb = new StringBuilder("[");
    for (int i = 0; i < dim; i++)
    {
        if (i > 0) sb.Append(',');
        sb.Append((i * 0.001f).ToString("0.000", CultureInfo.InvariantCulture));
    }
    sb.Append(']');
    return sb.ToString();
}

static void Exec(LibSQLConnection c, IDbTransaction tx, string sql, params (string k, object v)[] ps)
{
    using var cmd = c.CreateCommand();
    cmd.Transaction = (System.Data.Common.DbTransaction)tx;
    cmd.CommandText = sql;
    foreach (var (k, v) in ps) AddParam(cmd, k, v);
    cmd.ExecuteNonQuery();
}

static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
{
    var p = cmd.CreateParameter();
    p.ParameterName = name;
    p.Value = value;
    cmd.Parameters.Add(p);
}

// SQL splitter aware of:
//  - line comments `-- ...`
//  - block comments `/* ... */`
//  - string literals with '' escape
//  - nestable BEGIN ... END blocks (triggers) — inner `;` does NOT split
static IEnumerable<string> SplitStatements(string sql)
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

        // string literal
        if (c == '\'')
        {
            sb.Append(c); i++;
            while (i < sql.Length)
            {
                if (sql[i] == '\'')
                {
                    sb.Append('\''); i++;
                    if (i < sql.Length && sql[i] == '\'') { sb.Append('\''); i++; continue; } // '' escape
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

        // statement separator
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

    static bool IsIdentStart(char ch) => char.IsLetter(ch) || ch == '_';
    static bool IsIdentPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    static bool IsEmptyOrOnlyComments(string s)
    {
        var lines = s.Split('\n');
        foreach (var line in lines)
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
