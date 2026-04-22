using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/promo-codes")]
public class PromoCodesController : ControllerBase
{
    private readonly IPromoCodeService _promoCodes;
    private readonly IValidator<CreatePromoCodeRequest> _createValidator;
    private readonly IValidator<UpdatePromoCodeRequest> _updateValidator;
    private readonly IValidator<ValidatePromoCodeRequest> _validateValidator;
    private readonly IAuditLogService _auditLog;

    public PromoCodesController(
        IPromoCodeService promoCodes,
        IValidator<CreatePromoCodeRequest> createValidator,
        IValidator<UpdatePromoCodeRequest> updateValidator,
        IValidator<ValidatePromoCodeRequest> validateValidator,
        IAuditLogService auditLog)
    {
        _promoCodes = promoCodes;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _validateValidator = validateValidator;
        _auditLog = auditLog;
    }

    [HttpPost("validate")]
    [EnableRateLimiting("orders")]
    public async Task<ActionResult<PromoCodeValidationResultDto>> Validate([FromBody] ValidatePromoCodeRequest request)
    {
        var validation = await _validateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));
        }

        return Ok(await _promoCodes.ValidateAsync(request));
    }

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyCollection<PromoCodeDto>>> GetAdmin()
    {
        return Ok(await _promoCodes.GetAdminAsync());
    }

    [HttpPost("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PromoCodeDto>> Create([FromBody] CreatePromoCodeRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));
        }

        try
        {
            var created = await _promoCodes.CreateAsync(request);
            await _auditLog.LogAsync("PromoCodeCreated", "PromoCode",
                entityId: created.Id.ToString(),
                entityName: created.Code,
                actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                actorEmail: User.FindFirstValue(ClaimTypes.Email),
                details: $"{created.DiscountPercentage}% off");
            return CreatedAtAction(nameof(GetAdmin), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("admin/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PromoCodeDto>> Update(int id, [FromBody] UpdatePromoCodeRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));
        }

        try
        {
            var updated = await _promoCodes.UpdateAsync(id, request);
            await _auditLog.LogAsync("PromoCodeUpdated", "PromoCode",
                entityId: id.ToString(),
                entityName: updated.Code,
                actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                actorEmail: User.FindFirstValue(ClaimTypes.Email));
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("admin/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _promoCodes.DeleteAsync(id);
            await _auditLog.LogAsync("PromoCodeDeleted", "PromoCode",
                entityId: id.ToString(),
                actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                actorEmail: User.FindFirstValue(ClaimTypes.Email));
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

