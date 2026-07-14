using System;
using System.Threading;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliCancellationTokenTypeConverter_Should
{
    private static CancellationToken Convert(string value)
    {
        var converter = new CliCancellationTokenTypeConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        var result = converter.ConvertFromInvariantString(value);
        return Assert.IsType<CancellationToken>(result);
    }

    [Theory]
    [InlineData("30s")]
    [InlineData("5m")]
    [InlineData("2h")]
    [InlineData("1d")]
    [InlineData("1.5h")]
    public void Parse_Compact_Forms(string input)
    {
        var token = Convert(input);
        Assert.True(token.CanBeCanceled);
    }

    [Theory]
    [InlineData("00:00:30")]          // TimeSpan
    [InlineData("0:00:30")]           // TimeSpan
    [InlineData("PT30S")]             // ISO 8601
    [InlineData("30 seconds")]        // human-readable
    public void Parse_Standard_Forms(string input)
    {
        var token = Convert(input);
        Assert.True(token.CanBeCanceled);
    }

    [Fact]
    public void Compact_Form_Produces_Cancellable_Token_With_Expected_Fire()
    {
        // "1s" fires within ~1 second; wait slightly longer then assert cancellation.
        var token = Convert("1s");
        Assert.True(token.CanBeCanceled);
        Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(3)));
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose_Timeout_Source_When_Invocation_Scope_Completes()
    {
        var converter = new CliCancellationTokenTypeConverter();
        CancellationToken token;
        using (CliInvocationDisposalScope.Begin())
        {
            // A day-long timeout would otherwise leave a Timer scheduled for the full window.
            token = Assert.IsType<CancellationToken>(converter.ConvertFromInvariantString("1d"));
            Assert.True(token.CanBeCanceled);
            // Inside the scope the source is alive — its wait handle is accessible.
            Assert.NotNull(token.WaitHandle);
        }

        // Scope disposal disposed the underlying CancellationTokenSource (and its day-long timer)
        // even though the timeout never elapsed: touching the token's wait handle now throws.
        Assert.Throws<ObjectDisposedException>(() => token.WaitHandle);
    }
}
