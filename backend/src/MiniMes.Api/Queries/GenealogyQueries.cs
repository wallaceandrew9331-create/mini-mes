using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;

namespace MiniMes.Api.Queries;

/// <summary>
/// Genealogy reads. Genealogy is stored as an adjacency list (installations: parent_id, child_id) and walked
/// with recursive CTEs. Writes stay trivial (one row per install/remove) and history is kept for free;
/// the price is a recursive query per read, which is cheap at assembly depths of 3-5 levels.
/// These run through Dapper on EF's connection because recursive SQL is clearer written as SQL.
/// </summary>
public class GenealogyQueries(MesDbContext db)
{
    static GenealogyQueries() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    // Join the DbContext's transaction when one is open, so reads see uncommitted writes from the same unit of work.
    private IDbConnection Conn => db.Database.GetDbConnection();
    private IDbTransaction? Tx => db.Database.CurrentTransaction?.GetDbTransaction();

    private const string TreeSql = """
        WITH RECURSIVE tree AS (
            SELECT s.id, NULL::int AS parent_id, 0 AS depth
            FROM serials s
            WHERE s.serial_number = @serialNumber
          UNION ALL
            SELECT i.child_id, i.parent_id, t.depth + 1
            FROM installations i
            JOIN tree t ON i.parent_id = t.id
            WHERE i.removed_at IS NULL
        )
        SELECT t.id, t.parent_id, t.depth, s.serial_number, p.part_number, p.name AS part_name, p.kind AS part_kind,
               r.revision, s.lot_number, s.status, p.refurb_interval_flights,
               usage.total_flights, usage.flights_since_refurb,
               (SELECT count(*)::int FROM ncrs n WHERE n.serial_id = t.id AND n.status = 'Open') AS open_ncrs
        FROM tree t
        JOIN serials s ON s.id = t.id
        JOIN part_revisions r ON r.id = s.part_revision_id
        JOIN parts p ON p.id = r.part_id
        -- Flight counts come from manifests (what actually flew), split at the last completed refurb.
        CROSS JOIN LATERAL (
            SELECT count(*)::int AS total_flights,
                   (count(*) FILTER (WHERE f.flown_at > coalesce(
                       (SELECT max(w.completed_at) FROM work_orders w
                        WHERE w.serial_id = t.id AND w.type = 'Refurb' AND w.status = 'Complete'),
                       '-infinity')))::int AS flights_since_refurb
            FROM flight_manifest m
            JOIN flights f ON f.id = m.flight_id
            WHERE m.serial_id = t.id
        ) usage
        ORDER BY t.depth, p.part_number, s.serial_number
        """;

    /// <summary>The serial and everything currently installed beneath it. Empty if the serial does not exist.</summary>
    public async Task<IReadOnlyList<GenealogyNode>> TreeAsync(string serialNumber)
    {
        var rows = await Conn.QueryAsync<GenealogyNode>(TreeSql, new { serialNumber }, Tx);
        return rows.AsList();
    }

    /// <summary>True if <paramref name="candidateId"/> is <paramref name="nodeId"/> or one of its ancestors.</summary>
    public Task<bool> IsSelfOrAncestorAsync(int candidateId, int nodeId) =>
        Conn.ExecuteScalarAsync<bool>("""
            WITH RECURSIVE up AS (
                SELECT @nodeId AS id
              UNION ALL
                SELECT i.parent_id FROM installations i JOIN up ON i.child_id = up.id WHERE i.removed_at IS NULL
            )
            SELECT EXISTS (SELECT 1 FROM up WHERE id = @candidateId)
            """, new { candidateId, nodeId }, Tx);

    /// <summary>
    /// For each serial in a lot, its current install path from the top-level assembly down, e.g.
    /// [B-07, EN-0102, VLV-0301]. A serial that is not installed has a path of just itself.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string[]>> CurrentPathsForLotAsync(string lotNumber)
    {
        var rows = await Conn.QueryAsync<(int SerialId, string[] Path)>("""
            WITH RECURSIVE up AS (
                SELECT s.id AS serial_id, s.id AS node_id, ARRAY[s.serial_number::text] AS path
                FROM serials s
                WHERE s.lot_number = @lotNumber
              UNION ALL
                SELECT up.serial_id, p.id, p.serial_number::text || up.path
                FROM up
                JOIN installations i ON i.child_id = up.node_id AND i.removed_at IS NULL
                JOIN serials p ON p.id = i.parent_id
            )
            SELECT DISTINCT ON (serial_id) serial_id, path
            FROM up
            ORDER BY serial_id, cardinality(path) DESC
            """, new { lotNumber }, Tx);
        return rows.ToDictionary(r => r.SerialId, r => r.Path);
    }
}
