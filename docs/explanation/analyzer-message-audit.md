# Analyzer message actionability audit

**Audited 2026-07-27 against `src/Portico.Analyzers/PorticoDiagnostics.cs` and each analyzer's
`Diagnostic.Create` call site; POR011 added 2026-07-28, POR012 and POR013 assessed 2026-07-29.**
Every live diagnostic (POR001–POR013, excluding the retired POR007) is assessed against four
criteria drawn from
[SHERLOC (arXiv 2606.24820)](https://arxiv.org/abs/2606.24820), which measured a 3.8× swing in
agent resolution rate between "Very High" and "Low" diagnostic quality:

1. **What** — does the message state what is wrong, specifically?
2. **Why** — does it explain why, in terms of the framework's model?
3. **Fix** — does it show the fix, ideally as a correct code fragment?
4. **Names** — does it name the exact symbol, parameter, or route involved?

A rule that ships a Roslyn code fix is noted — a code fix is the highest form of actionability. Where
adding one is cheap, that is flagged.

---

## Summary

| Rule | What | Why | Fix | Names | Code fix | Verdict |
|------|:----:|:---:|:---:|:-----:|:--------:|---------|
| [POR001](#por001) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |
| [POR002](#por002) | ✅ | ✅ | ✅ | ✅ | — | **PASS** |
| [POR003](#por003) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |
| [POR004](#por004) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |
| [POR005](#por005) | ✅ | ○ | ✅ | ✅ | ✅ | **PASS** |
| [POR006](#por006) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |
| [POR008](#por008) | ✅ | ✅ | ✅ | ✅ | — | **PASS** |
| [POR009](#por009) | ✅ | ✅ | ✅ | ✅ | — | **PASS** |
| [POR010](#por010) | ✅ | ✅ | ✅ | ✅ | — | **PASS** |
| [POR011](#por011) | ✅ | ✅ | ✅ | ✅ | — | **PASS** |
| [POR012](#por012) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |
| [POR013](#por013) | ✅ | ✅ | ✅ | ✅ | ✅ | **PASS** |

✅ = fully met. ○ = present but implicit / could be added cheaply.

**Six of the twelve rules now ship a code fix** — POR001, POR003, POR004, POR005, POR006, POR012 and
POR013. POR001, POR003 and POR005 gained theirs in POR-122, which took this document's own finding as
its brief: it singled POR005 out as the cheapest fix available, because the analyzer already computes
the corrected route. The six that ship none are listed with their reasons in
[analyzer-rules.md](../reference/analyzer-rules.md) — in every case the correction requires a decision
the analyzer cannot see, and a fix that guesses is worse than none.

No message rewrites were required. Two rules (POR001, POR004) were rewritten in `14d9beb`; the rest
were written to this standard from the start. The prior commit improved two messages without
recording the standard it applied — this document records it.

> **This table used to stop at POR011, and the omission was not caught by reading.** POR012 shipped
> and simply never appeared here; POR013 shipped and got a parenthetical in the header saying it
> "has not been assessed", which is how an unassessed rule looks when someone notices the gap but
> not the shape of it. `PorticoAnalyzerDocs_Should.Assess_Every_Live_Rule_In_The_Message_Audit` now
> compares this table against the analyzers' own `SupportedDiagnostics`, so a rule cannot ship
> unassessed again — the same guard POR-158 built for the README and the agent asset, extended to
> the file it did not cover.

---

## POR001 — Route placeholder does not match any parameter

**Message format:**
```
Route placeholder '{target}' on 'Deploy' binds to a parameter of the same name, but 'Deploy' has
none called 'target'. Available parameters: environment. Rename the placeholder to one of those,
or add a parameter 'target'.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the mismatched placeholder and the method it is on. |
| Why | ✅ "binds to a parameter of the same name" — states the binding model. |
| Fix | ✅ "Rename the placeholder to one of those, or add a parameter 'target'" — two concrete actions. Lists available parameter names so the user knows what to rename to. |
| Names | ✅ Placeholder name, method name, all available parameter names. |

**Code fix:** ✅ `RoutePlaceholderCodeFix` (POR-122). This entry used to read *"None. The analyzer
cannot know which of the available parameters the placeholder was meant to match, so an automated fix
would need to guess."* The premise was right and the conclusion did not follow: a fix does not have to
choose. It offers **one rename action per available parameter, plus an add-parameter action**, which is
the ordinary Roslyn shape for a rename suggestion and leaves the decision where it belongs.

**Verdict: PASS.** Rewritten in `14d9beb`. The key improvement was listing the available parameters —
without that, an agent reading the message has the problem but not the vocabulary of the solution.

---

## POR002 — Duplicate route on one type

**Message format:**
```
Route 'init' is declared twice on the same type, by both 'InitA' and 'InitB'. One of them can never
be reached — rename one, or give it a distinct subcommand prefix.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the duplicated route and both methods that declare it. |
| Why | ✅ "One of them can never be reached" — states the consequence. |
| Fix | ✅ "rename one, or give it a distinct subcommand prefix" — two concrete actions. |
| Names | ✅ Route string, both method names. |

**Code fix:** None. The analyzer cannot know which of the two methods the user wants to keep, or what
the renamed route should be. Not automatable.

**Verdict: PASS.** Clean from inception.

---

## POR003 — Malformed option spec

**Message format:**
```
[CliOption] spec 'verbose' is invalid: alias 'verbose' is missing a leading '-' (use '-x' for short,
'--name' for long). Valid form is a pipe-separated list of dash-prefixed aliases (e.g. "--verbose|-v").
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the invalid spec and carries a targeted sub-reason from `ValidateSpec`. |
| Why | ✅ The sub-reason explains why (e.g. "is missing a leading '-'", "is just a dash with no name", "is reserved as the POSIX end-of-options terminator"). |
| Fix | ✅ Shows the valid form with an example (`"--verbose\|-v"`). Individual sub-reasons include parenthetical guidance (`"use '-x' for short, '--name' for long"`). |
| Names | ✅ The invalid spec string, the specific alias within it that is malformed. |

**Code fix:** ✅ **partial** — `CliOptionSpecCodeFix` (POR-122), registered only for the two branches
that carry unambiguous intent: an undashed alias (`"verbose"` → `"--verbose"`, or `"-v"` for a single
character) and an empty segment from a leading, doubled or trailing pipe (`"--verbose|"` →
`"--verbose"`). For an empty spec, a whitespace-only one, and a bare `"-"` or `"--"` **no action is
registered at all** — there is no name to recover, and a fix that guesses is worse than no fix, because
the user accepts it without reading. The repair is decided in the analyzer beside the validator and
re-validated before it is offered, so it can never hand back a spec the rule still reports.

The original entry read: None. The sub-reason mechanism has seven distinct branches; a code fix would need to
know the intended alias form. Could be done for the "missing leading '-'" case (prepend `--`), but the
other branches have ambiguous intent.

**Verdict: PASS.** The `ValidateSpec` sub-reason mechanism is the strongest diagnostic shape in the
analyzer suite — seven targeted messages, each explaining both what is wrong and what correct
looks like. Clean from inception.

---

## POR004 — Missing example on a route method

**Message format:**
```
Method 'Init' is decorated with [CliRoute] but has no [CliCommandExample]. Add one — e.g.
[CliCommandExample("<the command as a user would type it>")] — it both documents the command and
becomes an executable CliContractValidator<T> test.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the method and what it is missing. |
| Why | ✅ "it both documents the command and becomes an executable CliContractValidator&lt;T&gt; test" — explains why examples exist. |
| Fix | ✅ Shows the exact attribute to add, with a placeholder for the command string. |
| Names | ✅ Method name. |

**Code fix:** ✅ `MissingCommandExampleCodeFix` inserts a stub `[CliCommandExample("TODO")]` that
compiles. One keystroke in the IDE lightbulb menu.

**Verdict: PASS.** Rewritten in `14d9beb`. The key improvement was the inline example of the
attribute — an agent can copy it literally. Combined with the code fix, this is the most actionable
rule in the suite.

---

## POR005 — Argument has no matching route placeholder

**Message format:**
```
[CliArgument] on parameter 'src' of 'Copy' has no matching placeholder in the route "cp {dest}".
Put the argument in the route: [CliRoute("cp {dest} {src}")].
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the parameter and the route it does not appear in. |
| Why | ○ Implicit — the message says there is no matching placeholder but does not explain that `[CliRoute]` defines argument positions. At Error severity, the corrected route makes the binding model obvious enough. |
| Fix | ✅ **Generates the corrected route string** — `[CliRoute("cp {dest} {src}")]` is a copy-pasteable code fragment. The strongest fix in the analyzer suite. |
| Names | ✅ Method name, parameter name, current route, corrected route. |

**Code fix:** ✅ `CliArgumentRouteCodeFix` (POR-122) — **this document's own recommendation, now
shipped.** The note below is preserved because it is the brief POR-122 was written from; it was right,
and it was the cheapest fix in the suite. One detail was tightened in the doing: the corrected route is
**recomputed** in the fix rather than read back out of the formatted message, because a fix coupled to
message wording would break the first time this document's advice to reword one was taken.

The original entry read: None, but **cheap to add**. The corrected route string is already computed in the
analyzer (`$"{route} {{{parameterName}}}".Trim()`). A code fix that replaces the `[CliRoute]`
argument with the suggested string would be straightforward — comparable in complexity to the
existing `MissingCommandExampleCodeFix`.

**Verdict: PASS.** The generated fix code fragment makes the implicit "why" a non-issue. An agent
reading this message can apply the fix without understanding the binding model.

---

## POR006 — CliOptions bundle needs a public parameterless constructor

**Message format:**
```
'DeployOptions' extends CliOptions but lacks a public parameterless constructor. Option bundles are
instantiated per-invocation via Activator.CreateInstance — move dependencies out of the constructor
or expose them as [CliOption] properties.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the type and what it is missing. |
| Why | ✅ "instantiated per-invocation via Activator.CreateInstance" — explains the mechanism. |
| Fix | ✅ "move dependencies out of the constructor or expose them as [CliOption] properties" — two concrete actions appropriate to the likely cause (a constructor that takes services). |
| Names | ✅ Type name, base type name ("CliOptions"). |

**Code fix:** ✅ `BundleMissingCtorCodeFix` inserts a `public TypeName() { }` constructor.

**Note:** The analyzer correctly **exempts `CliMiddleware`** (line 55 of `BundleCtorAnalyzer.cs`),
even though it inherits from `CliOptions`, because middleware is user-constructed and cloned, never
`Activator`-constructed. The description field on the diagnostic documents this exemption.

**Verdict: PASS.** Clean from inception.

---

## POR008 — Invalid return type on a route method

**Message format:**
```
Method 'Run' is decorated with [CliRoute] and returns 'void'. Only 'int' and 'Task<int>' are
supported — return your exit code (0 = success, 1 = runtime error, 2 = usage error, 130 = cancelled)
or throw CliExitException for error paths.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the method and its current (invalid) return type. |
| Why | ✅ "Only 'int' and 'Task&lt;int&gt;' are supported" — states the constraint. The exit code semantics (0/1/2/130) explain *why* the return type carries a value. |
| Fix | ✅ Lists the two valid return types, the four conventional exit codes, and `CliExitException` as the error-path alternative. |
| Names | ✅ Method name, current return type as displayed by Roslyn. |

**Code fix:** None. Changing a return type is non-trivial — it requires rewriting the method body's
`return` statements. Not cheap.

**Verdict: PASS.** Clean from inception. The exit-code table in the message is unusually helpful for
agents, which tend to return `0` unconditionally unless told otherwise.

---

## POR009 — Two options declare the same alias

**Message format:**
```
Option alias '--name' is declared by both parameter 'service' and parameter 'cluster'. Each alias
must be unique per command — two options binding the same alias would silently receive the same value
at dispatch. Rename one, or give it a distinct alias.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the duplicated alias and both declarations that claim it. |
| Why | ✅ "two options binding the same alias would silently receive the same value at dispatch" — explains the consequence (silently shared state). |
| Fix | ✅ "Rename one, or give it a distinct alias" — concrete action. |
| Names | ✅ Alias, both owner descriptions. |

**Code fix:** None. The analyzer cannot know which of the two options the user wants to rename, or
what the new alias should be. Not automatable.

**Verdict: PASS.** Clean from inception. The description field additionally documents the case
sensitivity rule (single-char short aliases are case-sensitive, longer aliases are case-insensitive),
which is a subtlety an agent would otherwise not know.

---

## POR010 — Option type cannot be converted from a command-line string

**Message format:**
```
Option '--money' has type 'Money', which cannot be converted from a command-line string. Everything a
user types is a string: give 'Money' a [TypeConverter] that converts from string, or use a type that
already has one (a primitive, enum, string, TimeSpan, Guid, Uri, DateTime, or a collection of those).
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the option and the unconvertible type. |
| Why | ✅ "Everything a user types is a string" — explains the fundamental constraint. |
| Fix | ✅ Two concrete actions: add a `[TypeConverter]`, or switch to a supported type. Lists the full set of out-of-the-box convertible types. |
| Names | ✅ Option alias (or member name), type display name. |

**Code fix:** None. The two fixes (write a custom TypeConverter or change the type) both require
understanding intent. Not automatable.

**Note:** The rule is deliberately conservative — it fires only for types declared in the user's own
compilation, not referenced types, because `TypeDescriptor`'s intrinsic converter table is invisible
to Roslyn and a false positive at Error severity would break a working build.

**Verdict: PASS.** Clean from inception.

---

## POR011 — Route declares the same placeholder twice

**Message format:**
```
Route "copy {path} {path}" on 'Copy' repeats placeholder '{path}'. Each placeholder name must appear
once — use distinct names for distinct positions (e.g. '{src}' and '{dst}').
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the repeated placeholder, not merely "a duplicate placeholder". |
| Why | ✅ The description field explains the consequence: both slots resolve to one parameter and the second value overwrites the first at dispatch. |
| Fix | ✅ Shows the corrected shape inline (`'{src}'` and `'{dst}'`) rather than describing it. |
| Names | ✅ Route string, method, placeholder name. |

**Code fix:** None. Renaming a placeholder means renaming the bound parameter too, and only the
author knows which position deserves which name.

**Note:** This rule guards the framework's own verification mechanism. A repeated placeholder is
silent data loss that `CliContractValidator` cannot catch — the example still dispatches, so it
reports a pass while one of the two values is discarded. That makes it the one rule whose absence
would produce a *false green* rather than a runtime error, which is why it is Error severity
despite the runtime already rejecting it at `CliApplication.Create`.

**Verdict: PASS.** Clean from inception.

---

## POR012 — `[CliOption]` on a `bool` is probably meant to be a switch

**Message format:**
```
Option '--force' is a 'bool', which reads a VALUE — a user must type '--force true'. For an
ordinary presence-only switch ('--force' on its own), declare it 'CliFlag? force = null'. If a
two-state value is what you meant, this warning is safe to suppress.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ States the type and its consequence ("reads a VALUE"), not merely "wrong type". |
| Why | ✅ In the framework's own model: value option vs. presence-only, spelled out in the description as "absent and false collapse into the same answer". |
| Fix | ✅ Gives the replacement declaration as a correct fragment, parameter name substituted. |
| Names | ✅ Alias, declared type, and parameter name. |

**Code fix:** `BoolUsedAsSwitchCodeFix` — rewrites the parameter to `CliFlag? … = null`.

**Note:** The only `Warning` in the audit alongside POR013, and the message carries the escape
hatch in its last sentence ("safe to suppress"). That sentence is load-bearing rather than
decorative: this repository and the `portico-cli` template both set `TreatWarningsAsErrors`, so for
a scaffolded project the warning *is* a build failure, and a user who genuinely meant a two-state
option needs to be told so at the point of failure.

**Verdict: PASS.** Clean from inception.

---

## POR013 — A `catch` clause swallows `CliExitException`

**Message format:**
```
This 'catch (Exception ex)' in command handler 'Migrate' swallows CliExitException, so a controlled
exit is downgraded to whatever the handler returns — a failed command can exit 0. Add
'when (ex is not CliExitException)', or rethrow it.
```

| Criterion | Assessment |
|-----------|------------|
| What | ✅ Names the construct and what it destroys, not "possible exception handling issue". |
| Why | ✅ States the observable consequence in one clause — "a failed command can exit 0" — which is the whole reason the rule exists. |
| Fix | ✅ Two fixes, both as fragments: the exception filter, or a rethrow. |
| Names | ✅ The catch clause and the enclosing handler. |

**Code fix:** `SwallowedCliExitExceptionCodeFix` — adds the `when (ex is not CliExitException)`
filter.

**Note:** The description is explicit that the analyzer sees the handler body only, so an exception
swallowed several frames deep is out of reach. Saying so in the message is what stops a user reading
silence as "no such problem here" — a rule that closes the common case and admits it beats one that
implies coverage it does not have.

**Verdict: PASS.** Clean from inception.

---

## Where code fixes would be cheap to add

Four rules already ship code fixes:

| Rule | Code fix | What it does |
|------|----------|-------------|
| POR004 | `MissingCommandExampleCodeFix` | Inserts a `[CliCommandExample("TODO")]` stub |
| POR006 | `BundleMissingCtorCodeFix` | Inserts a `public TypeName() { }` constructor |
| POR012 | `BoolUsedAsSwitchCodeFix` | Rewrites a `bool` option to `CliFlag? … = null` |
| POR013 | `SwallowedCliExitExceptionCodeFix` | Adds `when (ex is not CliExitException)` to the catch |

One rule has a cheap opportunity:

| Rule | Opportunity | Effort |
|------|------------|--------|
| POR005 | Replace the `[CliRoute]` argument with the corrected route string (already computed by the analyzer) | Low — the shape is identical to `MissingCommandExampleCodeFix` |

The remaining seven rules involve ambiguous intent (POR001, POR002, POR009, POR011), structurally different
fixes per sub-case (POR003), return-type changes that require method-body rewrites (POR008), or
whole-type rewrites (POR010). None is cheap. (Four shipped + one cheap + seven not cheap = the
twelve live rules; POR007 is retired.)

---

## Part 2 — measurement (outstanding)

Part 1 of POR-49 is complete: written verdicts for all twelve live rules, no rewrites required. Part 2
— giving an agent deliberately-broken contracts and measuring first-pass fix rate under three arms
(current messages, rewritten messages, analyzers suppressed) — is not started. Because no messages
were rewritten, the arm A/arm B distinction in Part 2 collapses: there is no "before" and "after" to
compare. The remaining useful measurement is arm A (with analyzers) vs arm C (without), which
quantifies the value of the analyzer suite as a whole rather than the quality of individual messages.

The harness shape to follow is POR-42's
([`agent-grounding-benchmark.md`](agent-grounding-benchmark.md),
[`agent-grounding-benchmark-specs.json`](../reference/agent-grounding-benchmark-specs.json),
[`agent-grounding-benchmark-results.jsonl`](../reference/agent-grounding-benchmark-results.jsonl)).
