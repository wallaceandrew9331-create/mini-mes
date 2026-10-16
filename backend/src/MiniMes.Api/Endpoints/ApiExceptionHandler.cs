using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Domain;
using Npgsql;

namespace MiniMes.Api.Endpoints;

/// <summary>Thrown by endpoint code for protocol-level failures, e.g. a missing If-Match header.</summary>
public class HttpProblemException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Turns domain and persistence exceptions into RFC 7807 problem responses.</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var problem = ex switch
        {
            DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found", ex.Message),
            HttpProblemException p => Problem(p.Status, "Request rejected", ex.Message),
            DbUpdateConcurrencyException => Problem(StatusCodes.Status412PreconditionFailed, "Stale version",
                "The work order was changed by someone else. Reload the traveler and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg } =>
                Problem(StatusCodes.Status409Conflict, "Conflict", pg.ConstraintName is null ? pg.MessageText : $"Violates {pg.ConstraintName}"),
            _ => null,
        };
        if (problem is null) return false;

        if (ex is DomainException { Details.Count: > 0 } domain) problem.Extensions["details"] = domain.Details;
        ctx.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new() { HttpContext = ctx, ProblemDetails = problem, Exception = ex });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
