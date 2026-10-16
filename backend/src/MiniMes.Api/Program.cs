using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Endpoints;
using MiniMes.Api.Events;
using MiniMes.Api.Queries;
using MiniMes.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// The connection string is read when the context is created, so tests can override configuration.
builder.Services.AddDbContext<MesDbContext>((sp, options) => options
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Mes"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<GenealogyQueries>();
builder.Services.AddScoped<GenealogyService>();
builder.Services.AddScoped<FlightService>();
builder.Services.AddScoped<Numbering>();

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddHostedService<OutboxRelay>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

var api = app.MapGroup("/api");
api.MapWorkOrders();
api.MapNcrs();
api.MapSerials();
api.MapFlights();
api.MapGet("/stations", async (MesDbContext db) => await db.Stations.OrderBy(s => s.Code).ToListAsync()).WithTags("Reference");

app.MapGet("/healthz", async (MesDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();

public partial class Program; // exposes the entry point to WebApplicationFactory in the integration tests
