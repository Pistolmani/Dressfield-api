using Dressfield.Application.DTOs;
using FluentValidation;

namespace Dressfield.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("ელ-ფოსტა აუცილებელია")
            .EmailAddress().WithMessage("ელ-ფოსტის ფორმატი არასწორია")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("პაროლი აუცილებელია")
            .MaximumLength(128);
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("ელ-ფოსტა აუცილებელია")
            .EmailAddress().WithMessage("ელ-ფოსტის ფორმატი არასწორია")
            .MaximumLength(256);
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("ელ-ფოსტა აუცილებელია")
            .EmailAddress().WithMessage("ელ-ფოსტის ფორმატი არასწორია")
            .MaximumLength(256);

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("ტოკენი აუცილებელია");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("ახალი პაროლი აუცილებელია")
            .MinimumLength(8).WithMessage("პაროლი მინიმუმ 8 სიმბოლო")
            .Matches("[A-Z]").WithMessage("პაროლი უნდა შეიცავდეს დიდ ასოს")
            .Matches("[a-z]").WithMessage("პაროლი უნდა შეიცავდეს პატარა ასოს")
            .Matches("[0-9]").WithMessage("პაროლი უნდა შეიცავდეს ციფრს");
    }
}

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("სახელი აუცილებელია")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("გვარი აუცილებელია")
            .MaximumLength(100);

        RuleFor(x => x.Phone)
            .MaximumLength(30)
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.AddressLine1)
            .MaximumLength(300)
            .When(x => !string.IsNullOrWhiteSpace(x.AddressLine1));

        RuleFor(x => x.City)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.City));
    }
}
