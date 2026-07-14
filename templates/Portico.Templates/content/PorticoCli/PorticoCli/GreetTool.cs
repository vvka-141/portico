using Portico;

namespace PorticoCli;

public sealed class GreetTool : IGreetTool
{
    public int Greet(string name, CliFlag? loud = null)
    {
        var greeting = $"Hello, {name}!";
        Console.WriteLine(loud.HasValue ? greeting.ToUpperInvariant() : greeting);

        // The exit code IS the result. 0 = success; throw CliExitException for a failure path.
        return 0;
    }
}
