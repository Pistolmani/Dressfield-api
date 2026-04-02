using Dressfield.Application.DTOs;

namespace Dressfield.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetCartAsync(string userId);
    Task<CartDto> SyncCartAsync(string userId, SyncCartRequest request);
    Task ClearCartAsync(string userId);
}
