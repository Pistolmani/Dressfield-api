using System.Security.Claims;
using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dressfield.API.Controllers;

[ApiController]
[Authorize]
[Route("api/cart")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly IValidator<SyncCartRequest> _syncValidator;

    public CartController(ICartService cartService, IValidator<SyncCartRequest> syncValidator)
    {
        _cartService = cartService;
        _syncValidator = syncValidator;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var cart = await _cartService.GetCartAsync(userId);
        return Ok(cart);
    }

    [HttpPut]
    public async Task<IActionResult> Sync([FromBody] SyncCartRequest request)
    {
        var validation = await _syncValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var cart = await _cartService.SyncCartAsync(userId, request);
        return Ok(cart);
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _cartService.ClearCartAsync(userId);
        return NoContent();
    }
}
