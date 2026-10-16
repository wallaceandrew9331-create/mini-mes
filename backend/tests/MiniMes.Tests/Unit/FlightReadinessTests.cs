using MiniMes.Api.Domain;

namespace MiniMes.Tests.Unit;

public class FlightReadinessTests
{
    private static GenealogyNode Node(string sn, int depth, string kind = "Component", string status = "Serviceable",
        int? interval = null, int sinceRefurb = 0, int openNcrs = 0) => new()
    {
        SerialNumber = sn,
        Depth = depth,
        PartKind = kind,
        Status = status,
        RefurbIntervalFlights = interval,
        FlightsSinceRefurb = sinceRefurb,
        OpenNcrs = openNcrs,
    };

    [Fact]
    public void Serviceable_vehicle_within_limits_is_ready()
    {
        var tree = new[] { Node("B-1", 0, "Vehicle"), Node("EN-1", 1, "Assembly", interval: 3, sinceRefurb: 2) };
        Assert.Empty(FlightReadiness.Check(tree));
    }

    [Fact]
    public void Part_at_its_refurb_interval_blocks_flight()
    {
        var tree = new[] { Node("B-1", 0, "Vehicle"), Node("EN-1", 1, "Assembly", interval: 3, sinceRefurb: 3) };
        var problem = Assert.Single(FlightReadiness.Check(tree));
        Assert.Contains("EN-1 is due for refurbishment (3/3", problem);
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        var tree = new[]
        {
            Node("B-1", 0, "Vehicle", status: "InRefurb"),
            Node("GF-1", 1, openNcrs: 1),
            Node("TP-1", 2, status: "Scrapped"),
        };
        Assert.Equal(3, FlightReadiness.Check(tree).Count);
    }

    [Fact]
    public void Only_vehicles_fly()
    {
        var problem = Assert.Single(FlightReadiness.Check([Node("EN-1", 0, "Assembly")]));
        Assert.Contains("not a vehicle", problem);
    }
}
