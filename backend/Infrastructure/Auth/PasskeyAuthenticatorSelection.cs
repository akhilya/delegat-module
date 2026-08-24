using System.Text.Json.Serialization;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// WebAuthn authenticatorSelection for discoverable passkeys (Bitwarden / 1Password / Windows Hello).
/// Do not set <see cref="AuthenticatorAttachment"/> to platform — roaming authenticators would be hidden.
/// </summary>
public sealed class PasskeyAuthenticatorSelection
{
    public string ResidentKey { get; init; } = "required";

    public bool RequireResidentKey { get; init; } = true;

    public string UserVerification { get; init; } = "preferred";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthenticatorAttachment { get; init; }

    public static PasskeyAuthenticatorSelection Discoverable() => new();
}
