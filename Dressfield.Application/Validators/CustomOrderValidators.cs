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

        RuleFor(x => x.TotalPrice)
            .GreaterThan(0).WithMessage("ფასი არასწორია");

        RuleFor(x => x.ProductTypeId)
            .MaximumLength(50).WithMessage("პროდუქტის ტიპი მაქსიმუმ 50 სიმბოლოა");

        RuleFor(x => x.ColorHex)
            .MaximumLength(20).WithMessage("ფერი მაქსიმუმ 20 სიმბოლოა");

        RuleFor(x => x.ClothingSize)
            .MaximumLength(20).WithMessage("ზომა მაქსიმუმ 20 სიმბოლოა");

        // Guard against absurd canvas sizes that could break the admin preview math.
        RuleFor(x => x.CanvasWidth)
            .InclusiveBetween(1, 10_000).When(x => x.CanvasWidth.HasValue)
            .WithMessage("ტილოს სიგანე არასწორია");

        RuleFor(x => x.CanvasHeight)
            .InclusiveBetween(1, 10_000).When(x => x.CanvasHeight.HasValue)
            .WithMessage("ტილოს სიმაღლე არასწორია");

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

        RuleFor(x => x.Side)
            .MaximumLength(20).WithMessage("მხარე მაქსიმუმ 20 სიმბოლოა");

        // Canvas geometry - reject anything that's clearly a bug (negatives, NaN-as-default).
        // Generous upper bound: canvases are typically <=1000px but we don't want false positives.
        RuleFor(x => x.PositionX).GreaterThanOrEqualTo(0).When(x => x.PositionX.HasValue);
        RuleFor(x => x.PositionY).GreaterThanOrEqualTo(0).When(x => x.PositionY.HasValue);
        RuleFor(x => x.Width).GreaterThan(0).When(x => x.Width.HasValue);
        RuleFor(x => x.Height).GreaterThan(0).When(x => x.Height.HasValue);
        RuleFor(x => x.ScaleX).GreaterThan(0).When(x => x.ScaleX.HasValue);
        RuleFor(x => x.ScaleY).GreaterThan(0).When(x => x.ScaleY.HasValue);
        RuleFor(x => x.Angle)
            .InclusiveBetween(-360, 360).When(x => x.Angle.HasValue)
            .WithMessage("კუთხე -360°...360° შუალედშია");

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
        // rejects every design URL - better than silently accepting arbitrary HTTPS resources.
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
