using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-29. Examples-are-tests is the wedge, so a failing example has to say WHY. The framework always
// knew — it wrote the reason to the console — but the validator threw it away and the user got a red
// test saying only "didn't dispatch". With twenty examples on a contract, that sends them hunting for
// something the framework could simply have handed them.
public sealed class CliContractFailureReason_Should
{
    public interface IPaymentContract
    {
        [CliRoute("pay")]
        [CliCommandExample("pay --amount 10.5")]         // good
        [CliCommandExample("pay --amount abc")]          // bad: 'abc' is not a decimal
        [CliCommandExample("pay --amount 1 --bogus")]    // bad: unknown option
        int Pay([CliOption("--amount")] decimal amount);
    }

    private static IReadOnlyList<CliContractExample> Contract =>
        new CliContractValidator<IPaymentContract>().Enumerate();

    [Fact]
    public void Keep_The_Same_Verdicts_It_Always_Had()
    {
        // The matching logic is correct and this ticket does not touch it. Pinned so a future change
        // to the reason plumbing cannot quietly change WHICH examples pass.
        var byExample = Contract.ToDictionary(e => e.Example, e => e.Matched);

        Assert.True(byExample["pay --amount 10.5"]);
        Assert.False(byExample["pay --amount abc"]);
        Assert.False(byExample["pay --amount 1 --bogus"]);
    }

    [Fact]
    public void Name_The_Value_A_Conversion_Rejected()
    {
        var failed = Contract.Single(e => e.Example == "pay --amount abc");

        Assert.Equal(
            "Value 'abc' for option '--amount' is invalid. abc is not a valid value for Decimal.",
            failed.FailureReason);
    }

    [Fact]
    public void Name_An_Unrecognized_Option()
    {
        var failed = Contract.Single(e => e.Example == "pay --amount 1 --bogus");

        Assert.NotNull(failed.FailureReason);
        Assert.Contains("Unrecognized option", failed.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("--bogus", failed.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Leave_A_Dispatched_Example_With_No_Reason_To_Give()
    {
        var ok = Contract.Single(e => e.Example == "pay --amount 10.5");

        Assert.True(ok.Matched);
        Assert.Null(ok.FailureReason);
    }

    [Fact]
    public void Hand_The_Reason_To_The_Validate_Callback()
    {
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);

        new CliContractValidator<IPaymentContract>().Validate(
            onNotInvoked: (example, reason) => reasons[example.Example] = reason);

        Assert.Equal(2, reasons.Count);
        Assert.Contains("Unrecognized option", reasons["pay --amount 1 --bogus"], StringComparison.Ordinal);
        Assert.Contains("abc", reasons["pay --amount abc"], StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Nothing_To_The_Process_Console()
    {
        // Asking a question should not print. The validator used to spray the framework's rejection
        // diagnostics into the user's test log as a side effect of running the examples.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var realOut = Console.Out;
        var realError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            _ = new CliContractValidator<IPaymentContract>().Enumerate();
        }
        finally
        {
            Console.SetOut(realOut);
            Console.SetError(realError);
        }

        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    public interface IHelpEatenContract
    {
        // An example that is swallowed by a built-in trigger never reaches a handler, and exits 0 —
        // so "matched" would be a lie and "no diagnostic" would be unhelpful. It is named instead.
        [CliRoute("status")]
        [CliCommandExample("--help")]
        int Status();
    }

    [Fact]
    public void Explain_An_Example_A_Builtin_Trigger_Swallowed()
    {
        var eaten = Assert.Single(new CliContractValidator<IHelpEatenContract>().Enumerate());

        Assert.False(eaten.Matched);
        Assert.Contains("--help", eaten.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("without reaching a handler", eaten.FailureReason!, StringComparison.Ordinal);
    }
}
