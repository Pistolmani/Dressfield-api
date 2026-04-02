using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class CreateCustomOrderRequestValidator : AbstractValidator<CreateCustomOrderRequest>
{
    public CreateCustomOrderRequestValidator()
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

        RuleFor(x => x.TotalPrice)
            .GreaterThanOrEqualTo(0).WithMessage("ფასი ვერ იქნება უარყოფითი");

        RuleFor(x => x.CustomerNotes)
            .MaximumLength(1000).WithMessage("შენიშვნა მაქსიმუმ 1000 სიმბოლოა");

        RuleFor(x => x.Designs)
            .NotEmpty().WithMessage("მინიმუმ ერთი დიზაინი საჭიროა");

        RuleForEach(x => x.Designs).SetValidator(new CreateCustomOrderDesignRequestValidator());
    }
}

public class CreateCustomOrderDesignRequestValidator : AbstractValidator<CreateCustomOrderDesignRequest>
{
    public CreateCustomOrderDesignRequestValidator()
    {
        RuleFor(x => x.DesignImageUrl)
            .NotEmpty().WithMessage("დიზაინის სურათი აუცილებელია")
            .MaximumLength(500).WithMessage("სურათის URL მაქსიმუმ 500 სიმბოლოა");

        RuleFor(x => x.Placement)
            .MaximumLength(50).WithMessage("განთავსება მაქსიმუმ 50 სიმბოლოა");

        RuleFor(x => x.Size)
            .MaximumLength(20).WithMessage("ზომა მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.ThreadColor)
            .MaximumLength(20).WithMessage("ძაფის ფერი მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("რიგითობა ვერ იქნება უარყოფითი");
    }
}

public class UpdateCustomOrderStatusRequestValidator : AbstractValidator<UpdateCustomOrderStatusRequest>
{
    public UpdateCustomOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("სტატუსი არასწორია");

        RuleFor(x => x.AdminNotes)
            .MaximumLength(1000).WithMessage("ადმინის შენიშვნა მაქსიმუმ 1000 სიმბოლოა");
    }
}
