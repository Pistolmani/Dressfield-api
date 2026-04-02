using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("პროდუქტის სახელი აუცილებელია").MaximumLength(150).WithMessage("პროდუქტის სახელი მაქსიმუმ 150 სიმბოლოა");
        RuleFor(x => x.Slug).NotEmpty().WithMessage("Slug აუცილებელია").MaximumLength(160).WithMessage("Slug მაქსიმუმ 160 სიმბოლოა").Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").WithMessage("Slug უნდა შეიცავდეს მხოლოდ პატარა ლათინურ ასოებს, ციფრებს და ტირეებს");
        RuleFor(x => x.ShortDescription).MaximumLength(300).WithMessage("მოკლე აღწერა მაქსიმუმ 300 სიმბოლოა");
        RuleFor(x => x.Description).NotEmpty().WithMessage("პროდუქტის აღწერა აუცილებელია").MaximumLength(5000).WithMessage("პროდუქტის აღწერა მაქსიმუმ 5000 სიმბოლოა");
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).WithMessage("ფასი ვერ იქნება უარყოფითი");
        RuleFor(x => x.Sku).MaximumLength(64).WithMessage("SKU მაქსიმუმ 64 სიმბოლოა");
        RuleForEach(x => x.Images).SetValidator(new CreateProductImageRequestValidator());
        RuleForEach(x => x.Variants).SetValidator(new CreateProductVariantRequestValidator());
    }
}
