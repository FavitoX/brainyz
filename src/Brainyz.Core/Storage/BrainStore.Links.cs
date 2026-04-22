// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Models;

namespace Brainyz.Core.Storage;

public sealed partial class BrainStore
{
    public async Task AddLinkAsync(Link link, CancellationToken ct = default)
    {
        var now = _clock.NowMs();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO links
                (id, from_type, from_id, to_type, to_id, relation_type, note, created_at)
            VALUES (@id, @from_type, @from_id, @to_type, @to_id, @rel, @note, @created)
            """;
        cmd.Bind("@id", link.Id);
        cmd.Bind("@from_type", EncodeLinkEntity(link.FromType));
        cmd.Bind("@from_id", link.FromId);
        cmd.Bind("@to_type", EncodeLinkEntity(link.ToType));
        cmd.Bind("@to_id", link.ToId);
        cmd.Bind("@rel", EncodeLinkRelation(link.Relation));
        cmd.Bind("@note", link.Annotation);
        cmd.Bind("@created", link.CreatedAtMs == 0 ? now : link.CreatedAtMs);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteLinkAsync(string linkId, CancellationToken ct = default)
    {
        if (!await ExistsAsync("links", linkId, ct)) return false;

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM links WHERE id = @id";
        cmd.Bind("@id", linkId);
        await cmd.ExecuteNonQueryAsync(ct);
        return true;
    }

    /// <summary>
    /// Retrieves links with optional filters. Any combination of
    /// <paramref name="fromType"/>/<paramref name="fromId"/>,
    /// <paramref name="toType"/>/<paramref name="toId"/>, and
    /// <paramref name="relation"/> is valid; unspecified filters are ignored.
    /// </summary>
    public async Task<IReadOnlyList<Link>> GetLinksAsync(
        LinkEntity? fromType = null,
        string? fromId = null,
        LinkEntity? toType = null,
        string? toId = null,
        LinkRelation? relation = null,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (fromType is not null) where.Add("from_type = @from_type");
        if (fromId   is not null) where.Add("from_id = @from_id");
        if (toType   is not null) where.Add("to_type = @to_type");
        if (toId     is not null) where.Add("to_id = @to_id");
        if (relation is not null) where.Add("relation_type = @rel");

        var sql = """
            SELECT id, from_type, from_id, to_type, to_id, relation_type, note, created_at
            FROM links
            """;
        if (where.Count > 0) sql += "\nWHERE " + string.Join(" AND ", where);
        sql += "\nORDER BY created_at DESC";

        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        if (fromType is not null) cmd.Bind("@from_type", EncodeLinkEntity(fromType.Value));
        if (fromId   is not null) cmd.Bind("@from_id", fromId);
        if (toType   is not null) cmd.Bind("@to_type", EncodeLinkEntity(toType.Value));
        if (toId     is not null) cmd.Bind("@to_id", toId);
        if (relation is not null) cmd.Bind("@rel", EncodeLinkRelation(relation.Value));

        var results = new List<Link>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct)) results.Add(ReadLink(rdr));
        return results;
    }

    private static Link ReadLink(System.Data.Common.DbDataReader rdr) => new(
        Id: rdr.GetStringSafe(0),
        FromType: DecodeLinkEntity(rdr.GetStringSafe(1)),
        FromId: rdr.GetStringSafe(2),
        ToType: DecodeLinkEntity(rdr.GetStringSafe(3)),
        ToId: rdr.GetStringSafe(4),
        Relation: DecodeLinkRelation(rdr.GetStringSafe(5)),
        Annotation: rdr.GetStringOrNull(6),
        CreatedAtMs: rdr.GetInt64Safe(7));

    private static string EncodeLinkEntity(LinkEntity e) => e switch
    {
        LinkEntity.Decision => "decision",
        LinkEntity.Principle => "principle",
        LinkEntity.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(e))
    };

    private static LinkEntity DecodeLinkEntity(string s) => s switch
    {
        "decision" => LinkEntity.Decision,
        "principle" => LinkEntity.Principle,
        "note" => LinkEntity.Note,
        _ => throw new InvalidDataException($"Unknown link entity: '{s}'")
    };

    private static string EncodeLinkRelation(LinkRelation r) => r switch
    {
        LinkRelation.Supersedes => "supersedes",
        LinkRelation.RelatesTo => "relates_to",
        LinkRelation.DependsOn => "depends_on",
        LinkRelation.ConflictsWith => "conflicts_with",
        LinkRelation.InformedBy => "informed_by",
        LinkRelation.DerivedFrom => "derived_from",
        LinkRelation.SplitFrom => "split_from",
        LinkRelation.Contradicts => "contradicts",
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    private static LinkRelation DecodeLinkRelation(string s) => s switch
    {
        "supersedes" => LinkRelation.Supersedes,
        "relates_to" => LinkRelation.RelatesTo,
        "depends_on" => LinkRelation.DependsOn,
        "conflicts_with" => LinkRelation.ConflictsWith,
        "informed_by" => LinkRelation.InformedBy,
        "derived_from" => LinkRelation.DerivedFrom,
        "split_from" => LinkRelation.SplitFrom,
        "contradicts" => LinkRelation.Contradicts,
        _ => throw new InvalidDataException($"Unknown link relation: '{s}'")
    };
}
