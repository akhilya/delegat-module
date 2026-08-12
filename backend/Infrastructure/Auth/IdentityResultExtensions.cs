using Microsoft.AspNetCore.Identity;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Maps ASP.NET Identity failures to stable <see cref="ApiException"/> codes for the UI.
/// Field error values are API codes (not English descriptions) so the client can i18n them.
/// </summary>
public static class IdentityResultExtensions
{
    public static void ThrowIfFailed(this IdentityResult result)
    {
        if (result.Succeeded)
            return;

        throw ToApiException(result);
    }

    public static ApiException ToApiException(this IdentityResult result)
    {
        var fieldErrors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var messages = new List<string>();
        string? primaryCode = null;

        foreach (var error in result.Errors)
        {
            var (code, field) = Map(error.Code);
            primaryCode ??= code;
            messages.Add(string.IsNullOrWhiteSpace(error.Description) ? code : error.Description);

            if (!fieldErrors.TryGetValue(field, out var list))
            {
                list = new List<string>();
                fieldErrors[field] = list;
            }

            if (!list.Contains(code, StringComparer.Ordinal))
                list.Add(code);
        }

        return new ApiException(
            message: messages.Count > 0 ? string.Join("; ", messages) : "Bad request",
            statusCode: StatusCodes.Status400BadRequest,
            errors: fieldErrors.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToArray(),
                StringComparer.Ordinal),
            code: primaryCode ?? "bad_request");
    }

    private static (string Code, string Field) Map(string? identityCode) => identityCode switch
    {
        "InvalidUserName" => ("username_invalid", "userName"),
        "DuplicateUserName" => ("username_taken", "userName"),
        "PasswordTooShort" => ("password_too_short", "password"),
        "PasswordRequiresDigit" => ("password_requires_digit", "password"),
        "PasswordRequiresLower" => ("password_requires_lowercase", "password"),
        "PasswordRequiresUpper" or
            "PasswordRequiresNonAlphanumeric" or
            "PasswordRequiresUniqueChars" => ("password_policy", "password"),
        "PasswordMismatch" => ("password_mismatch", "password"),
        _ => ("bad_request", "userName"),
    };
}
