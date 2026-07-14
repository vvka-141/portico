using System;

namespace Portico;

/// <summary>
/// Case-insensitive Levenshtein distance and tolerance helpers shared by the command-name and
/// option-name suggestion paths. Kept simple — O(n·m) with two reusable row buffers, suitable
/// for the short strings typical of CLI identifiers.
/// </summary>
internal static class CliFuzzyMatch
{
    public static int LevenshteinDistance(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 0; i < a.Length; i++)
        {
            current[0] = i + 1;
            var ac = char.ToLowerInvariant(a[i]);
            for (int j = 0; j < b.Length; j++)
            {
                var cost = ac == char.ToLowerInvariant(b[j]) ? 0 : 1;
                current[j + 1] = Math.Min(
                    Math.Min(current[j] + 1, previous[j + 1] + 1),
                    previous[j] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
