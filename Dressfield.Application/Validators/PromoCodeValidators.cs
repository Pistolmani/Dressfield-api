using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class CreatePromoCodeRequestValidator : AbstractValidator<CreatePromoCodeRequest>
{
    public CreatePromoCodeRequestValidator()
    {
        ApplyCommonRules();
    }

    private void ApplyCommonRules()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("პრომო კოდი აუცილებელია")
            .MaximumLength(64).WithMessage("პრომო კოდი მაქსიმუმ 64 სიმბოლოა")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("პრომო კოდი შეიძლება შეიცავდეს მხოლოდ ასოებს, ციფრებს, _ და -");

        RuleFor(x => x.DiscountPercentage)
            .GreaterThan(0).WithMessage("ფასდაკლება უნდა იყოს 0%-ზე მეტი")
            .LessThanOrEqualTo(100).WithMessage("ფასდაკლება 100%-ზე მეტი ვერ იქნება");
    }
}

public class UpdatePromoCodeRequestValidator : AbstractValidator<UpdatePromoCodeRequest>
{
    public UpdatePromoCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("პრომო კოდი აუცილებელია")
            .MaximumLength(64).WithMessage("პრომო კოდი მაქსიმუმ 64 სიმბოლოა")
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("პრომო კოდი შეიძლება შეიცავდეს მხოლოდ ასოებს, ციფრებს, _ და -");

        RuleFor(x => x.DiscountPercentage)
            .GreaterThan(0).WithMessage("ფასდაკლება უნდა იყოს 0%-ზე მეტი")
            .LessThanOrEqualTo(100).WithMessage("ფასდაკლება 100%-ზე მეტი ვერ იქნება");
    }
}

public class ValidatePromoCodeRequestValidator : AbstractValidator<ValidatePromoCodeRequest>
{
    public ValidatePromoCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("პრომო კოდი აუცილებელია")
            .MaximumLength(64).WithMessage("პრომო კოდი მაქსიმუმ 64 სიმბოლოა");

        RuleFor(x => x.Subtotal)
            .GreaterThan(0).WithMessage("შეკვეთის ჯამი უნდა იყოს 0-ზე მეტი");
    }
}

