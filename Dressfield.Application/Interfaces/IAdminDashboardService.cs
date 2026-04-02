using Dressfield.Application.DTOs;

namespace Dressfield.Application.Interfaces;

public interface IAdminDashboardService
{
    Task<AdminDashboardSummaryDto> GetSummaryAsync();
}

