namespace Delegat.Tests.Graph;

public class GraphEvaluatorOpTests
{
    [Theory]
    [InlineData("{0}-{1}", "A", "B", "A-B")]
    [InlineData("{1}/{0}", "A", "B", "B/A")]
    [InlineData("", "A", "B", "AB")]
    public void FormatString_SubstitutesOrConcatenates(string template, string a, string b, string expected)
    {
        var result = new GraphEvalHarness()
            .Static("a", a)
            .Static("b", b)
            .Op("op", "formatString", d => d.template = template)
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "string");
    }

    [Fact]
    public void FormatString_SortsHandlesByTrailingIndex()
    {
        var result = new GraphEvalHarness()
            .Static("a", "A")
            .Static("b", "B")
            .Op("op", "formatString", d => d.template = "{0}{1}")
            .Edge("b", "op", "in-1")
            .Edge("a", "op", "in-0")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", "AB", "string");
    }

    [Theory]
    [InlineData("x", "1", "2", "1x2")]
    [InlineData("", "1", "2", "12")]
    [InlineData("-", "1", "", "1")]
    public void Join_SkipsEmptyParts(string delim, string a, string b, string expected)
    {
        var result = new GraphEvalHarness()
            .Static("a", a)
            .Static("b", b)
            .Op("op", "join", d => d.delimiter = delim)
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "string");
    }

    [Fact]
    public void MakeArray_DropsNullsAndStringifies()
    {
        var g = new GraphEvalHarness();
        g.ProductFields["n"] = 12.5m;
        var result = g
            .Static("a", "x")
            .ProductField("n", "n")
            .Op("op", "makeArray")
            .Edge("a", "op", "in-0")
            .Edge("n", "op", "in-1")
            .Field("out", "array")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", new[] { "x", "12.5" }, "array");
    }

    [Fact]
    public void ArrayAppend_AppendsStringifiedItem()
    {
        var result = new GraphEvalHarness()
            .Static("list", "[\"a\"]", "array")
            .Static("item", "b")
            .Op("op", "arrayAppend")
            .Edge("list", "op", "in-0")
            .Edge("item", "op", "in-1")
            .Field("out", "array")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", new[] { "a", "b" }, "array");
    }

    [Theory]
    [InlineData(",", "a, b, c", new[] { "a", "b", "c" })]
    [InlineData("|", "a|b", new[] { "a", "b" })]
    [InlineData(",", "", new string[0])]
    public void SplitToArray_SplitsAndTrims(string delim, string input, string[] expected)
    {
        var result = new GraphEvalHarness()
            .Static("src", input)
            .Op("op", "splitToArray", d => d.delimiter = delim)
            .Edge("src", "op")
            .Field("out", "array")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "array");
    }

    [Theory]
    [InlineData(0, "a")]
    [InlineData(1, "b")]
    [InlineData(5, null)]
    [InlineData(-1, null)]
    public void ArrayIndex_ReturnsElementOrNull(int index, string? expected)
    {
        var result = new GraphEvalHarness()
            .Static("src", "[\"a\",\"b\"]", "array")
            .Op("op", "arrayIndex", d => d.index = index)
            .Edge("src", "op")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "string");
    }

    [Fact]
    public void ArrayLength_StringIsOneElementNotChars()
    {
        var result = new GraphEvalHarness()
            .Static("src", "abc")
            .Op("op", "arrayLength")
            .Edge("src", "op")
            .Field("out", "number")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", 1m, "number");
    }

    [Fact]
    public void ArrayLength_ListCount()
    {
        var result = new GraphEvalHarness()
            .Static("src", "[\"a\",\"b\",\"c\"]", "array")
            .Op("op", "arrayLength")
            .Edge("src", "op")
            .Field("out", "integer")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", 3L, "integer");
    }

    [Theory]
    [InlineData("true", "yes", "no", "yes")]
    [InlineData("false", "yes", "no", "no")]
    [InlineData("0", "yes", "no", "no")]
    [InlineData("нет", "yes", "no", "no")]
    [InlineData("hello", "yes", "no", "yes")]
    public void IfElse_UsesTruthiness(string cond, string thenVal, string elseVal, string expected)
    {
        var result = new GraphEvalHarness()
            .Static("c", cond)
            .Static("t", thenVal)
            .Static("e", elseVal)
            .Op("op", "ifElse")
            .Edge("c", "op", "in-0")
            .Edge("t", "op", "in-1")
            .Edge("e", "op", "in-2")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "string");
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("false", true)]
    [InlineData("0", true)]
    [InlineData("1", false)]
    [InlineData("", true)]
    public void Not_InvertsTruthiness(string input, bool expected)
    {
        var result = new GraphEvalHarness()
            .Static("src", input)
            .Op("op", "not")
            .Edge("src", "op")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "boolean");
    }

    [Fact]
    public void And_RequiresAllTruthy_OrEmptyIsFalse()
    {
        var both = EvalLogic("and", "true", "1");
        GraphAssert.Output(both, "out", true, "boolean");
        var one = EvalLogic("and", "true", "0");
        GraphAssert.Output(one, "out", false, "boolean");
        var empty = new GraphEvalHarness()
            .Op("op", "and")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();
        GraphAssert.Output(empty, "out", false, "boolean");
    }

    [Fact]
    public void Or_AnyTruthy()
    {
        GraphAssert.Output(EvalLogic("or", "0", "yes"), "out", true, "boolean");
        GraphAssert.Output(EvalLogic("or", "0", "false"), "out", false, "boolean");
    }

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("12.50", "12.5", true)]
    [InlineData("abc", "ABC", false)]
    public void Equals_NumericWhenBothParseElseOrdinalString(string a, string b, bool expected)
    {
        var result = EvalLogic("equals", a, b);
        GraphAssert.Output(result, "out", expected, "boolean");
    }

    [Fact]
    public void Equals_StringOneVsIntOne()
    {
        var g = new GraphEvalHarness();
        g.ProductFields["n"] = 1;
        var result = g
            .Static("a", "1")
            .ProductField("b", "n")
            .Op("op", "equals")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }

    [Fact]
    public void NotEquals_NegatesEquals()
    {
        GraphAssert.Output(EvalLogic("notEquals", "1", "2"), "out", true, "boolean");
    }

    [Theory]
    [InlineData("greaterThan", "5", "2", true)]
    [InlineData("greaterThan", "2", "5", false)]
    [InlineData("greaterThan", "x", "2", false)]
    [InlineData("lessThan", "2", "5", true)]
    [InlineData("lessThan", "5", "2", false)]
    public void Compare_RequiresBothNumeric(string op, string a, string b, bool expected)
    {
        GraphAssert.Output(EvalLogic(op, a, b), "out", expected, "boolean");
    }

    [Theory]
    [InlineData("Hello", "ell", true)]
    [InlineData("Hello", "ELL", true)]
    [InlineData("Hello", "z", false)]
    public void Contains_SubstringIgnoreCase(string hay, string needle, bool expected)
    {
        GraphAssert.Output(EvalLogic("contains", hay, needle), "out", expected, "boolean");
    }

    [Fact]
    public void Contains_ListMembershipIsOrdinal()
    {
        var result = new GraphEvalHarness()
            .Static("hay", "[\"Red\",\"Blue\"]", "array")
            .Static("n", "Red")
            .Op("op", "contains")
            .Edge("hay", "op", "in-0")
            .Edge("n", "op", "in-1")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("x", false)]
    [InlineData("0", false)]
    public void IsEmpty_NullWhitespaceOrEmptyCollection(string? input, bool expected)
    {
        var g = new GraphEvalHarness();
        if (input is null)
        {
            g.ProductFields["v"] = null;
            g.ProductField("src", "v");
        }
        else
        {
            g.Static("src", input);
        }

        var result = g
            .Op("op", "isEmpty")
            .Edge("src", "op")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "boolean");
    }

    [Fact]
    public void IsEmpty_EmptyMakeArray_IsTrue()
    {
        var result = new GraphEvalHarness()
            .Op("arr", "makeArray")
            .Op("op", "isEmpty")
            .Edge("arr", "op")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }

    [Fact]
    public void IsValid_NegatesIsEmpty()
    {
        var result = new GraphEvalHarness()
            .Static("src", "x")
            .Op("op", "isValid")
            .Edge("src", "op")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }

    [Fact]
    public void MissingWires_OpsDoNotThrow()
    {
        foreach (var op in new[]
                 {
                     "formatString", "join", "makeArray", "arrayAppend", "splitToArray",
                     "arrayIndex", "arrayLength", "ifElse", "not", "and", "or", "equals",
                     "notEquals", "greaterThan", "lessThan", "contains", "isEmpty", "isValid",
                 })
        {
            var result = new GraphEvalHarness()
                .Op("op", op)
                .Field("out", "string")
                .WireToField("op", "out")
                .Eval();

            Assert.True(result.Outputs.ContainsKey("out"), op);
        }
    }

    [Theory]
    [InlineData("add", "10", 15, "25")]
    [InlineData("subtract", "10", 15, "-5")]
    [InlineData("multiply", "10", 15, "150")]
    [InlineData("divide", "30", 15, "2")]
    public void Arithmetic_AppliesOperand(string op, string input, int operand, string expected)
    {
        var result = new GraphEvalHarness()
            .Static("a", input)
            .Op("op", op, d => d.operand = operand)
            .Edge("a", "op")
            .Field("out", "number")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "number");
    }

    [Fact]
    public void Divide_ByZeroYieldsNull()
    {
        var result = new GraphEvalHarness()
            .Static("a", "30")
            .Op("op", "divide", d => d.operand = 0)
            .Edge("a", "op")
            .Field("out", "number")
            .WireToField("op", "out")
            .Eval();

        Assert.False(result.Outputs.TryGetValue("out", out var v) && v != null);
    }

    [Theory]
    [InlineData("min", "10", "4", "7", "4")]
    [InlineData("max", "10", "4", "7", "10")]
    [InlineData("min", "3", "nope", "8", "3")]
    public void MinMax_PicksNumericExtreme(string op, string a, string b, string c, string expected)
    {
        var result = new GraphEvalHarness()
            .Static("a", a)
            .Static("b", b)
            .Static("c", c)
            .Op("op", op)
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Edge("c", "op", "in-2")
            .Field("out", "number")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", expected, "number");
    }

    [Fact]
    public void IfElse_GappedInputIndexKeepsBranchPositions()
    {
        // Wire only the condition (in-0) and the else-branch (in-2): the missing
        // true-branch must stay empty instead of shifting the else value up.
        var result = new GraphEvalHarness()
            .Static("cond", "true")
            .Static("alt", "else-value")
            .Op("op", "ifElse")
            .Edge("cond", "op", "in-0")
            .Edge("alt", "op", "in-2")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        Assert.False(result.Outputs.TryGetValue("out", out var v) && v != null);
    }

    [Fact]
    public void And_GappedMiddlePinStillAnds()
    {
        // Wires on in-0 and in-2 with in-1 unwired: the gap must not count as a false operand.
        var result = new GraphEvalHarness()
            .Static("a", "1")
            .Static("b", "yes")
            .Op("op", "and")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-2")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }

    private static Delegat.Services.Marketplace.Graph.GraphEvalResult EvalLogic(
        string op,
        string a,
        string b)
        => new GraphEvalHarness()
            .Static("a", a)
            .Static("b", b)
            .Op("op", op)
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();
}
