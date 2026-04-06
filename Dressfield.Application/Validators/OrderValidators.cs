using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.ContactName)
            .NotEmpty().WithMessage("Contact name is required")
            .MaximumLength(100).WithMessage("Contact name must be 100 characters or less");

        RuleFor(x => x.ContactPhone)
            .NotEmpty().WithMessage("Phone number is required")
            .MaximumLength(30).WithMessage("Phone number must be 30 characters or less");

        RuleFor(x => x.ContactEmail)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email format is invalid")
            .MaximumLength(200).WithMessage("Email must be 200 characters or less");

        RuleFor(x => x.ShippingCity)
            .NotEmpty().WithMessage("City is required")
            .MaximumLength(100).WithMessage("City must be 100 characters or less");

        RuleFor(x => x.ShippingAddressLine1)
            .NotEmpty().WithMessage("Shipping address is required")
            .MaximumLength(200).WithMessage("Shipping address must be 200 characters or less");

        RuleFor(x => x.ShippingAddressLine2)
            .MaximumLength(200).WithMessage("Address line 2 must be 200 characters or less");

        RuleFor(x => x.ShippingPostalCode)
            .MaximumLength(20).WithMessage("Postal code must be 20 characters or less");

        RuleFor(x => x.PromoCode)
            .MaximumLength(64).WithMessage("Promo code must be 64 characters or less");

        RuleFor(x => x.CustomerNotes)
            .MaximumLength(1000).WithMessage("Customer notes must be 1000 characters or less");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Cart is empty");

        RuleForEach(x => x.Items).SetValidator(new CartItemRequestValidator());
    }
}

public class CartItemRequestValidator : AbstractValidator<CartItemRequest>
{
    public CartItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("Invalid product");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be at least 1")
            .LessThanOrEqualTo(100).WithMessage("Quantity cannot exceed 100");
    }
}

public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid order status");

        RuleFor(x => x.AdminNotes)
            .MaximumLength(1000).WithMessage("Admin notes must be 1000 characters or less");
    }
}

