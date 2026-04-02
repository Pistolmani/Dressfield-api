using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("სახელი აუცილებელია")
            .MaximumLength(100).WithMessage("სახელი მაქსიმუმ 100 სიმბოლოა");

        RuleFor(x => x.ContactPhone)
            .NotEmpty().WithMessage("ტელეფონის ნომერი აუცილებელია")
            .MaximumLength(30).WithMessage("ტელეფონის ნომერი მაქსიმუმ 30 სიმბოლოა");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("ელ-ფოსტა აუცილებელია")
            .EmailAddress().WithMessage("ელ-ფოსტის ფორმატი არასწორია")
            .MaximumLength(200).WithMessage("ელ-ფოსტა მაქსიმუმ 200 სიმბოლოა");

        RuleFor(x => x.ShippingCity)
            .NotEmpty().WithMessage("ქალაქი აუცილებელია")
            .MaximumLength(100).WithMessage("ქალაქი მაქსიმუმ 100 სიმბოლოა");

        RuleFor(x => x.ShippingAddressLine1)
            .NotEmpty().WithMessage("მისამართი აუცილებელია")
            .MaximumLength(200).WithMessage("მისამართი მაქსიმუმ 200 სიმბოლოა");

        RuleFor(x => x.ShippingAddressLine2)
            .MaximumLength(200).WithMessage("მისამართის მეორე ველი მაქსიმუმ 200 სიმბოლოა");

        RuleFor(x => x.ShippingPostalCode)
            .MaximumLength(20).WithMessage("საფოსტო ინდექსი მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.CustomerNotes)
            .MaximumLength(1000).WithMessage("შენიშვნა მაქსიმუმ 1000 სიმბოლოა");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("კალათა ცარიელია");

        RuleForEach(x => x.Items).SetValidator(new CartItemRequestValidator());
    }
}

public class CartItemRequestValidator : AbstractValidator<CartItemRequest>
{
    public CartItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("პროდუქტი არასწორია");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("რაოდენობა მინიმუმ 1 უნდა იყოს")
            .LessThanOrEqualTo(100).WithMessage("რაოდენობა მაქსიმუმ 100-ია");
    }
}

public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("სტატუსი არასწორია");

        RuleFor(x => x.AdminNotes)
            .MaximumLength(1000).WithMessage("ადმინის შენიშვნა მაქსიმუმ 1000 სიმბოლოა");
    }
}
