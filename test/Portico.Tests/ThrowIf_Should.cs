using System;
using Xunit;

namespace Portico;

public sealed class ThrowIf_Should
{
    [Fact]
    public void ReturnTheValue_WhenArgumentIsNotNull()
    {
        var console = new object();
        Assert.Same(console, ThrowIf.ArgumentNull(console));
    }

    [Fact]
    public void ThrowArgumentNull_WhenArgumentIsNull()
    {
        object? console = null;

        var exception = Assert.Throws<ArgumentNullException>(() => ThrowIf.ArgumentNull(console));

        Assert.Equal("console", exception.ParamName);
    }

    [Fact]
    public void PhraseTheMessageByExpressionShape()
    {
        object? console = null;
        var box = new Box();

        // A bare identifier is the subject of the sentence; a member access "returned" null.
        var identifier = Assert.Throws<ArgumentNullException>(() => ThrowIf.ArgumentNull(console));
        var expression = Assert.Throws<ArgumentNullException>(() => ThrowIf.ArgumentNull(box.Value));

        Assert.Contains("console is null.", identifier.Message, StringComparison.Ordinal);
        Assert.Contains("box.Value returned null.", expression.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HonorAnExplicitMessage()
    {
        object? console = null;

        var exception = Assert.Throws<ArgumentNullException>(
            () => ThrowIf.ArgumentNull(console, "The console is required."));

        Assert.Contains("The console is required.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnTheValue_WhenStringHasContent()
    {
        Assert.Equal("build", ThrowIf.ArgumentNullOrWhiteSpace("build"));
    }

    [Fact]
    public void ThrowArgumentNull_WhenStringIsNull()
    {
        string? name = null;

        var exception = Assert.Throws<ArgumentNullException>(() => ThrowIf.ArgumentNullOrWhiteSpace(name));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void ThrowArgumentOutOfRange_WhenStringIsEmptyOrWhitespace(string value)
    {
        // Null and blank are different failures: blank is a range violation, not a missing argument.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ThrowIf.ArgumentNullOrWhiteSpace(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void NotThrow_WhenConditionIsTrue()
    {
        ThrowIf.False(true);
    }

    [Fact]
    public void ThrowInvalidOperation_WhenConditionIsFalse()
    {
        // ThrowIf.False asserts the condition holds — it throws on false, despite the name
        // reading like the opposite to a first-time reader.
        var routes = Array.Empty<string>();

        var exception = Assert.Throws<InvalidOperationException>(() => ThrowIf.False(routes.Length > 0));

        Assert.Contains("evaluated to false", exception.Message, StringComparison.Ordinal);
    }

    private sealed class Box
    {
        public object? Value => null;
    }
}
