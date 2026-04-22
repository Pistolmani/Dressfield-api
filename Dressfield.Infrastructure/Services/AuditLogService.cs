using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Entities;
using Dressfield.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dressfield.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly DressfieldDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(DressfieldDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? entityName = null,
        string? actorId = null,
        string? actorEmail = null,
        string? details = null,
        string? ipAddress = null)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                EntityName = entityName,
                ActorId = actorId,
                ActorEmail = actorEmail,
                Details = details,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit log must never break the main flow
            _logger.LogWarning(ex, "Failed to write audit log entry: {Action} {EntityType} {EntityId}", action, entityType, entityId);
        }
    }

    public async Task<AuditLogPageDto> GetAsync(
        int page = 1,
        int pageSize = 50,
        string? entityType = null,
        string? action = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(l => l.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogDto(
                l.Id,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.EntityName,
                l.ActorEmail,
                l.Details,
                l.IpAddress,
                l.CreatedAt))
            .ToListAsync();

        return new AuditLogPageDto(items, totalCount, page, pageSize);
    }
}
