# PorticoCli

```bash
dotnet run --project PorticoCli -- greet --name Ada
dotnet run --project PorticoCli -- greet --name Grace --loud
dotnet run --project PorticoCli -- --help
```

## The loop that makes this different

```bash
dotnet test
```

Every `[CliCommandExample]` on `IGreetTool` is executed against the real pipeline. Rename the route,
retype an option, make one required — the example stops dispatching and the test suite goes red.

Try it: change `--name` to `--who` in `IGreetTool.cs` and run `dotnet test` again. The examples in your
help output cannot drift from the CLI you actually shipped, because they are the tests.

The analyzers are already watching too: delete an example and `POR004` asks for it back; declare two
options with the same alias and `POR009` fails the build.

## Where to go next

- [Portico README](https://github.com/vvka-141/portico)
- [Capabilities](https://github.com/vvka-141/portico/blob/main/docs/reference/capabilities.md) — env-var
  fallback, map options, `Sensitive`, human-readable durations, shell completion
- [Analyzer rules](https://github.com/vvka-141/portico/blob/main/docs/reference/analyzer-rules.md)
