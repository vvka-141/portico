using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Portico;

public sealed class Extensions_Should
{
    [Fact]
    public void JoinWithTheGivenSeparator()
    {
        Assert.Equal("a, b, c", new[] { "a", "b", "c" }.Join(", "));
    }

    [Fact]
    public void JoinWithACommaByDefault()
    {
        Assert.Equal("a,b", new[] { "a", "b" }.Join());
    }

    [Fact]
    public void JoinToEmpty_WhenSequenceIsEmpty()
    {
        Assert.Equal(string.Empty, Array.Empty<string>().Join(", "));
    }

    [Fact]
    public void ApplyTheActionToEveryElement()
    {
        var seen = new List<int>();

        new[] { 1, 2, 3 }.ForEach(seen.Add);

        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public void DequeueTheLeadingRun_AndLeaveTheRest()
    {
        var queue = new Queue<string>(["build", "release", "--verbose", "extra"]);

        var segments = queue.DequeueWhile(arg => !arg.StartsWith('-')).ToList();

        Assert.Equal(["build", "release"], segments);
        // The first non-matching element must remain — CliInvocation parses options from here.
        Assert.Equal(["--verbose", "extra"], queue);
    }

    [Fact]
    public void DequeueNothing_WhenTheHeadDoesNotMatch()
    {
        var queue = new Queue<string>(["--verbose", "build"]);

        Assert.Empty(queue.DequeueWhile(arg => !arg.StartsWith('-')));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void DequeueLazily()
    {
        var queue = new Queue<string>(["a", "b"]);

        var lazy = queue.DequeueWhile(_ => true);

        Assert.Equal(2, queue.Count);
        Assert.Equal(["a", "b"], lazy.ToList());
        Assert.Empty(queue);
    }

    [Fact]
    public void QuoteInDoubleQuotes()
    {
        Assert.Equal("\"hello world\"", "hello world".Quote());
    }

    [Fact]
    public void QuoteIdempotently()
    {
        Assert.Equal("\"already\"", "\"already\"".Quote());
    }

    [Theory]
    [InlineData("hello world", true)]
    [InlineData("tab\there", true)]
    [InlineData("hello", false)]
    [InlineData("", false)]
    public void DetectWhiteSpace(string value, bool expected)
    {
        Assert.Equal(expected, value.HasWhiteSpaces());
    }

    [Fact]
    public void QuoteOnlyWhatNeedsIt()
    {
        // The pairing CliInvocation.ToString relies on: quote a segment iff a shell would need it.
        string[] segments = ["myapp", "build", "my project"];

        var line = segments
            .Select(s => s.HasWhiteSpaces() ? s.Quote() : s)
            .Join(" ");

        Assert.Equal("myapp build \"my project\"", line);
    }
}
