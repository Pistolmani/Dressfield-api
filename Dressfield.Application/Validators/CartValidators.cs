using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class SyncCartRequestValidator : AbstractValidator<SyncCartRequest>
{
    public SyncCartRequestValidator()
    {
        RuleFor(x => x.Items)
            .NotNull().WithMessage("კალათა ვერ იქნება ცარიელი");

        RuleForEach(x => x.Items).SetValidator(new SyncCartItemRequestValidator());
    }
}

public class SyncCartItemRequestValidator : AbstractValidator<SyncCartItemRequest>
{
    public SyncCartItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("პროდუქტი არასწორია");

        RuleFor(x => x.VariantId)
            .GreaterThanOrEqualTo(0)
            .When(x => x.VariantId.HasValue)
            .WithMessage("ვარიანტი არასწორია");

        RuleFor(x => x.Quantity)
            .InclusiveBetween(1, 100).WithMessage("რაოდენობა უნდა იყოს 1-დან 100-მდე");
    }
}
