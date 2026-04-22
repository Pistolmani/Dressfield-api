namespace Dressfield.Application.DTOs;

public record AuditLogDto(
    int Id,
    string Action,
    string EntityType,
    string? EntityId,
    string? EntityName,
    string? ActorEmail,
    string? Details,
    string? IpAddress,
    DateTime CreatedAt);

public record AuditLogPageDto(
    IReadOnlyCollection<AuditLogDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
