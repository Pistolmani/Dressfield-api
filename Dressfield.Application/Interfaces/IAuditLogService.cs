using Dressfield.Application.DTOs;

namespace Dressfield.Application.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? entityName = null,
        string? actorId = null,
        string? actorEmail = null,
        string? details = null,
        string? ipAddress = null);

    Task<AuditLogPageDto> GetAsync(
        int page = 1,
        int pageSize = 50,
        string? entityType = null,
        string? action = null);
}
