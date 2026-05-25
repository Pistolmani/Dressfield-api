namespace Dressfield.Infrastructure.Services;

internal static class LogSanitizer
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "<empty>";

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
            return "<malformed>";

        var first = email[0];
        return $"{first}***{email[at..]}";
    }
}
