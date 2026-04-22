using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/custom-orders")]
public class CustomOrdersController : ControllerBase
{
    private readonly ICustomOrderService _customOrderService;
    private readonly IAuditLogService _auditLog;

    public CustomOrdersController(ICustomOrderService customOrderService, IAuditLogService auditLog)
    {
        _customOrderService = customOrderService;
        _auditLog = auditLog;
    }

    /// <summary>Submit a new custom order. Works for both guests and logged-in users.</summary>
    [HttpPost]
    [EnableRateLimiting("orders")]
    public async Task<ActionResult<CustomOrderCheckoutResponse>> Create([FromBody] CreateCustomOrderRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var response = await _customOrderService.CreateAsync(request, userId);
        return Ok(response);
    }

    /// <summary>Get the logged-in customer's own orders.</summary>
    [Authorize]
    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyCollection<CustomOrderSummaryDto>>> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Ok(await _customOrderService.GetByUserAsync(userId));
    }

    /// <summary>Get a specific order — customer can only access their own.</summary>
    [Authorize]
    [HttpGet("my/{id:int}")]
    public async Task<ActionResult<CustomOrderDetailDto>> GetMineById(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var order = await _customOrderService.GetByIdForUserAsync(id, userId);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>List all custom orders (admin). Optionally filter by status.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<ActionResult<IReadOnlyCollection<CustomOrderSummaryDto>>> GetAdmin(
        [FromQuery] CustomOrderStatus? status)
        => Ok(await _customOrderService.GetAdminAsync(status));

    /// <summary>Get full detail of a custom order (admin).</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin/{id:int}")]
    public async Task<ActionResult<CustomOrderDetailDto>> GetAdminById(int id)
    {
        var order = await _customOrderService.GetAdminByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Update order status and optional admin notes (admin).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPut("admin/{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateCustomOrderStatusRequest request)
    {
        await _customOrderService.UpdateStatusAsync(id, request);
        await _auditLog.LogAsync("CustomOrderStatusChanged", "CustomOrder",
            entityId: id.ToString(),
            entityName: $"Custom Order #{id}",
            actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
            actorEmail: User.FindFirstValue(ClaimTypes.Email),
            details: $"Status → {request.Status}{(string.IsNullOrWhiteSpace(request.AdminNotes) ? "" : $"; Notes: {request.AdminNotes}")}");
        return NoContent();
    }
}
