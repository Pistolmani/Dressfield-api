using Dressfield.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = "Admin")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogService _auditLog;

    public AuditLogsController(IAuditLogService auditLog)
    {
        _auditLog = auditLog;
    }

    /// <summary>GET /api/admin/audit-logs?page=1&pageSize=50&entityType=Product&action=ProductCreated</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null)
    {
        var result = await _auditLog.GetAsync(page, pageSize, entityType, action);
        return Ok(result);
    }
}
