namespace MiniMes.Api.Domain;

/// <summary>A business rule was violated. Mapped to HTTP 422 by the API.</summary>
public class DomainException(string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public IReadOnlyList<string> Details { get; } = details ?? [];
}

/// <summary>A referenced record does not exist. Mapped to HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Marker for entities that form the audit trail: inserts only, never updates or deletes.</summary>
public interface IAppendOnly;
