using System.Net;
using System.Net.Http.Json;
using MiniMes.Api.Endpoints;
using static MiniMes.Tests.Integration.MesApiFixture;

namespace MiniMes.Tests.Integration;

[Collection(MesApiCollection.Name)]
public class GenealogyAndRecallTests(MesApiFixture api)
{
    [Fact]
    public async Task Genealogy_reflects_engine_swap_and_flags_parts_due_for_refurb()
    {
        var tree = await api.GetAsync<GenealogyResponse>("/api/serials/B-07/genealogy");
        var engines = tree.Nodes.Where(n => n.PartNumber == "EN-2").Select(n => n.SerialNumber).ToList();

        Assert.Equal(["EN-0101", "EN-0102", "EN-0110"], engines);       // EN-0103 was swapped out
        Assert.True(tree.Nodes.Single(n => n.SerialNumber == "EN-0102").RefurbDue);
        Assert.Equal(1, tree.Nodes.Single(n => n.SerialNumber == "EN-0110").TotalFlights);
        Assert.All(tree.Nodes.Where(n => n.Depth == 2), n => Assert.Contains(n.ParentId, tree.Nodes.Select(p => (int?)p.Id)));
        Assert.Contains(tree.FlightReadinessProblems!, p => p.Contains("EN-0102 is due for refurbishment"));
    }

    [Fact]
    public async Task History_keeps_flights_flown_before_a_part_was_removed()
    {
        var history = await api.GetAsync<HistoryResponse>("/api/serials/EN-0103/history");

        Assert.Equal(2, history.TotalFlights);
        Assert.Null(history.InstalledIn);
        Assert.Equal(2, history.Events.Count(e => e.Kind == "flight"));
        Assert.Contains(history.Events, e => e.Title == "Removed from B-07");
        Assert.Equal(history.Events.OrderBy(e => e.At), history.Events);
    }

    [Fact]
    public async Task Recall_finds_flown_vehicles_and_where_each_suspect_part_is_now()
    {
        var recall = await api.GetAsync<RecallResponse>("/api/recall?lot=L26-0412");

        Assert.Equal(["B-07", "B-09"], recall.AffectedVehicles.Select(v => v.SerialNumber));
        Assert.Equal(3, recall.AffectedVehicles.Single(v => v.SerialNumber == "B-07").FlightsWithLotParts);

        // In B-11 but never flown: listed as a part, not an affected flown vehicle.
        var inB11 = recall.Parts.Single(p => p.CurrentPath[0] == "B-11");
        Assert.Empty(inB11.Flights);
        Assert.Equal(3, inB11.CurrentPath.Count);

        // On the shelf: path is just itself.
        Assert.Contains(recall.Parts, p => p.CurrentPath.Count == 1);
    }

    [Fact]
    public async Task Installing_a_parent_into_its_own_child_is_rejected()
    {
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        var pump = await api.CreateSerialAsync("TP-20", "A", "TP");
        await api.InstallAsync(engine, pump);

        var res = await api.Client.PostAsJsonAsync($"/api/serials/{pump}/children", new InstallRequest(engine, "Test Tech"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task A_part_can_only_be_installed_in_one_parent()
    {
        var engineA = await api.CreateSerialAsync("EN-2", "B", "EN");
        var engineB = await api.CreateSerialAsync("EN-2", "B", "EN");
        var pump = await api.CreateSerialAsync("TP-20", "A", "TP");
        await api.InstallAsync(engineA, pump);

        var res = await api.Client.PostAsJsonAsync($"/api/serials/{engineB}/children", new InstallRequest(pump, "Test Tech"), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Flight_snapshots_the_tree_and_counts_toward_refurb_interval()
    {
        var booster = await api.CreateSerialAsync("BK-1", "C", "B");
        var engine = await api.CreateSerialAsync("EN-2", "B", "EN");
        await api.InstallAsync(booster, engine);

        var res = await api.Client.PostAsJsonAsync($"/api/vehicles/{booster}/flights", new RecordFlightRequest("Test Mission", null), Json);
        res.EnsureSuccessStatusCode();
        var flight = await res.Content.ReadFromJsonAsync<FlightDto>(Json);

        Assert.Equal(1, flight!.FlightNumber);
        Assert.Equal(2, flight.SerialsFlown);
        var tree = await api.GetAsync<GenealogyResponse>($"/api/serials/{booster}/genealogy");
        Assert.All(tree.Nodes, n => Assert.Equal(1, n.FlightsSinceRefurb));
    }

    [Fact]
    public async Task Vehicle_that_is_not_flight_ready_cannot_fly()
    {
        var res = await api.Client.PostAsJsonAsync("/api/vehicles/B-07/flights", new RecordFlightRequest("Nope", null), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Contains("EN-0102 is due for refurbishment", await res.Content.ReadAsStringAsync());
    }
}
