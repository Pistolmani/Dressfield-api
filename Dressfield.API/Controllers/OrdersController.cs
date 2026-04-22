using System.Security.Claims;
using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Dressfield.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly IValidator<CreateOrderRequest> _createValidator;
    private readonly IValidator<UpdateOrderStatusRequest> _updateValidator;
    private readonly IAuditLogService _auditLog;

    public OrdersController(
        IOrderService orders,
        IValidator<CreateOrderRequest> createValidator,
        IValidator<UpdateOrderStatusRequest> updateValidator,
        IAuditLogService auditLog)
    {
        _orders          = orders;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _auditLog        = auditLog;
    }

    /// <summary>POST /api/orders — place an order, returns BOG payment redirect URL.</summary>
    [HttpPost]
    [EnableRateLimiting("orders")]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);   // null for guests

        try
        {
            var result = await _orders.CreateAsync(request, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>GET /api/orders/my — authenticated user's orders.</summary>
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var orders = await _orders.GetByUserAsync(userId);
        return Ok(orders);
    }

    /// <summary>GET /api/orders/my/{id} — authenticated user's order detail.</summary>
    [HttpGet("my/{id:int}")]
    [Authorize]
    public async Task<IActionResult> GetMineById(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var order  = await _orders.GetByIdForUserAsync(id, userId);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>
    /// GET /api/orders/status?orderId=1&amp;key=... â€” public status lookup for payment return flow.
    /// </summary>
    [HttpGet("status")]
    [EnableRateLimiting("orders")]
    public async Task<IActionResult> GetPublicStatus(
        [FromQuery] int orderId,
        [FromQuery(Name = "key")] string? orderKey)
    {
        if (orderId <= 0 || string.IsNullOrWhiteSpace(orderKey))
            return NotFound();

        var status = await _orders.GetPublicStatusAsync(orderId, orderKey);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>GET /api/orders/admin?status=Pending — all orders, optionally filtered.</summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdmin([FromQuery] OrderStatus? status)
    {
        var orders = await _orders.GetAdminAsync(status);
        return Ok(orders);
    }

    /// <summary>GET /api/orders/admin/{id}</summary>
    [HttpGet("admin/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminById(int id)
    {
        var order = await _orders.GetAdminByIdAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>PUT /api/orders/admin/{id}/status</summary>
    [HttpPut("admin/{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        try
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await _orders.UpdateStatusAsync(id, request with { ChangedByUserId = adminUserId });
            await _auditLog.LogAsync("OrderStatusChanged", "Order",
                entityId: id.ToString(),
                entityName: $"Order #{id}",
                actorId: adminUserId,
                actorEmail: User.FindFirstValue(ClaimTypes.Email),
                details: $"Status → {request.Status}{(string.IsNullOrWhiteSpace(request.AdminNotes) ? "" : $"; Notes: {request.AdminNotes}")}");
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
