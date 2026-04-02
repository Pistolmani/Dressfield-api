using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("სახელი აუცილებელია")
            .MinimumLength(2).WithMessage("სახელი მინიმუმ 2 სიმბოლო")
            .MaximumLength(50);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("გვარი აუცილებელია")
            .MinimumLength(2).WithMessage("გვარი მინიმუმ 2 სიმბოლო")
            .MaximumLength(50);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("ელ-ფოსტა აუცილებელია")
            .EmailAddress().WithMessage("ელ-ფოსტის ფორმატი არასწორია");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("პაროლი აუცილებელია")
            .MinimumLength(8).WithMessage("პაროლი მინიმუმ 8 სიმბოლო")
            .Matches("[A-Z]").WithMessage("პაროლი უნდა შეიცავდეს დიდ ასოს")
            .Matches("[a-z]").WithMessage("პაროლი უნდა შეიცავდეს პატარა ასოს")
            .Matches("[0-9]").WithMessage("პაროლი უნდა შეიცავდეს ციფრს");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("პაროლები არ ემთხვევა");

        RuleFor(x => x.Phone)
            .Matches(@"^\+995\s?5\d{2}\s?\d{3}\s?\d{3}$")
            .When(x => !string.IsNullOrEmpty(x.Phone))
            .WithMessage("ტელეფონის ფორმატი: +995 5XX XXX XXX");
    }
}
