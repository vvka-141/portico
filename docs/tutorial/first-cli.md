# Build your first Portico CLI

Start here. In fifteen minutes you will have a working CLI whose contract test is green — and then
you will break it on purpose and watch the build go red. That last step is the point.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- A terminal

## Scaffold the project

```
dotnet new install Portico.Templates
dotnet new portico-cli -n MyCli
cd MyCli
```

You now have a solution with two projects:

```
MyCli/
  MyCli.sln
  MyCli/              ← the CLI
    Program.cs
    IGreetTool.cs     ← the contract (interface)
    GreetTool.cs      ← the implementation
  MyCli.Tests/        ← the contract test
    GreetContract_Should.cs
```

## The contract

Open `IGreetTool.cs`. This is the whole declaration:

```csharp
public interface IGreetTool
{
    [Description("Greet someone")]
    [CliRoute("greet")]
    [CliCommandExample("greet --name Ada")]
    [CliCommandExample("greet --name Grace --loud")]
    int Greet(
        [CliOption("--name|-n", "Who to greet")] string name,
        [CliOption("--loud", "Shout it")] CliFlag? loud = null);
}
```

A plain C# method, one route attribute, two examples. The interface is not ceremony — it is what
`CliContractValidator<IGreetTool>` proxies, which is how every example becomes an executable test.

`CliFlag?` is presence-only: `--loud`, not `--loud true`. A `bool` would be a value option.

## The implementation

`GreetTool.cs` is what the command actually does:

```csharp
public sealed class GreetTool : IGreetTool
{
    public int Greet(string name, CliFlag? loud = null)
    {
        var greeting = $"Hello, {name}!";
        Console.WriteLine(loud.HasValue ? greeting.ToUpperInvariant() : greeting);
        return 0;
    }
}
```

The return value is the exit code. `0` is success; throw `CliExitException` for error paths.

## The wiring

`Program.cs` is one statement:

```csharp
public static int Main(string[] args) =>
    CliApplication
        .Create(cfg => cfg
            .AddCommands(new GreetTool())
            .WithVersion("MyCli 1.0.0"))
        .Run(args);
```

## Run it

```
dotnet run --project MyCli -- greet --name Ada
```

```
Hello, Ada!
```

```
dotnet run --project MyCli -- greet --name Grace --loud
```

```
HELLO, GRACE!
```

## Run the contract test

```
dotnet test
```

```
Passed!  - Failed: 0, Passed: 3, Skipped: 0
```

Three tests, all green. Open `GreetContract_Should.cs` to see what they check:

```csharp
public static IEnumerable<object[]> Examples() =>
    new CliContractValidator<IGreetTool>()
        .Enumerate()
        .Select(example => new object[] { example });

[Theory]
[MemberData(nameof(Examples))]
public void Dispatch(CliContractExample example) =>
    Assert.True(
        example.Matched,
        $"Example did not dispatch: {example.Example}{Environment.NewLine}" +
        $"  Reason: {example.FailureReason}");
```

Each `[CliCommandExample]` is run through the real pipeline against a `DispatchProxy` of
`IGreetTool`. If an example stops dispatching, this test goes red.

The second test pins the bound values:

```csharp
[Fact]
public void Bind_The_Name_And_The_Flag()
{
    var loud = Contract.Single(e => e.Example == "greet --name Grace --loud");

    Assert.Equal(nameof(IGreetTool.Greet), loud.Handler);
    Assert.Equal("Grace", loud.Arguments["name"]);
    Assert.NotNull(loud.Arguments["loud"]);
}
```

Dispatching is the floor. This test asserts that `greet --name Grace --loud` reaches `Greet` and
binds `name` to `"Grace"` — not `"Grace --loud"`, not `null`, not a string `"Grace"`.

## Break it

Now rename the option. In `IGreetTool.cs`, change `--name` to `--who`:

```csharp
[CliCommandExample("greet --name Ada")]           // ← still says --name
[CliCommandExample("greet --name Grace --loud")]   // ← still says --name
int Greet(
    [CliOption("--who|-n", "Who to greet")] string name,   // ← changed to --who
    [CliOption("--loud", "Shout it")] CliFlag? loud = null);
```

Run the tests:

```
dotnet test
```

```
Failed!  - Failed: 3, Passed: 0, Skipped: 0

Example did not dispatch: greet --name Ada
  Reason: Unrecognized option: --name. Did you mean --who?
```

The examples say `--name` but the option is now `--who`. The contract test catches the drift
between the documentation and the code — because the documentation IS the test.

Fix it by updating the examples to match:

```csharp
[CliCommandExample("greet --who Ada")]
[CliCommandExample("greet --who Grace --loud")]
```

Run `dotnet test` again — green.

## What just happened

Two enforcement points, and it is worth being exact about which does what:

- **POR004** (a Roslyn analyzer) is an **Error**: a `[CliRoute]` with no `[CliCommandExample]` at all
  breaks the build outright. No configuration, nothing to opt into.
- **`CliContractValidator<T>`** checks that the examples' *contents* still dispatch and bind. It is a
  test you write (the template writes it for you), and it is what caught the rename above.

Together they guarantee that every command has at least one example, and every example is executable.
The documentation cannot drift from the code, because the documentation is the test.

## What's next

- The [README](../../README.md) covers the full feature set: secrets redaction, `CliTestHarness`,
  multi-team composition, the HTTP analogy.
- [Capabilities reference](../reference/capabilities.md) — the whole surface, every entry backed by
  a test.
- [Analyzer rules](../reference/analyzer-rules.md) — the ten compile-time checks, and how to
  suppress one.
- [`examples/AdminCli`](../../examples/AdminCli) — a backend admin CLI with five commands, built and
  contract-tested by CI.
