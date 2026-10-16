using Dapper;
using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;

namespace MiniMes.Api.Services;

/// <summary>Human-readable document numbers from Postgres sequences: gap-tolerant, never duplicated.</summary>
public class Numbering(MesDbContext db)
{
    public async Task<string> NextWorkOrderAsync() => $"WO-{await NextAsync("work_order_numbers")}";

    public async Task<string> NextNcrAsync() => $"NCR-{await NextAsync("ncr_numbers"):0000}";

    private Task<long> NextAsync(string sequence) =>
        db.Database.GetDbConnection().ExecuteScalarAsync<long>($"SELECT nextval('{sequence}')");
}
