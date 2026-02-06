namespace PrintO.Marketplace.SDK;

/// <summary>
/// Schema for connection credentials fields
/// </summary>
public class CredentialsSchema
{
    /// <summary>
    /// List of credential fields
    /// </summary>
    public List<CredentialField> Fields { get; set; } = new();
}

/// <summary>
/// Definition of a credential input field
/// </summary>
public class CredentialField
{
    /// <summary>
    /// Field key (e.g. "apiKey", "clientId")
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Description/hint
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Field type: "text", "password", "number", "select"
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// Is the field required
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Hide value (for passwords/tokens)
    /// </summary>
    public bool IsSecret { get; set; } = false;

    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Options for select type
    /// </summary>
    public List<SelectOption>? Options { get; set; }

    /// <summary>
    /// Placeholder text
    /// </summary>
    public string? Placeholder { get; set; }
}

/// <summary>
/// Option for dropdown/select fields
/// </summary>
public class SelectOption
{
    /// <summary>
    /// Option value submitted to the backend.
    /// </summary>
    public string Value { get; set; } = null!;

    /// <summary>
    /// Display label shown in the UI.
    /// </summary>
    public string Label { get; set; } = null!;
}
