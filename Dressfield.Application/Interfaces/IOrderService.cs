using Dressfield.Application.DTOs;
using Dressfield.Core.Enums;

namespace Dressfield.Application.Interfaces;

public interface IOrderService
{
    // Admin
    Task<IReadOnlyCollection<OrderSummaryDto>> GetAdminAsync(OrderStatus? status);
    Task<OrderDetailDto?> GetAdminByIdAsync(int id);
    Task UpdateStatusAsync(int id, UpdateOrderStatusRequest request, string? changedByUserId = null);
    Task DeleteAsync(int id);
    Task<int> DeleteAllPendingAsync();

    // Customer
    Task<IReadOnlyCollection<OrderSummaryDto>> GetByUserAsync(string userId);
    Task<OrderDetailDto?> GetByIdForUserAsync(int id, string userId);
    Task<OrderStatusLookupDto?> GetPublicStatusAsync(int orderId, string orderKey);

    // Checkout - works for guests and logged-in users
    Task<CheckoutResponse> CreateAsync(CreateOrderRequest request, string? userId, string? idempotencyKey = null);

    // Payment webhook - called when BOG confirms payment status
    Task HandlePaymentCallbackAsync(string bogOrderId, string? orderKey);
}
