using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Domain;
using MiniMes.Api.Endpoints;
using Npgsql;
using static MiniMes.Tests.Integration.MesApiFixture;

namespace MiniMes.Tests.Integration;

[Collection(MesApiCollection.Name)]
public class TravelerTests(MesApiFixture api)
{
    [Fact]
    public async Task Concurrent_sign_offs_with_the_same_version_let_exactly_one_win()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var (wo, etag) = await api.ReleaseAsync(engine);
        var first = wo.Operations[0].Id;

        var results = await Task.WhenAll(
            api.SignOffAsync(wo, first, etag, "Inspector A"),
            api.SignOffAsync(wo, first, etag, "Inspector B"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Sign_off_returns_a_new_etag_and_the_old_one_goes_stale()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var (wo, etag) = await api.ReleaseAsync(engine);

        var ok = await api.SignOffAsync(wo, wo.Operations[0].Id, etag);
        ok.EnsureSuccessStatusCode();
        Assert.NotEqual(etag, ok.Headers.ETag!.Tag);

        var stale = await api.SignOffAsync(wo, wo.Operations[1].Id, etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task Ncr_blocks_operation_until_rework_is_signed_off()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var (wo, etag) = await api.ReleaseAsync(engine);
        var op10 = wo.Operations[0];

        var raised = await api.Client.PostAsJsonAsync("/api/ncrs",
            new RaiseNcrRequest(engine, wo.Id, op10.Id, "Kit short one B-nut", "QA Tech"), Json);
        raised.EnsureSuccessStatusCode();
        var ncr = (await raised.Content.ReadFromJsonAsync<NcrDto>(Json))!;

        // The NCR bumped the version, so reload; then the open NCR itself blocks the sign-off.
        var res = await api.Client.GetAsync($"/api/work-orders/{wo.Id}");
        etag = res.Headers.ETag!.Tag;
        var blocked = await api.SignOffAsync(wo, op10.Id, etag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        (await api.Client.PostAsJsonAsync($"/api/ncrs/{ncr.Id}/disposition",
            new DispositionRequest(Disposition.Rework, "MRB", "Pull B-nut from stores"), Json)).EnsureSuccessStatusCode();

        res = await api.Client.GetAsync($"/api/work-orders/{wo.Id}");
        var traveler = (await res.Content.ReadFromJsonAsync<Traveler>(Json))!;
        var rework = traveler.Operations[0];
        Assert.True(rework.IsRework);
        Assert.True(rework.IsCurrent);

        var signRework = await api.SignOffAsync(traveler, rework.Id, res.Headers.ETag!.Tag);
        signRework.EnsureSuccessStatusCode();
        (await api.SignOffAsync(traveler, op10.Id, signRework.Headers.ETag!.Tag)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Scrapping_an_installed_part_removes_it_from_the_genealogy()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var valve = await api.CreateSerialAsync("VLV-115", "B", "MOV");
        await api.InstallAsync(engine, valve);

        var raised = await api.Client.PostAsJsonAsync("/api/ncrs", new RaiseNcrRequest(valve, null, null, "Seat leak at proof", "QA Tech"), Json);
        var ncr = (await raised.Content.ReadFromJsonAsync<NcrDto>(Json))!;
        (await api.Client.PostAsJsonAsync($"/api/ncrs/{ncr.Id}/disposition", new DispositionRequest(Disposition.Scrap, "MRB", null), Json))
            .EnsureSuccessStatusCode();

        var tree = await api.GetAsync<GenealogyResponse>($"/api/serials/{engine}/genealogy");
        Assert.Single(tree.Nodes);
        var history = await api.GetAsync<HistoryResponse>($"/api/serials/{valve}/history");
        Assert.Equal(SerialStatus.Scrapped, history.Serial.Status);
        Assert.Contains(history.Events, e => e.Kind == "remove" && e.Detail!.Contains(ncr.Number));
    }

    [Fact]
    public async Task Sign_off_writes_a_station_event_to_the_outbox_in_the_same_transaction()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var (wo, etag) = await api.ReleaseAsync(engine);
        (await api.SignOffAsync(wo, wo.Operations[0].Id, etag)).EnsureSuccessStatusCode();

        var (scope, db) = api.NewDbContext();
        using (scope)
        {
            var evt = await db.Outbox.SingleAsync(m => m.Type == "operation.signed_off" && m.Key == engine);
            Assert.Contains(wo.Number, evt.Payload);
            Assert.Contains("\"station\": \"RCV\"", evt.Payload); // jsonb normalises spacing
        }
    }

    [Fact]
    public async Task Sign_offs_are_append_only_in_the_app_and_in_the_database()
    {
        var (scope, db) = api.NewDbContext();
        using (scope)
        {
            var signOff = await db.SignOffs.FirstAsync();
            db.Entry(signOff).Property(s => s.SignedBy).CurrentValue = "Someone else";
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();

            // Bypass EF entirely: the trigger still refuses.
            var ex = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM sign_offs WHERE id = {0}", signOff.Id));
            Assert.Contains("append-only", ex.MessageText);
        }
    }

    [Fact]
    public async Task Database_rejects_a_second_active_installation_even_without_the_api_check()
    {
        var engineA = await api.CreateSerialAsync("EN-2", "B", "EN");
        var engineB = await api.CreateSerialAsync("EN-2", "B", "EN");
        var pump = await api.CreateSerialAsync("TP-20", "A", "TP");
        await api.InstallAsync(engineA, pump);

        var (scope, db) = api.NewDbContext();
        using (scope)
        {
            var b = await db.Serials.SingleAsync(s => s.SerialNumber == engineB);
            var p = await db.Serials.SingleAsync(s => s.SerialNumber == pump);
            db.Installations.Add(new Installation { Parent = b, Child = p, InstalledBy = "rogue script", InstalledAt = DateTimeOffset.UtcNow });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)ex.InnerException!).SqlState);
        }
    }
}
