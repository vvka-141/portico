using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
//
// POR010. Everything a user types is a string, so an option's type must be buildable from one.
//
// Half of these tests are NEGATIVE, and they are the important half: this rule fires at Error
// severity, so a false positive fails a build that works — strictly worse than the runtime error it
// replaces. The rule is conservative by design and only speaks when it is certain.
public sealed class UnconvertibleOptionTypeAnalyzer_Should
{
    private static async Task<int> Por010Count(string source) =>
        (await AnalyzerTestRunner.RunAsync(new UnconvertibleOptionTypeAnalyzer(), source))
            .Count(d => d.Id == "POR010");

    [Fact]
    public async Task Flag_A_Class_Of_Your_Own_With_No_Converter()
    {
        const string source = """
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money 5")]
                public int C([CliOption("--money")] Money money) => 0;
            }
            """;

        Assert.Equal(1, await Por010Count(source));
    }

    [Fact]
    public async Task Name_The_Option_And_The_Type()
    {
        const string source = """
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money 5")]
                public int C([CliOption("--money|-m")] Money money) => 0;
            }
            """;

        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(new UnconvertibleOptionTypeAnalyzer(), source));
        var message = diagnostic.GetMessage();

        Assert.Contains("--money|-m", message);
        Assert.Contains("Money", message);
        Assert.Contains("TypeConverter", message);
    }

    [Fact]
    public async Task Accept_A_Type_That_Declares_A_TypeConverter()
    {
        const string source = """
            using System.ComponentModel;
            using Portico;

            [TypeConverter(typeof(MoneyConverter))]
            public sealed class Money { public decimal Amount; }

            public sealed class MoneyConverter : TypeConverter { }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money 5")]
                public int C([CliOption("--money")] Money money) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }

    [Fact]
    public async Task Accept_A_Converter_Inherited_From_A_Base_Class()
    {
        const string source = """
            using System.ComponentModel;
            using Portico;

            [TypeConverter(typeof(MoneyConverter))]
            public abstract class Currency { }

            public sealed class Money : Currency { }

            public sealed class MoneyConverter : TypeConverter { }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money 5")]
                public int C([CliOption("--money")] Money money) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("int?")]
    [InlineData("bool")]
    [InlineData("decimal")]
    [InlineData("System.TimeSpan")]
    [InlineData("System.TimeSpan?")]
    [InlineData("System.Guid")]
    [InlineData("System.Uri")]
    [InlineData("System.DateTime")]
    [InlineData("System.IO.FileInfo")]
    [InlineData("System.Net.IPAddress")]
    [InlineData("Portico.CliFlag?")]
    public async Task Say_Nothing_About_A_Referenced_Type(string type)
    {
        // Whether a type from metadata has a converter is a RUNTIME fact — TypeDescriptor's intrinsic
        // table (Guid, TimeSpan, Uri, …) is invisible to Roslyn, and IPAddress genuinely has no
        // converter. Guessing either way would be wrong, so the rule stays silent and the runtime
        // check at CliApplication.Create remains the backstop.
        var source = $$"""
            using Portico;

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --value x")]
                public int C([CliOption("--value")] {{type}} value) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }

    [Fact]
    public async Task Accept_An_Enum_Of_Your_Own()
    {
        // An enum converts from its member name. This is the false positive that would have hurt
        // most — enums are the most natural option type there is.
        const string source = """
            using Portico;

            public enum Level { Low, High }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --level High")]
                public int C([CliOption("--level")] Level level) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }

    [Fact]
    public async Task Look_Through_A_Collection_To_Its_Element()
    {
        const string source = """
            using System.Collections.Generic;
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money 5")]
                public int C([CliOption("--money")] List<Money> money) => 0;
            }
            """;

        Assert.Equal(1, await Por010Count(source));
    }

    [Fact]
    public async Task Look_Through_A_Map_To_Its_Value()
    {
        // A map option binds VALUES from the command line; the key is always a string.
        const string source = """
            using System.Collections.Generic;
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --money[usd] 5")]
                public int C([CliOption("--money")] Dictionary<string, Money> money) => 0;
            }
            """;

        Assert.Equal(1, await Por010Count(source));
    }

    [Fact]
    public async Task Accept_A_Collection_Of_A_Convertible_Element()
    {
        const string source = """
            using System.Collections.Generic;
            using Portico;

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c --tag a --tag b")]
                public int C([CliOption("--tag")] HashSet<string> tags) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }

    [Fact]
    public async Task Flag_An_Unconvertible_Bundle_Property()
    {
        const string source = """
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class Bundle : CliOptions
            {
                [CliOption("--money")] public Money? Money { get; set; }
            }
            """;

        Assert.Equal(1, await Por010Count(source));
    }

    [Fact]
    public async Task Ignore_A_Parameter_With_No_CliOption_Attribute()
    {
        const string source = """
            using Portico;

            public sealed class Money { public decimal Amount; }

            public sealed class S
            {
                [CliRoute("c")]
                [CliCommandExample("c")]
                public int C(Money money) => 0;
            }
            """;

        Assert.Equal(0, await Por010Count(source));
    }
}
