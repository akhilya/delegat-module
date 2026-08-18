using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Normalizes marketplace <see cref="ExternalField"/> limits onto local category attributes.
/// </summary>
internal static class AttributeFieldConstraints
{
    /// <summary>
    /// Copy length / range / regex limits from an adapter field onto a new attribute form.
    /// </summary>
    public static void ApplyTo(ref CategoryAttribute.AddForm form, ExternalField field)
    {
        form.maxLength = NormalizeMaxLength(field.MaxLength);
        (form.minValue, form.maxValue) = NormalizeRange(field.MinValue, field.MaxValue);
        form.maxValues = NormalizeMaxValues(field.MaxValues);
        form.validationPattern = UsableValidationPattern(field.ValidationPattern);
        form.allowCustomValues = field.AllowCustomValues;
        if (form.maxLength is int maxLen)
        {
            form.defaultValue = StringClip.ClipOptional(form.defaultValue, maxLen);
        }
    }

    /// <summary>
    /// Fill empty constraint columns on an existing attribute (re-import). Does not overwrite user edits.
    /// </summary>
    public static bool ApplyIfEmpty(CategoryAttribute attribute, ExternalField field)
    {
        var maxLength = NormalizeMaxLength(field.MaxLength);
        var (minValue, maxValue) = NormalizeRange(field.MinValue, field.MaxValue);
        var maxValues = NormalizeMaxValues(field.MaxValues);
        var pattern = UsableValidationPattern(field.ValidationPattern);
        var changed = false;

        if (attribute.maxLength == null && maxLength != null)
        {
            attribute.maxLength = maxLength;
            changed = true;
        }

        if (attribute.minValue == null && minValue != null)
        {
            attribute.minValue = minValue;
            changed = true;
        }

        if (attribute.maxValue == null && maxValue != null)
        {
            attribute.maxValue = maxValue;
            changed = true;
        }

        if (attribute.maxValues == null && maxValues != null)
        {
            attribute.maxValues = maxValues;
            changed = true;
        }

        if (string.IsNullOrEmpty(attribute.validationPattern) && pattern != null)
        {
            attribute.validationPattern = pattern;
            changed = true;
        }

        if (!attribute.allowCustomValues && field.AllowCustomValues)
        {
            attribute.allowCustomValues = true;
            changed = true;
        }

        return changed;
    }

    internal static int? NormalizeMaxLength(int? maxLength)
    {
        if (maxLength is null or <= 0)
            return null;
        return Math.Min(maxLength.Value, ProductAttributeValue.VALUE_MAX_LENGTH);
    }

    /// <summary>
    /// Marketplace 0 / missing means unlimited. Caps runaway adapter values.
    /// </summary>
    internal const int MaxValuesCap = 10_000;

    internal static int? NormalizeMaxValues(int? maxValues)
    {
        if (maxValues is null or <= 0)
            return null;
        return Math.Min(maxValues.Value, MaxValuesCap);
    }

    internal static (decimal? Min, decimal? Max) NormalizeRange(decimal? minValue, decimal? maxValue)
    {
        if (minValue != null && maxValue != null && minValue > maxValue)
            return (null, null);
        return (minValue, maxValue);
    }

    /// <summary>
    /// Yandex encodes a default unit id as <c>unitId=123</c> on <see cref="ExternalField.ValidationPattern"/>.
    /// That is not a value regex and must not be copied onto the local attribute.
    /// </summary>
    internal static string? UsableValidationPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;
        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("unitId=", StringComparison.OrdinalIgnoreCase))
            return null;
        return StringClip.ClipOptional(trimmed, CategoryAttribute.ATTRIBUTE_VALIDATION_PATTERN_MAX_LENGTH);
    }
}
