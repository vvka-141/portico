<!--
Keep this short. The diff says what changed; this says why, and what would have caught it.
-->

## What and why

<!-- One paragraph. If this fixes an issue, link it. -->

## Behaviour change

<!--
If user-visible behaviour changed, show it — the before and after, as a command line and its output.
If nothing user-visible changed, write "none".
-->

## Checklist

- [ ] `dotnet build portico.sln -c Release` passes (warnings are errors).
- [ ] `dotnet test portico.sln -c Release` passes.
- [ ] **There is a test that fails without this change.** A fix with no failing-first test is a
      coincidence, not a fix.
- [ ] The core `Portico` package still has **zero** dependencies (anything needing
      `Microsoft.Extensions.*` belongs in an adapter package).
- [ ] New public members carry an XML `<summary>` **and** an `<example>` (the doc gate will fail
      otherwise).
- [ ] `CHANGELOG.md` updated if this is user-visible.
