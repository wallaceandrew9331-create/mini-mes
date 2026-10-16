using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Events;
using MiniMes.Api.Queries;

namespace MiniMes.Api.Services;

/// <summary>Install and remove serials. Each call stages changes; the caller saves.</summary>
public class GenealogyService(MesDbContext db, GenealogyQueries queries)
{
    public async Task<Installation> InstallAsync(string parentSn, string childSn, string by, DateTimeOffset now)
    {
        var parent = await FindAsync(parentSn);
        var child = await FindAsync(childSn);

        if (string.IsNullOrWhiteSpace(by)) throw new DomainException("Install requires a name");
        if (parent.Status == SerialStatus.Scrapped || child.Status == SerialStatus.Scrapped)
            throw new DomainException("Cannot install to or from a scrapped serial");
        if (child.PartRevision.Part.Kind == PartKind.Vehicle)
            throw new DomainException($"{childSn} is a vehicle and cannot be installed in another serial");
        if (await db.Installations.AnyAsync(i => i.ChildId == child.Id && i.RemovedAt == null))
            throw new DomainException($"{childSn} is already installed; remove it first");
        // Without this an install could create a cycle and the recursive tree query would never terminate.
        if (await queries.IsSelfOrAncestorAsync(child.Id, parent.Id))
            throw new DomainException($"{childSn} is {parentSn} or one of its parents");

        var installation = new Installation { Parent = parent, Child = child, InstalledBy = by.Trim(), InstalledAt = now };
        db.Installations.Add(installation);
        db.Outbox.Add(OutboxMessage.Create("part.installed", childSn, new { parent = parentSn, child = childSn, by, at = now }, now));
        return installation;
    }

    /// <summary>Removes the serial from whatever it is installed in. Returns null if it was not installed.</summary>
    public async Task<Installation?> RemoveAsync(string childSn, string by, string reason, DateTimeOffset now)
    {
        var child = await FindAsync(childSn);
        var installation = await db.Installations.Include(i => i.Parent)
            .SingleOrDefaultAsync(i => i.ChildId == child.Id && i.RemovedAt == null);
        if (installation is null) return null;

        installation.Remove(by, reason, now);
        db.Outbox.Add(OutboxMessage.Create("part.removed", childSn,
            new { parent = installation.Parent.SerialNumber, child = childSn, by, reason, at = now }, now));
        return installation;
    }

    private async Task<SerializedPart> FindAsync(string serialNumber) =>
        await db.Serials.Include(s => s.PartRevision).ThenInclude(r => r.Part)
            .SingleOrDefaultAsync(s => s.SerialNumber == serialNumber)
        ?? throw new NotFoundException($"Serial {serialNumber} not found");
}
