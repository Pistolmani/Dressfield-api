using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/promo-codes")]
public class PromoCodesController : ControllerBase
{
    private readonly IPromoCodeService _promoCodes;
    private readonly IValidator<CreatePromoCodeRequest> _createValidator;
    private readonly IValidator<UpdatePromoCodeRequest> _updateValidator;
    private readonly IValidator<ValidatePromoCodeRequest> _validateValidator;

    public PromoCodesController(
        IPromoCodeService promoCodes,
        IValidator<CreatePromoCodeRequest> createValidator,
        IValidator<UpdatePromoCodeRequest> updateValidator,
        IValidator<ValidatePromoCodeRequest> validateValidator)
    {
        _promoCodes = promoCodes;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _validateValidator = validateValidator;
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
            return Ok(await _promoCodes.UpdateAsync(id, request));
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
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

