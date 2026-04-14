using Dressfield.Application.DTOs;
using Dressfield.Core.Enums;

namespace Dressfield.Application.Interfaces;

public interface ICustomOrderService
{
    // Admin
    Task<IReadOnlyCollection<CustomOrderSummaryDto>> GetAdminAsync(CustomOrderStatus? status);
    Task<CustomOrderDetailDto?> GetAdminByIdAsync(int id);
    Task UpdateStatusAsync(int id, UpdateCustomOrderStatusRequest request);

    // Customer / Guest
    Task<IReadOnlyCollection<CustomOrderSummaryDto>> GetByUserAsync(string userId);
    Task<CustomOrderDetailDto?> GetByIdForUserAsync(int id, string userId);

    // Public — supports both authenticated users and guests
    Task<CustomOrderCheckoutResponse> CreateAsync(CreateCustomOrderRequest request, string? userId);
    Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey);
}
