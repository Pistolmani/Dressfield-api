using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
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

public class CreateProductImageRequestValidator : AbstractValidator<CreateProductImageRequest>
{
    public CreateProductImageRequestValidator()
    {
        RuleFor(x => x.ImageUrl).NotEmpty().WithMessage("სურათის URL აუცილებელია").MaximumLength(500).WithMessage("სურათის URL მაქსიმუმ 500 სიმბოლოა");
        RuleFor(x => x.AltText).MaximumLength(200).WithMessage("Alt ტექსტი მაქსიმუმ 200 სიმბოლოა");
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("სურათის რიგი ვერ იქნება უარყოფითი");
    }
}

public class CreateProductVariantRequestValidator : AbstractValidator<CreateProductVariantRequest>
{
    public CreateProductVariantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("ვარიანტის სახელი აუცილებელია").MaximumLength(100).WithMessage("ვარიანტის სახელი მაქსიმუმ 100 სიმბოლოა");
        RuleFor(x => x.Value).MaximumLength(100).WithMessage("ვარიანტის მნიშვნელობა მაქსიმუმ 100 სიმბოლოა");
        RuleFor(x => x.Sku).MaximumLength(64).WithMessage("SKU მაქსიმუმ 64 სიმბოლოა");
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0).WithMessage("მარაგი ვერ იქნება უარყოფითი");
    }
}
