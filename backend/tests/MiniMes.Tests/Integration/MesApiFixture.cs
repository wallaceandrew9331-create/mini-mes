using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniMes.Api.Data;
using MiniMes.Api.Endpoints;
using Testcontainers.PostgreSql;

namespace MiniMes.Tests.Integration;

/// <summary>
/// One real Postgres 16 (Testcontainers) and one in-process API per test run, seeded with the demo factory.
/// Tests that mutate data create their own serials, so they do not disturb the seed other tests read.
/// </summary>
public sealed class MesApiFixture : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Mes", postgres.GetConnectionString()));
        Client = factory.CreateClient(); // starts the app: migrate + seed
    }

    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    /// <summary>A DbContext for tests that go underneath the API.</summary>
    public (IServiceScope Scope, MesDbContext Db) NewDbContext()
    {
        var scope = factory.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<MesDbContext>());
    }

    // --- helpers for building fresh, isolated test data through the API ---

    public static string Unique(string prefix) => $"{prefix}-T{Guid.NewGuid().ToString("N")[..8]}";

    public async Task<string> CreateSerialAsync(string partNumber, string revision, string prefix, string? lot = null)
    {
        var sn = Unique(prefix);
        var res = await Client.PostAsJsonAsync("/api/serials", new CreateSerialRequest(partNumber, revision, sn, lot), Json);
        res.EnsureSuccessStatusCode();
        return sn;
    }

    public async Task InstallAsync(string parent, string child)
    {
        var res = await Client.PostAsJsonAsync($"/api/serials/{parent}/children", new InstallRequest(child, "Test Tech"), Json);
        res.EnsureSuccessStatusCode();
    }

    public async Task<(Traveler Traveler, string ETag)> ReleaseAsync(string serialNumber, string type = "Build")
    {
        var res = await Client.PostAsJsonAsync("/api/work-orders", new { serialNumber, type }, Json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Traveler>(Json) ?? throw new InvalidOperationException(), res.Headers.ETag!.Tag);
    }

    public Task<HttpResponseMessage> SignOffAsync(Traveler wo, int operationId, string etag, string by = "Test Tech")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/work-orders/{wo.Id}/operations/{operationId}/sign-off")
        {
            Content = JsonContent.Create(new SignOffRequest(by, null), options: Json),
        };
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        return Client.SendAsync(req);
    }

    public async Task<T> GetAsync<T>(string url) =>
        await Client.GetFromJsonAsync<T>(url, Json) ?? throw new InvalidOperationException($"Empty response from {url}");
}

[CollectionDefinition(Name)]
public class MesApiCollection : ICollectionFixture<MesApiFixture>
{
    public const string Name = "MES API";
}
