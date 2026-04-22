using Dressfield.Application.DTOs;
using Dressfield.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dressfield.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IAuditLogService _auditLog;

    public ProductsController(IProductService productService, IAuditLogService auditLog)
    {
        _productService = productService;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ProductSummaryDto>>> GetActive([FromQuery] string? search) =>
        Ok(await _productService.GetActiveAsync(search));

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<ActionResult<IReadOnlyCollection<ProductSummaryDto>>> GetAdmin([FromQuery] string? search) =>
        Ok(await _productService.GetAdminAsync(search));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> GetById(int id)
    {
        var product = await _productService.GetActiveByIdAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("slug/{slug}")]
    public async Task<ActionResult<ProductDetailDto>> GetBySlug(string slug)
    {
        var product = await _productService.GetActiveBySlugAsync(slug);
        return product is null ? NotFound() : Ok(product);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> GetAdminById(int id)
    {
        var product = await _productService.GetAdminByIdAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<ProductDetailDto>> Create([FromBody] CreateProductRequest request)
    {
        try
        {
            var product = await _productService.CreateAsync(request);
            await _auditLog.LogAsync("ProductCreated", "Product",
                entityId: product.Id.ToString(),
                entityName: product.Name,
                actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                actorEmail: User.FindFirstValue(ClaimTypes.Email));
            return CreatedAtAction(nameof(GetAdminById), new { id = product.Id }, product);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDetailDto>> Update(int id, [FromBody] UpdateProductRequest request)
    {
        try
        {
            var product = await _productService.UpdateAsync(id, request);
            await _auditLog.LogAsync("ProductUpdated", "Product",
                entityId: id.ToString(),
                entityName: product.Name,
                actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                actorEmail: User.FindFirstValue(ClaimTypes.Email));
            return Ok(product);
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

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _productService.DeleteAsync(id);
        await _auditLog.LogAsync("ProductDeleted", "Product",
            entityId: id.ToString(),
            actorId: User.FindFirstValue(ClaimTypes.NameIdentifier),
            actorEmail: User.FindFirstValue(ClaimTypes.Email));
        return NoContent();
    }
}
