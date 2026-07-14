using System;
using System.IO;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class ICliConsole_Should
{
    [Fact]
    public void Default_IsOutputRedirected_To_False_On_Custom_Implementations()
    {
        ICliConsole custom = new MinimalConsole();
        Assert.False(custom.IsOutputRedirected);
    }

    [Fact]
    public void Default_IsColorEnabled_To_False_On_Custom_Implementations()
    {
        // Custom consoles are opt-in-to-color — default is safe (no styling).
        ICliConsole custom = new MinimalConsole();
        Assert.False(custom.IsColorEnabled);
    }

    [Fact]
    public void SystemCliConsole_Respects_NO_COLOR_Environment()
    {
        // This test doesn't assert a specific value because the test host's redirection/TTY
        // state isn't deterministic — but it verifies that setting NO_COLOR forces color off
        // regardless of TTY state.
        var original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            Assert.False(SystemCliConsole.Instance.IsColorEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    [Fact]
    public void Custom_Implementation_Can_Override_IsColorEnabled()
    {
        ICliConsole colorful = new ColorfulTestConsole();
        Assert.True(colorful.IsColorEnabled);
    }

    private sealed class MinimalConsole : ICliConsole
    {
        public TextWriter Out => TextWriter.Null;
        public TextWriter Error => TextWriter.Null;
        public TextReader In => TextReader.Null;
    }

    private sealed class ColorfulTestConsole : ICliConsole
    {
        public TextWriter Out => TextWriter.Null;
        public TextWriter Error => TextWriter.Null;
        public TextReader In => TextReader.Null;
        public bool IsColorEnabled => true;
    }
}
