using System;
using System.Collections.Generic;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-17. A failed scalar conversion said "The value 'abc' is invalid" — it named the value but not
// the option that rejected it. On a command with several numeric options the user cannot tell which
// one to fix. The map and collection paths already named the option; scalar was the odd one out.
//
// It also leaked "(Parameter 'value')" — the ArgumentException.ParamName of a local in
// CliOptionMaterializer. `value` is not a name the user typed or can act on.
public sealed class CliConversionError_Should
{
    public enum Mode { Fast, Safe }

    public interface ITool
    {
        [CliRoute("pay")]
        [CliCommandExample("pay --amount 1.5")]
        int Pay([CliOption("--amount")] decimal amount, [CliOption("--fee")] decimal fee = 0m);

        [CliRoute("run")]
        [CliCommandExample("run --mode fast")]
        int Run([CliOption("--mode")] Mode mode);

        [CliRoute("tag")]
        [CliCommandExample("tag --port 8080")]
        int Tag([CliOption("--port")] int[] ports);

        [CliRoute("map")]
        [CliCommandExample("map --limit[cpu] 4")]
        int Map([CliOption("--limit")] Dictionary<string, int> limits);

        [CliRoute("login")]
        [CliCommandExample("login --retries 3")]
        int Login([CliOption("--retries", Sensitive = true)] int retries);
    }

    private sealed class Tool : ITool
    {
        public int Pay(decimal amount, decimal fee) => 0;
        public int Run(Mode mode) => 0;
        public int Tag(int[] ports) => 0;
        public int Map(Dictionary<string, int> limits) => 0;
        public int Login(int retries) => 0;
    }

    private static CliTestRunResult Run(string commandLine) => CliTestHarness
        .ForApplication(cfg => cfg.AddCommands(new Tool()))
        .Run(commandLine);

    [Fact]
    public void Name_The_Scalar_Option_That_Failed()
    {
        var result = Run("app pay --amount abc");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("--amount", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'abc'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_Leak_The_Internal_Parameter_Name()
    {
        var result = Run("app pay --amount abc");

        // The converter's diagnosis survives; its ParamName does not.
        Assert.DoesNotContain("Parameter '", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Decimal", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_The_Option_On_A_Failed_Enum_Conversion()
    {
        var result = Run("app run --mode sideways");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("--mode", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Fast", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Safe", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_The_Option_On_A_Failed_Collection_Item()
    {
        var result = Run("app tag --port 80 http");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("--port", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter '", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Name_The_Option_And_Key_On_A_Failed_Map_Value()
    {
        var result = Run("app map --limit[cpu] lots");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("--limit", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("cpu", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter '", result.StandardError, StringComparison.Ordinal);
    }

    // Naming the option must not become an excuse to start echoing its value. A Sensitive option
    // reports WHICH option failed and nothing about WHAT was passed — not the value, and not the
    // converter's explanation, which embeds the value too ("'hunter2' is not a valid value...").
    [Fact]
    public void Name_A_Sensitive_Option_Without_Echoing_Its_Value()
    {
        var result = Run("app login --retries hunter2");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("--retries", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
    }

    // POR-116. The POR-17 fix stripped "(Parameter 'value')" from option errors but the argument
    // path used raw e.Message and still leaked the internal parameter name.
    public interface IArgTool
    {
        [CliRoute("scale {n}")]
        [CliCommandExample("scale 5")]
        int Scale([CliArgument("replica count")] int n);
    }

    private sealed class ArgTool : IArgTool
    {
        public int Scale(int n) => 0;
    }

    [Fact]
    public void Not_Leak_The_Internal_Parameter_Name_For_Arguments()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new ArgTool()))
            .Run("app scale abc");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("N", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'abc'", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter '", result.StandardError, StringComparison.Ordinal);
    }
}
