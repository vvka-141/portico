using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliInvocation_FromArgs_Should
{
    /// <summary>
    /// Tests that parsing a simple executable with no options or arguments correctly populates the properties.
    /// </summary>
    [Fact]
    public void Parse_SimpleExecutable_NoOptionsOrArguments_Should_Succeed()
    {
        // Arrange
        string invocation = "app.exe";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        Assert.Equal("app.exe", parsedCommand.ToString());
        Assert.Empty(parsedCommand.Segments); // No segments should be present
        Assert.Empty(parsedCommand.Options); // No options should be present

        // Additional Assertions (Optional)
        // Verify that ToString returns the original command line
        Assert.Equal("app.exe", parsedCommand.ToString());

        // Verify that the formatted signature is correct
        Assert.Equal("app.exe", parsedCommand.ToString("Signature", null));

        // Implicit string conversion
        string implicitString = parsedCommand;
        Assert.Equal("app.exe", implicitString);
    }

    /// <summary>
    /// Tests that parsing an executable with multiple arguments correctly captures all segments without any options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithMultipleArguments_Should_CaptureAllSegments()
    {
        // Arrange
        string invocation = "app.exe input.txt output.txt log.txt";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        Assert.Equal("app.exe input.txt output.txt log.txt", parsedCommand.ToString());


        // Verify that Segments contain the correct arguments
        Assert.Equal(3, parsedCommand.Segments.Length);
        Assert.Contains("input.txt", parsedCommand.Segments);
        Assert.Contains("output.txt", parsedCommand.Segments);
        Assert.Contains("log.txt", parsedCommand.Segments);

        // Ensure that there are no options present
        Assert.Empty(parsedCommand.Options);

        // Additional Assertions (Optional)
        // Verify that ToString returns the original command line
        Assert.Equal("app.exe input.txt output.txt log.txt", parsedCommand.ToString());

        // Verify that the formatted signature is correct
        Assert.Equal("app.exe input.txt output.txt log.txt", parsedCommand.ToString("Signature", null));

        // Implicit string conversion
        string implicitString = parsedCommand;
        Assert.Equal("app.exe input.txt output.txt log.txt", implicitString);
    }

    /// <summary>
    /// Tests that parsing an executable with quoted arguments correctly captures all quoted segments.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithQuotedArguments_Should_CaptureQuotedSegments()
    {
        // Arrange
        string invocation = @"app.exe ""C:\Program Files\input file.txt"" ""C:\Output Folder\output file.txt""";
        var signatureRegex = new Regex(@"app.exe \w{32} \w{32}");
        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);


        // Verify that Segments contain the correct quoted arguments
        Assert.Equal(2, parsedCommand.Segments.Length);
        Assert.Contains(@"C:\Program Files\input file.txt", parsedCommand.Segments);
        Assert.Contains(@"C:\Output Folder\output file.txt", parsedCommand.Segments);

        // Ensure that there are no options present
        Assert.Empty(parsedCommand.Options);

    }


    // Environment-variable expansion was removed from CliInvocation.SplitCommandLine; the shell
    // expands %VAR% (cmd.exe) / $VAR (POSIX) before the process sees argv. The old test that
    // pinned expansion behavior has been retired with the feature.

    /// <summary>
    /// Tests that parsing an executable with flag options correctly captures all flag options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithFlags_Should_CaptureFlagOptions()
    {
        // Arrange
        string invocation = "app.exe --verbose --debug -h";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        Assert.Equal("app.exe --verbose --debug -h", parsedCommand.ToString());

        // Verify that Segments are empty since there are no arguments
        Assert.Empty(parsedCommand.Segments);

        // Ensure that Options contain exactly three flag options
        Assert.Equal(3, parsedCommand.Options.Length);

        // Verify that each option is a CliFlagOptionCapture and has the correct name
        Assert.Contains(parsedCommand.Options, option =>
            option is CliFlagOptionCapture flagOption && flagOption.Name == "--verbose");

        Assert.Contains(parsedCommand.Options, option =>
            option is CliFlagOptionCapture flagOption && flagOption.Name == "--debug");

        Assert.Contains(parsedCommand.Options, option =>
            option is CliFlagOptionCapture flagOption && flagOption.Name == "-h");

        // Additional Assertions (Optional)
        // Verify that ToString returns the original command line
        Assert.Equal("app.exe --verbose --debug -h", parsedCommand.ToString());

        // Verify that the formatted signature is correct
        Assert.Equal("app.exe --verbose --debug -h", parsedCommand.ToString("Signature", null));

        // Implicit string conversion should return the signature
        string implicitString = parsedCommand;
        Assert.Equal("app.exe --verbose --debug -h", implicitString);
    }


    /// <summary>
    /// Tests that parsing an executable with scalar options correctly captures all scalar options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithScalarOptions_Should_CaptureScalarOptions()
    {
        // Arrange
        string invocation = @"app.exe --output ""C:\Output Folder"" --level 5";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);

        // Verify that Segments are empty since there are no arguments
        Assert.Empty(parsedCommand.Segments);

        // Ensure that Options contain exactly two scalar options
        Assert.Equal(2, parsedCommand.Options.Length);

        // Verify that each option is a CliScalarOptionCapture and has the correct name and value
        Assert.Contains(parsedCommand.Options, option =>
            option is CliScalarOptionCapture scalarOption &&
            scalarOption.Name == "--output" &&
            scalarOption.Value == @"C:\Output Folder");

        Assert.Contains(parsedCommand.Options, option =>
            option is CliScalarOptionCapture scalarOption &&
            scalarOption.Name == "--level" &&
            scalarOption.Value == "5");

    }


    /// <summary>
    /// Tests that parsing an executable with collection options correctly captures all collection options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithCollectionOptions_Should_CaptureCollectionOptions()
    {
        // Arrange
        string invocation = "app.exe --files file1.txt file2.txt file3.txt";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        

        // Verify that Segments are empty since there are no arguments
        Assert.Empty(parsedCommand.Segments);

        // Ensure that Options contain exactly one collection option
        Assert.Single(parsedCommand.Options);

        // Verify that the option is a CliCollectionOptionCapture and has the correct name and values
        var collectionOption = Assert.IsType<CliCollectionOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--files", collectionOption.Name);
        Assert.Equivalent(new[] { "file1.txt", "file2.txt", "file3.txt" }, collectionOption.Values.ToArray());

    }

    /// <summary>
    /// Tests that parsing an executable with keyed flag options correctly captures all keyed flag options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithKeyedFlagOptions_Should_CaptureKeyedFlagOptions()
    {
        // Arrange
        string invocation = "app.exe --config[env] --config[debug]";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null
        Assert.Equal("app.exe", parsedCommand.ExecutableName);

        // Verify that Segments are empty since there are no arguments
        Assert.Empty(parsedCommand.Segments);

        // Ensure that Options contain exactly two keyed flag options
        Assert.Equal(2, parsedCommand.Options.Length);

        // Verify that each option is a CliKeyFlagOptionCapture and has the correct name and key
        Assert.Contains(parsedCommand.Options, option =>
            option is CliKeyFlagOptionCapture keyFlagOption &&
            keyFlagOption.Name == "--config" &&
            keyFlagOption.Key == "env");

        Assert.Contains(parsedCommand.Options, option =>
            option is CliKeyFlagOptionCapture keyFlagOption &&
            keyFlagOption.Name == "--config" &&
            keyFlagOption.Key == "debug");

    }

    /// <summary>
    /// Tests that parsing an executable with a quoted name containing spaces correctly captures the executable and its options.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithQuotedName_Should_CaptureQuotedExecutableAndOptions()
    {
        // Arrange
        string invocation = @"""C:\Program Files\MyApp\app.exe"" --mode release";


        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null

        // Verify ExecutableName is correctly extracted without surrounding quotes
        Assert.Equal(@"app.exe", parsedCommand.ExecutableName);

        // Verify that Signature replaces the option value with a placeholder

        // Verify that Segments are empty since there are no standalone arguments
        Assert.Empty(parsedCommand.Segments);

        // Ensure that Options contain exactly one scalar option
        Assert.Single(parsedCommand.Options);

        // Retrieve and verify the scalar option
        var scalarOption = Assert.IsType<CliScalarOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--mode", scalarOption.Name);
        Assert.Equal("release", scalarOption.Value);
    }

    /// <summary>
    /// Tests that parsing an executable with mixed positional arguments and options correctly captures both.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithMixedArgumentsAndOptions_Should_CaptureCorrectly()
    {
        // Arrange
        string invocation = @"app.exe input.txt --verbose --output ""C:\Output Folder""";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null

        // Verify ExecutableName is correctly extracted
        Assert.Equal("app.exe", parsedCommand.ExecutableName);


        // Verify that Segments contain the positional argument
        Assert.Single(parsedCommand.Segments);
        Assert.Contains("input.txt", parsedCommand.Segments);

        // Ensure that Options contain exactly two options
        Assert.Equal(2, parsedCommand.Options.Length);

        // Verify that --verbose is captured as a CliFlagOptionCapture
        Assert.Contains(parsedCommand.Options, option =>
            option is CliFlagOptionCapture flagOption &&
            flagOption.Name == "--verbose");

        // Verify that --output is captured as a CliScalarOptionCapture with the correct value
        Assert.Contains(parsedCommand.Options, option =>
            option is CliScalarOptionCapture scalarOption &&
            scalarOption.Name == "--output" &&
            scalarOption.Value == @"C:\Output Folder");
    }

    /// <summary>
    /// Tests that parsing an executable with subcommands and their options correctly captures both.
    /// </summary>
    [Fact]
    public void Parse_ExecutableWithSubcommandsAndOptions_Should_CaptureSubcommandsAndTheirOptions()
    {
        // Arrange
        string invocation = @"app.exe deploy --environment production --force";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand); // Ensure that the parsedCommand is not null

        // Verify ExecutableName is correctly extracted
        Assert.Equal("app.exe", parsedCommand.ExecutableName);

        Assert.Single(parsedCommand.Segments);
        // Verify Subcommand is correctly identified
        Assert.Equal("deploy", parsedCommand.Segments[0]);




        // Ensure that Options contain exactly two options
        Assert.Equal(2, parsedCommand.Options.Length);

        // Verify that --environment is captured as a CliScalarOptionCapture with the correct value
        Assert.Contains(parsedCommand.Options, option =>
            option is CliScalarOptionCapture scalarOption &&
            scalarOption.Name == "--environment" &&
            scalarOption.Value == "production");

        // Verify that --force is captured as a CliFlagOptionCapture
        Assert.Contains(parsedCommand.Options, option =>
            option is CliFlagOptionCapture flagOption &&
            flagOption.Name == "--force");
    }

    /// <summary>
    /// Tests that parsing an empty command line throws an ArgumentOutOfRangeException.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_EmptyCommandLine_Should_ThrowArgumentNullException(string commandLined)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => CliInvocation.FromArgs(commandLined));
    }

    /// <summary>
    /// Tests that parsing a command line with special characters correctly captures them.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithSpecialCharacters_Should_CaptureSpecialCharacters()
    {
        // Arrange
        string commandLineText = @"app.exe --path C:\Path\With\Special\!@#$%^&*() chars";

        // Act
        CliInvocation invocation = CliInvocation.FromArgs(commandLineText);

        // Assert
        Assert.NotNull(invocation);
        Assert.Equal("app.exe", invocation.ExecutableName);

        Assert.Single(invocation.Options);
        var collectionOption = Assert.IsType<CliCollectionOptionCapture>(invocation.Options[0]);
        Assert.Equal("--path", collectionOption.Name);
        Assert.Equal(2, collectionOption.Values.Length);
        Assert.Contains(@"C:\Path\With\Special\!@#$%^&*()", collectionOption.Values);
        Assert.Contains(@"chars", collectionOption.Values);
    }


    /// <summary>
    /// Tests that parsing a command line with duplicate options aggregates their values.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithDuplicateOptions_Should_AggregateValues()
    {
        // Arrange
        string commandLineText = "app.exe --include file1.txt --include file2.txt";

        // Act
        var invocation = CliInvocation.FromArgs(commandLineText);

        // Assert
        Assert.NotNull(invocation);
        Assert.Equal("app.exe", invocation.ExecutableName);

        Assert.Equal(2, invocation.Options.Length);
        Assert.All(invocation.Options, option =>
        {
            Assert.IsType<CliScalarOptionCapture>(option);
            Assert.Equal("--include", option.Name);
        });

        var includeOptions = invocation.Options.Cast<CliScalarOptionCapture>().ToList();
        Assert.Contains(includeOptions, option => option.Value == "file1.txt");
        Assert.Contains(includeOptions, option => option.Value == "file2.txt");
    }


    /// <summary>
    /// Tests that parsing a command line with nested subcommands and their options correctly captures all components.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithNestedSubcommandsAndOptions_Should_CaptureAllComponents()
    {
        // Arrange
        string invocation = @"app.exe service start --force --timeout 30";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand);
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        Assert.Equal(@"app.exe service start --force --timeout 30", parsedCommand.ToString());

        // Verify segments (subcommands)
        Assert.Equal(2, parsedCommand.Segments.Length);
        Assert.Contains("service", parsedCommand.Segments);
        Assert.Contains("start", parsedCommand.Segments);

        // Verify options
        Assert.Equal(2, parsedCommand.Options.Length);
        var forceOption = Assert.IsType<CliFlagOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--force", forceOption.Name);

        var timeoutOption = Assert.IsType<CliScalarOptionCapture>(parsedCommand.Options[1]);
        Assert.Equal("--timeout", timeoutOption.Name);
        Assert.Equal("30", timeoutOption.Value);
    }


    /// <summary>
    /// Tests that parsing a command line with Unicode characters correctly captures them.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithUnicodeCharacters_Should_CaptureUnicodeCharacters()
    {
        // Arrange
        string invocation = @"app.exe --message ""こんにちは世界"" --path C:\データ";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand);
        Assert.Equal("app.exe", parsedCommand.ExecutableName);


        Assert.Equal(2, parsedCommand.Options.Length);

        var messageOption = Assert.IsType<CliScalarOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--message", messageOption.Name);
        Assert.Equal("こんにちは世界", messageOption.Value);

        var pathOption = Assert.IsType<CliScalarOptionCapture>(parsedCommand.Options[1]);
        Assert.Equal("--path", pathOption.Name);
        Assert.Equal(@"C:\データ", pathOption.Value);
    }


    /// <summary>
    /// Tests that parsing a command line with mixed option types correctly captures all options.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithMixedOptionTypes_Should_CaptureAllOptionTypes()
    {
        // Arrange
        string invocation = @"app.exe --enable-feature --set-level 3 --tags tag1 tag2 tag3 --config[env] production";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand);
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
       

        Assert.Equal(4, parsedCommand.Options.Length);

        var enableFeatureOption = Assert.IsType<CliFlagOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--enable-feature", enableFeatureOption.Name);

        var setLevelOption = Assert.IsType<CliScalarOptionCapture>(parsedCommand.Options[1]);
        Assert.Equal("--set-level", setLevelOption.Name);
        Assert.Equal("3", setLevelOption.Value);

        var tagsOption = Assert.IsType<CliCollectionOptionCapture>(parsedCommand.Options[2]);
        Assert.Equal("--tags", tagsOption.Name);
        Assert.Equivalent(new[] { "tag1", "tag2", "tag3" }, tagsOption.Values.ToArray());

        var configOption = Assert.IsType<CliKeyValueOptionCapture>(parsedCommand.Options[3]);
        Assert.Equal("--config", configOption.Name);
        Assert.Equal("env", configOption.Key);
        Assert.Equal("production", configOption.Value);
    }

    /// <summary>
    /// Tests that parsing a command line with nested keyed options correctly captures all keys and their values.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithNestedKeyedOptions_Should_CaptureAllKeysAndValues()
    {
        // Arrange
        string invocation = "app.exe --database[host] localhost --database[port] 5432 --database[user] admin";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand);
        Assert.Equal("app.exe", parsedCommand.ExecutableName);
        Assert.Equal("app.exe --database[host] localhost --database[port] 5432 --database[user] admin", parsedCommand.ToString());

        Assert.Equal(3, parsedCommand.Options.Length);

        var hostOption = Assert.IsType<CliKeyValueOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--database", hostOption.Name);
        Assert.Equal("host", hostOption.Key);
        Assert.Equal("localhost", hostOption.Value);

        var portOption = Assert.IsType<CliKeyValueOptionCapture>(parsedCommand.Options[1]);
        Assert.Equal("--database", portOption.Name);
        Assert.Equal("port", portOption.Key);
        Assert.Equal("5432", portOption.Value);

        var userOption = Assert.IsType<CliKeyValueOptionCapture>(parsedCommand.Options[2]);
        Assert.Equal("--database", userOption.Name);
        Assert.Equal("user", userOption.Key);
        Assert.Equal("admin", userOption.Value);
    }



    /// <summary>
    /// Tests that parsing a command line with multiple keyed options and their collections correctly captures all keys and values.
    /// </summary>
    [Fact]
    public void Parse_CommandLineWithMultipleKeyedOptionsAndCollections_Should_CaptureAllKeysAndValues()
    {
        // Arrange
        string invocation = "app.exe --config[env] production --config[servers] server1 server2 server3";

        // Act
        CliInvocation parsedCommand = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.NotNull(parsedCommand);
        Assert.Equal("app.exe", parsedCommand.ExecutableName);

        Assert.Equal(2, parsedCommand.Options.Length);

        var envOption = Assert.IsType<CliKeyValueOptionCapture>(parsedCommand.Options[0]);
        Assert.Equal("--config", envOption.Name);
        Assert.Equal("env", envOption.Key);
        Assert.Equal("production", envOption.Value);

        var serversOption = Assert.IsType<CliKeyCollectionOptionCapture>(parsedCommand.Options[1]);
        Assert.Equal("--config", serversOption.Name);
        Assert.Equal("servers", serversOption.Key);
        Assert.Equivalent(new[] { "server1", "server2", "server3" }, serversOption.Values.ToArray());
    }


    [Fact]
    public void Parse_SimpleCommand_ExtractsExecutableName()
    {
        // Arrange
        var invocation = "dotnet.exe build";

        // Act
        var result = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.Equal("dotnet.exe", result.ExecutableName);
        Assert.Single(result.Segments, "build");
        Assert.Empty(result.Options);
    }



    [Fact]
    public void Parse_QuotedExecutableName_HandlesCorrectly()
    {
        // Arrange
        var invocation = "\"My Program.exe\" arg1 arg2";

        // Act
        var result = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.Equal("My Program.exe", result.ExecutableName);
        Assert.Equal(new[] { "arg1", "arg2" }, result.Segments);
    }

    [Theory]
    [InlineData("app.exe --foo=bar", "--foo", "bar")]
    [InlineData("app.exe --foo:bar", "--foo", "bar")]
    [InlineData("app.exe -f=bar", "-f", "bar")]
    [InlineData("app.exe -f:bar", "-f", "bar")]
    public void Split_Option_Assignment_Syntax(string invocation, string expectedName, string expectedValue)
    {
        var parsed = CliInvocation.FromArgs(invocation);
        Assert.Single(parsed.Options);
        var scalar = Assert.IsType<CliScalarOptionCapture>(parsed.Options[0]);
        Assert.Equal(expectedName, scalar.Name);
        Assert.Equal(expectedValue, scalar.Value);
    }

    [Fact]
    public void Preserve_Equal_Sign_Inside_Map_Key_Brackets()
    {
        // --config[env]=prod   → option "--config", key "env", value "prod"
        var parsed = CliInvocation.FromArgs("app.exe --config[env]=prod");
        Assert.Single(parsed.Options);
        var kv = Assert.IsType<CliKeyValueOptionCapture>(parsed.Options[0]);
        Assert.Equal("--config", kv.Name);
        Assert.Equal("env", kv.Key);
        Assert.Equal("prod", kv.Value);
    }

    [Theory]
    [InlineData("app.exe --flag", typeof(CliFlagOptionCapture))]
    [InlineData("app.exe --option value", typeof(CliScalarOptionCapture))]
    [InlineData("app.exe --list val1 val2", typeof(CliCollectionOptionCapture))]
    public void Parse_DifferentOptionTypes_CapturedCorrectly(string invocation, Type expectedType)
    {
        // Act
        var result = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.Single(result.Options);
        Assert.IsType(expectedType, result.Options[0]);
    }

    [Fact]
    public void Parse_KeyedOptions_CapturedCorrectly()
    {
        // Arrange
        var invocation = "app.exe --config[env] prod --settings[log] debug info warn";

        // Act
        var result = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.Equal(2, result.Options.Length);

        var configOption = Assert.IsType<CliKeyValueOptionCapture>(result.Options[0]);
        Assert.Equal("--config", configOption.Name);
        Assert.Equal("env", configOption.Key);
        Assert.Equal("prod", configOption.Value);

        var settingsOption = Assert.IsType<CliKeyCollectionOptionCapture>(result.Options[1]);
        Assert.Equal("--settings", settingsOption.Name);
        Assert.Equal("log", settingsOption.Key);
        Assert.Equal(new[] { "debug", "info", "warn" }, settingsOption.Values);
    }

    [Fact]
    public void Parse_ComplexCommand_HandlesAllFeatures()
    {
        // Arrange
        var invocation = @"tool.exe subcommand ""quoted arg"" --flag --scalar value --list item1 item2 --config[env] prod --multi[tags] tag1 tag2";

        // Act
        var result = CliInvocation.FromArgs(invocation);

        // Assert
        Assert.Equal("tool.exe", result.ExecutableName);
        Assert.Equal(new[] { "subcommand", "quoted arg" }, result.Segments);
        Assert.Equal(5, result.Options.Length);

        Assert.IsType<CliFlagOptionCapture>(result.Options[0]);
        Assert.IsType<CliScalarOptionCapture>(result.Options[1]);
        Assert.IsType<CliCollectionOptionCapture>(result.Options[2]);
        Assert.IsType<CliKeyValueOptionCapture>(result.Options[3]);
        Assert.IsType<CliKeyCollectionOptionCapture>(result.Options[4]);
    }

    [Theory]
    [InlineData("app.exe --count -5", "--count", "-5")]
    [InlineData("app.exe --count -42", "--count", "-42")]
    [InlineData("app.exe --amount -1.5", "--amount", "-1.5")]
    [InlineData("app.exe --frac -.5", "--frac", "-.5")]
    [InlineData("app.exe -n -7", "-n", "-7")]
    [InlineData("app.exe --count=-5", "--count", "-5")]
    [InlineData("app.exe --count:-5", "--count", "-5")]
    public void Parse_NegativeNumber_AsValue_NotOption(string invocation, string optionName, string expectedValue)
    {
        var result = CliInvocation.FromArgs(invocation);

        Assert.Single(result.Options);
        var capture = Assert.IsType<CliScalarOptionCapture>(result.Options[0]);
        Assert.Equal(optionName, capture.Name);
        Assert.Equal(expectedValue, capture.Value);
    }

    [Fact]
    public void Treat_DoubleDashDigit_As_Option_Not_Number()
    {
        // --5 doesn't fit any sane CLI; we surface it as an option so downstream
        // resolution prints "Unrecognized option(s): --5" rather than silently
        // accepting it as a value.
        var result = CliInvocation.FromArgs("app.exe --5");
        Assert.Single(result.Options);
        Assert.IsType<CliFlagOptionCapture>(result.Options[0]);
        Assert.Equal("--5", result.Options[0].Name);
    }

    [Fact]
    public void Parse_Negative_In_Collection_Capture()
    {
        var result = CliInvocation.FromArgs("app.exe --offsets -1 -2 -3");
        Assert.Single(result.Options);
        var capture = Assert.IsType<CliCollectionOptionCapture>(result.Options[0]);
        Assert.Equal(new[] { "-1", "-2", "-3" }, capture.Values);
    }

    [Fact]
    public void Treat_DashOnly_As_Positional()
    {
        // `-` is the conventional stand-in for stdin/stdout — must not be parsed as an option.
        var result = CliInvocation.FromArgs("app.exe pipe -");
        Assert.Equal(new[] { "pipe", "-" }, result.Segments);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Stop_Option_Parsing_At_EndOfOptions_Terminator()
    {
        var result = CliInvocation.FromArgs("app.exe run -- file.txt");

        Assert.Equal(new[] { "run", "file.txt" }, result.Segments);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Treat_Post_Terminator_DashTokens_As_Positional()
    {
        var result = CliInvocation.FromArgs("app.exe cp -- -weirdname --also-weird");

        Assert.Equal(new[] { "cp", "-weirdname", "--also-weird" }, result.Segments);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Mix_Options_Before_And_Positionals_After_Terminator()
    {
        var result = CliInvocation.FromArgs("app.exe cmd --flag -- trailing-arg");

        Assert.Equal(new[] { "cmd", "trailing-arg" }, result.Segments);
        Assert.Single(result.Options);
        Assert.IsType<CliFlagOptionCapture>(result.Options[0]);
        Assert.Equal("--flag", result.Options[0].Name);
    }

    [Fact]
    public void Allow_Terminator_With_Nothing_After()
    {
        var result = CliInvocation.FromArgs("app.exe --flag --");

        Assert.Empty(result.Segments);
        Assert.Single(result.Options);
        Assert.Equal("--flag", result.Options[0].Name);
    }
}