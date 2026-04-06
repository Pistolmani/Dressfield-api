using Dressfield.Application.DTOs;

namespace Dressfield.Application.Interfaces;

public interface IPromoCodeService
{
    Task<IReadOnlyCollection<PromoCodeDto>> GetAdminAsync();
    Task<PromoCodeDto> CreateAsync(CreatePromoCodeRequest request);
    Task<PromoCodeDto> UpdateAsync(int id, UpdatePromoCodeRequest request);
    Task DeleteAsync(int id);
    Task<PromoCodeValidationResultDto> ValidateAsync(ValidatePromoCodeRequest request);
}

