using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests.Graph;

public class MappingFieldCoercionTests
{
    public static IEnumerable<object?[]> CoerceCases()
    {
        yield return new object?[] { "null-type", null, "hi", "hi", typeof(string) };
        yield return new object?[] { "empty-type", "", "hi", "hi", typeof(string) };
        yield return new object?[] { "null-value", "number", null, null, null };

        yield return new object?[] { "string-int", "string", 12, "12", typeof(string) };
        yield return new object?[] { "text-decimal", "text", 12.5m, 12.5m.ToString(), typeof(string) };
        yield return new object?[] { "string-bool", "string", true, true.ToString(), typeof(string) };

        yield return new object?[] { "number-str", "number", "12.5", 12.5m, typeof(decimal) };
        yield return new object?[] { "number-dec", "number", 12.5m, 12.5m, typeof(decimal) };
        yield return new object?[] { "number-int", "number", 7, 7m, typeof(decimal) };
        yield return new object?[] { "number-garbage", "number", "abc", "abc", typeof(string) };

        yield return new object?[] { "int-long", "integer", 5L, 5L, typeof(long) };
        yield return new object?[] { "int-int32", "integer", 5, 5L, typeof(long) };
        yield return new object?[] { "int-trunc-string", "integer", "123.9", 123L, typeof(long) };
        yield return new object?[] { "int-str", "integer", "42", 42L, typeof(long) };
        yield return new object?[] { "int-garbage", "integer", "nope", "nope", typeof(string) };

        yield return new object?[] { "bool-true", "boolean", true, true, typeof(bool) };
        yield return new object?[] { "bool-1", "boolean", "1", true, typeof(bool) };
        yield return new object?[] { "bool-yes", "boolean", "yes", true, typeof(bool) };
        yield return new object?[] { "bool-da", "boolean", "да", true, typeof(bool) };
        yield return new object?[] { "bool-false", "boolean", "false", false, typeof(bool) };
        yield return new object?[] { "bool-0", "boolean", "0", false, typeof(bool) };
        yield return new object?[] { "bool-no", "boolean", "NO", false, typeof(bool) };
        yield return new object?[] { "bool-net", "boolean", "нет", false, typeof(bool) };
        yield return new object?[] { "bool-maybe", "boolean", "maybe", "maybe", typeof(string) };

        yield return new object?[] { "select-keep", "select", "red", "red", typeof(string) };
        yield return new object?[] { "array-keep", "array", "x", "x", typeof(string) };
        yield return new object?[] { "multiselect-keep", "multiselect", "a,b", "a,b", typeof(string) };
    }

    [Theory]
    [MemberData(nameof(CoerceCases))]
    public void Coerce_MatchesMatrix(
        string id,
        string? fieldType,
        object? input,
        object? expected,
        object? expectedTypeOrIgnored)
    {
        _ = id;
        var actual = MappingFieldCoercion.Coerce(input, fieldType);
        GraphAssert.Equal(expected, actual);
        if (expectedTypeOrIgnored is Type t && actual != null)
            Assert.IsType(t, actual);
    }

    [Fact]
    public void Coerce_String_PassesThroughNonStringEnumerable()
    {
        var list = new List<string> { "a", "b" };
        var actual = MappingFieldCoercion.Coerce(list, "string");
        Assert.Same(list, actual);
        Assert.Same(list, MappingFieldCoercion.Coerce(list, "text"));
    }

    [Fact]
    public void Coerce_String_DoesNotTreatStringAsEnumerable()
    {
        var actual = MappingFieldCoercion.Coerce("abc", "string");
        Assert.Equal("abc", actual);
        Assert.IsType<string>(actual);
    }

    [Fact]
    public void Coerce_Number_UsesInvariantCulture()
    {
        var actual = MappingFieldCoercion.Coerce("1,234.5", "number");
        GraphAssert.Equal(1234.5m, actual);
    }

    [Fact]
    public void Coerce_Integer_DecimalObject_GoesThroughCurrentCultureToString()
    {
        var actual = MappingFieldCoercion.Coerce(12.9m, "integer");
        GraphAssert.Equal(MappingFieldCoercion.Coerce(12.9m.ToString(), "integer"), actual);
    }
}
