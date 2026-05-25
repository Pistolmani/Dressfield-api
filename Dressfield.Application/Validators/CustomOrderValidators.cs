using Dressfield.Application.DTOs;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Dressfield.Application.Validators;

/// <summary>
/// Configures which hosts are allowed for uploaded design image URLs.
/// Populated from <c>AzureStorage:AllowedUploadHosts</c> in appsettings.
/// </summary>
public class UploadHostOptions
{
    public string[] AllowedHosts { get; set; } = [];
}

public class CreateCustomOrderRequestValidator : AbstractValidator<CreateCustomOrderRequest>
{
    public CreateCustomOrderRequestValidator(IOptions<UploadHostOptions> uploadHostOptions)
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

        RuleFor(x => x.CustomerNotes)
            .MaximumLength(1000).WithMessage("შენიშვნა მაქსიმუმ 1000 სიმბოლოა");

        RuleFor(x => x.Designs)
            .NotEmpty().WithMessage("მინიმუმ ერთი დიზაინი საჭიროა");

        var allowedHosts = uploadHostOptions.Value.AllowedHosts;
        RuleForEach(x => x.Designs).SetValidator(new CreateCustomOrderDesignRequestValidator(allowedHosts));
    }
}

public class CreateCustomOrderDesignRequestValidator : AbstractValidator<CreateCustomOrderDesignRequest>
{
    public CreateCustomOrderDesignRequestValidator(string[] allowedHosts)
    {
        RuleFor(x => x.DesignImageUrl)
            .NotEmpty().WithMessage("დიზაინის სურათი აუცილებელია")
            .MaximumLength(500).WithMessage("სურათის URL მაქსიმუმ 500 სიმბოლოა")
            .Must(url => IsAllowedUploadUrl(url, allowedHosts))
            .WithMessage("დიზაინის სურათი უნდა იყოს ჩვენი სერვერიდან ატვირთული ფაილი");

        RuleFor(x => x.Placement)
            .MaximumLength(50).WithMessage("განთავსება მაქსიმუმ 50 სიმბოლოა");

        RuleFor(x => x.Size)
            .MaximumLength(20).WithMessage("ზომა მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.ThreadColor)
            .MaximumLength(20).WithMessage("ძაფის ფერი მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("რიგითობა ვერ იქნება უარყოფითი");
    }

    private static bool IsAllowedUploadUrl(string? url, string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Fail closed: require an explicit allowlist. Any environment lacking AzureStorage:AllowedUploadHosts
        // rejects every design URL — better than silently accepting arbitrary HTTPS resources.
        if (allowedHosts.Length == 0)
            return false;

        return allowedHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
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
