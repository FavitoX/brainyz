// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using System.Globalization;
using Nelknet.LibSQL.Data;

namespace Brainyz.Core.Storage;

internal static class DbExtensions
{
    public static DbParameter Bind(this DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    /// <summary>
    /// Executes an arbitrary statement draining the reader.
    /// Required for PRAGMAs that return rows (journal_mode, user_version, …) —
    /// <c>ExecuteNonQueryAsync</c> fails with "Internal logic error" on Nelknet 0.2.4.
    /// Also safe for pure DDL (the reader simply yields no rows).
    /// </summary>
    public static async Task ExecuteDrainingAsync(
        this LibSQLConnection conn,
        string sql,
        CancellationToken ct = default)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) { /* drain */ }
    }

    // Type-tolerant readers. Nelknet 0.2.4 has two relevant quirks:
    //   1. IsDBNull(ord) may return false while GetValue(ord) returns DBNull.
    //      → do NOT trust IsDBNull; inspect the actual value instead.
    //   2. NULL in a TEXT column sometimes comes back as an empty string rather
    //      than DBNull.
    //      → treat "" as null for optional columns (consistent with the schema:
    //        brainyz optional fields do not use empty string as a meaningful
    //        value; CHECK constraints enforce this).

    public static long GetInt64Safe(this DbDataReader r, int ord)
    {
        var v = r.GetValue(ord);
        if (v is DBNull)
            throw new InvalidDataException($"Unexpected NULL at ordinal {ord} for non-nullable column.");
        return v switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d => (long)d,
            float f => (long)f,
            decimal dec => (long)dec,
            string str => long.Parse(str, CultureInfo.InvariantCulture),
            _ => Convert.ToInt64(v, CultureInfo.InvariantCulture)
        };
    }

    public static long? GetInt64OrNull(this DbDataReader r, int ord)
    {
        var v = r.GetValue(ord);
        if (v is DBNull) return null;
        return v switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d => (long)d,
            float f => (long)f,
            decimal dec => (long)dec,
            string str when !string.IsNullOrEmpty(str) => long.Parse(str, CultureInfo.InvariantCulture),
            string => null,
            _ => Convert.ToInt64(v, CultureInfo.InvariantCulture)
        };
    }

    public static string GetStringSafe(this DbDataReader r, int ord)
    {
        var v = r.GetValue(ord);
        if (v is DBNull)
            throw new InvalidDataException($"Unexpected NULL at ordinal {ord} for non-nullable column.");
        return v switch
        {
            string s => s,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => v?.ToString() ?? string.Empty
        };
    }

    public static string? GetStringOrNull(this DbDataReader r, int ord)
    {
        var v = r.GetValue(ord);
        if (v is DBNull) return null;
        var s = v switch
        {
            string str => str,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => v?.ToString()
        };
        // Nelknet quirk: NULL of TEXT sometimes arrives as "" — treat empty as
        // null so optional-field semantics remain consistent.
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
