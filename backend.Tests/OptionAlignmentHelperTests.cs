using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace.Mapping;

namespace Delegat.Tests;

public class OptionAlignmentHelperTests
{
    [Fact]
    public void ParseAttributeOptions_NullOrBlank_ReturnsEmpty()
    {
        Assert.Empty(OptionAlignmentHelper.ParseAttributeOptions(null));
        Assert.Empty(OptionAlignmentHelper.ParseAttributeOptions("   "));
    }

    [Fact]
    public void ParseAttributeOptions_ObjectArray_UsesValueAndLabel()
    {
        var options = OptionAlignmentHelper.ParseAttributeOptions(
            """[{"value":"red","label":"Красный"},{"value":"blue","label":"Синий"}]""");

        Assert.Equal(2, options.Count);
        Assert.Equal(new OptionAlignmentHelper.ParsedOption("red", "Красный"), options[0]);
        Assert.Equal(new OptionAlignmentHelper.ParsedOption("blue", "Синий"), options[1]);
    }

    [Fact]
    public void ParseAttributeOptions_StringArray_UsesSameValueAndLabel()
    {
        var options = OptionAlignmentHelper.ParseAttributeOptions("""["S","M","L"]""");

        Assert.Equal(3, options.Count);
        Assert.All(options, o => Assert.Equal(o.Value, o.Label));
        Assert.Equal("M", options[1].Value);
    }

    [Fact]
    public void ParseAttributeOptions_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(OptionAlignmentHelper.ParseAttributeOptions("{not-json"));
    }

    [Fact]
    public void BuildValueMapEntries_MatchesByLabelWithDifferentKeys()
    {
        var local = new[]
        {
            new OptionAlignmentHelper.ParsedOption("1", "Красный"),
            new OptionAlignmentHelper.ParsedOption("2", "Синий"),
        };
        var target = new[]
        {
            new ExternalFieldOption("red", "Красный"),
            new ExternalFieldOption("blue", "Синий"),
        };

        var entries = OptionAlignmentHelper.BuildValueMapEntries(local, target);

        Assert.Equal(2, entries.Count);
        Assert.Equal("1", entries[0]["from"]);
        Assert.Equal("red", entries[0]["to"]);
        Assert.Equal("2", entries[1]["from"]);
        Assert.Equal("blue", entries[1]["to"]);
    }

    [Fact]
    public void BuildValueMapEntries_DoesNotReuseTargetOption()
    {
        var local = new[]
        {
            new OptionAlignmentHelper.ParsedOption("a", "Red"),
            new OptionAlignmentHelper.ParsedOption("b", "Red"),
        };
        var target = new[]
        {
            new ExternalFieldOption("red", "Red"),
        };

        var entries = OptionAlignmentHelper.BuildValueMapEntries(local, target);

        Assert.Single(entries);
        Assert.Equal("a", entries[0]["from"]);
        Assert.Equal("red", entries[0]["to"]);
    }

    [Fact]
    public void BuildValueMapEntries_PartialLabelOverlap_ScoreTooLow_Skips()
    {
        var local = new[] { new OptionAlignmentHelper.ParsedOption("1", "Красный металлик") };
        var target = new[] { new ExternalFieldOption("red", "Красный") };

        // Contains-match scores 70, threshold is 80
        Assert.Empty(OptionAlignmentHelper.BuildValueMapEntries(local, target));
    }

    [Fact]
    public void NeedsValueMap_DirectValueOverlap_ReturnsFalse()
    {
        var local = new[] { new OptionAlignmentHelper.ParsedOption("red", "Красный") };
        var target = new[] { new ExternalFieldOption("red", "Red") };

        Assert.False(OptionAlignmentHelper.NeedsValueMap(local, target));
    }

    [Fact]
    public void NeedsValueMap_LabelOverlapDifferentKeys_ReturnsTrue()
    {
        var local = new[] { new OptionAlignmentHelper.ParsedOption("1", "Красный") };
        var target = new[] { new ExternalFieldOption("red", "Красный") };

        Assert.True(OptionAlignmentHelper.NeedsValueMap(local, target));
    }

    [Fact]
    public void NeedsValueMap_EmptySides_ReturnsFalse()
    {
        Assert.False(OptionAlignmentHelper.NeedsValueMap([], [new ExternalFieldOption("a", "A")]));
        Assert.False(OptionAlignmentHelper.NeedsValueMap(
            [new OptionAlignmentHelper.ParsedOption("a", "A")], []));
    }

    [Fact]
    public void NormalizeValueMapMappings_ResolvesLabelsToCanonicalValues()
    {
        var local = new[] { new OptionAlignmentHelper.ParsedOption("1", "Красный") };
        var target = new[] { new ExternalFieldOption("red", "Red") };
        var mappings = new[]
        {
            new Dictionary<string, string> { ["from"] = "Красный", ["to"] = "Red" },
        };

        var normalized = OptionAlignmentHelper.NormalizeValueMapMappings(mappings, local, target);

        Assert.Single(normalized);
        Assert.Equal("1", normalized[0]["from"]);
        Assert.Equal("red", normalized[0]["to"]);
    }

    [Fact]
    public void NormalizeValueMapMappings_DropsBlankPairs()
    {
        var mappings = new[]
        {
            new Dictionary<string, string> { ["from"] = " ", ["to"] = "red" },
            new Dictionary<string, string> { ["from"] = "1", ["to"] = "" },
        };

        Assert.Empty(OptionAlignmentHelper.NormalizeValueMapMappings(mappings, [], []));
    }

    [Fact]
    public void MergeValueMapMappings_PreservesExistingAndAddsMissing()
    {
        var existing = new[]
        {
            new Dictionary<string, string> { ["from"] = "1", ["to"] = "user-chosen" },
        };
        var additions = new[]
        {
            new Dictionary<string, string> { ["from"] = "1", ["to"] = "auto-red" },
            new Dictionary<string, string> { ["from"] = "2", ["to"] = "blue" },
        };

        var merged = OptionAlignmentHelper.MergeValueMapMappings(existing, additions);

        Assert.Equal(2, merged.Count);
        Assert.Equal("user-chosen", merged[0]["to"]);
        Assert.Equal("2", merged[1]["from"]);
        Assert.Equal("blue", merged[1]["to"]);
    }

    [Fact]
    public void MergeValueMapMappings_NullExisting_UsesAdditionsOnly()
    {
        var additions = new[]
        {
            new Dictionary<string, string> { ["from"] = "1", ["to"] = "red" },
        };

        var merged = OptionAlignmentHelper.MergeValueMapMappings(null, additions);

        Assert.Single(merged);
        Assert.Equal("1", merged[0]["from"]);
    }
}
