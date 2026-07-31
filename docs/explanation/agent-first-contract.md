# The agent-first CLI contract, scored

A seven-point contract for "a CLI an agent can drive" is converging in the wild, arrived at
independently by several authors. It is not in [clig.dev](https://clig.dev) — the canonical CLI design
guide contains no mention of agents at all — so the convention is real, converging, and ownerless.

This page says which of it is Portico's business, which is not, and why. Nothing here is aspiration:
each ✅ is a behaviour with a test behind it, named.

| Rule | Portico | Where |
|---|---|---|
| Secrets stay out of the agent's context | ✅ shipped | `Sensitive = true` |
| Never hang waiting for a human who is not there | ✅ shipped | `CliPrompt` |
| Exit codes are a stable enumeration | ✅ shipped, now documented | `CliExitException` |
| stderr is not a prompt-injection channel | ✅ **new** | `CliSanitizer` |
| Ground truth about the real surface | 🟡 partial | verified `--help` shipped; machine-readable emission sanctioned (see below), not yet built |
| Structured output (`--json`) | ❌ **declined** | see below |
| Typed error envelopes | ❌ declined for now | see below |

---

## ✅ Secrets stay out of the agent's context

`[CliOption(Sensitive = true)]` redacts the value everywhere the framework echoes the command line —
trace, timing, conversion errors — and the unknown-command path prints **no option values at all**,
because no route matched and the framework cannot know which of them was a password.

It was built to keep credentials out of container logs. The same mechanism keeps them out of an
agent's transcript: an agent that runs your CLI and reads its output never sees the secret. Nothing
was added for the agent case; the shape was already right.

## ✅ Never hang waiting for a human who is not there

The failure mode this rule exists to prevent is a CLI that blocks forever on `Are you sure? [y/N]`
when nothing is attached to stdin. Portico does not:

- `CliPrompt.GetLine` returns the supplied default when input ends;
- with **no** default, it throws rather than blocking — the command cannot proceed, and it says so;
- `GetPassword` detects redirected input and reads it as an ordinary line rather than trying to drive
  a terminal.

**This was already true, and was reported as a bug anyway** — a ticket asserted a hang that does not
happen, filed from a grep without reading the method. `CliAgentContract_Should` now pins the real
behaviour, so the next person to ask gets an answer from a test rather than a guess.

## ✅ Exit codes are a stable enumeration

Retry logic belongs in the shell, not in a model's judgement. The codes are POSIX-conventional and
they are now a documented, tested contract:

| Code | Meaning |
|---|---|
| `0` | success |
| `1` | runtime error — the handler threw |
| `2` | usage error — bad route, bad option, unconvertible value |
| `130` | cancelled (SIGINT / Ctrl+C — POSIX 128+2) |
| `143` | terminated (SIGTERM — POSIX 128+15) |

A handler returns an `int`, or throws `CliExitException` with the code it wants. Changing one of these
now takes a failing test to do, which is the point.

## ✅ stderr is not a prompt-injection channel

Everything the framework echoes back — the command line you typed, the value that failed to convert —
is **attacker-influenced input**. Left raw, it can carry ANSI escapes that rewrite a terminal, or
zero-width codepoints that hide text a human reviewer cannot see **but a model still reads**.

The framework now strips control characters and invisible codepoints from the strings *it* composes
out of user input. Tabs and newlines survive; they are layout, not injection.

**The rule is: nothing survives that a reader cannot see.** Not a list of characters that have been
attacked so far — that list only ever grows one incident at a time, and the answer to *"is X covered?"*
becomes *"whoever was attacked last"*. In practice that means every Unicode **format** character
(`Cf` — which is where the zero-widths, the bidi controls behind
[Trojan Source](https://trojansource.codes/) (CVE-2021-42574), and the **tag block** all live), plus
the short list of invisible codepoints that are not `Cf`: variation selectors, the Hangul fillers and
the combining grapheme joiner.

The tag block (U+E0020–U+E007F) is worth naming. It encodes readable ASCII in codepoints that most
renderers drop and every model reads — the "invisible instructions" vector — and it sits outside the
BMP, so catching it means the sanitizer walks **runes**, not UTF-16 chars.

What this costs is confined to diagnostics: a variation selector is invisible by construction, so an
emoji in an error message may lose its colour presentation and a CJK ideograph its glyph variant.
That is the right trade in the one place where not carrying an invisible payload outranks typography —
and, again, **handler output is untouched**.

**It does not touch handler output.** A handler writes with `Console.Write*` and owns its bytes — the
handler contract is sacred, and a framework that filtered them would break every program that
deliberately emits colour. This is the framework sanitizing its own echo, nothing more.

## 🟡 Ground truth about the real surface

An agent learns a CLI by reading `--help`. Portico's help is unusually trustworthy — the examples in
it are **executed against the real contract**, so they cannot be stale — but it is prose, not a
manifest.

Whether Portico should emit a machine-readable manifest was a **Charter question**, not an engineering
one (§5 rejects DSL *input* formats; a manifest is derived *output*). It was settled on 2026-07-15:
**emission is permitted** — read-only, hard-scoped, no schema ingestion and no MCP server mode.
See Charter §5.
The manifest itself is not yet built.

## ❌ Structured output (`--json`) — declined

This is the one people will ask for, so here is the reasoning rather than a shrug.

A `--json` mode means the framework knows and shapes what a handler emits. That is a direct
contradiction of the handler contract (CHARTER §4): **a handler is a plain C# method that writes with
`Console.Write*`**. To serialize its output, the framework would have to own the output — take a
return value instead of an exit code, or intercept the console. Either is a different framework.

And it is the same mistake the Charter already refuses elsewhere: Portico does not own presentation
(§5 — that is Spectre.Console's job, and it is a *composition*). JSON is presentation. A handler that
wants JSON writes JSON:

```csharp
[CliRoute("health")]
[CliCommandExample("health --json")]
int Health([CliOption("--json")] CliFlag? json = null)
{
    var report = Probe();
    Console.WriteLine(json.HasValue ? JsonSerializer.Serialize(report) : report.ToText());
    return report.Healthy ? 0 : 1;
}
```

That is four lines, testable by an example, and it leaves the handler in charge of its own bytes.

**Reopen if** a user demonstrates something this shape genuinely cannot do.

## ❌ Typed error envelopes — declined for now

More tractable than `--json` — `CliExitException` is already the framework's error channel, so giving
it a structured rendering would not touch handler code. But it is new permanent public surface in the
window where surface is cheapest *not* to add, and **no user has asked for it**. The exit-code
enumeration above already lets a caller branch on *kind* without parsing English, which is the actual
requirement behind the rule.

**Reopen if** a real user needs to distinguish two failures that share exit code `1`.

---

Every ✅ above is covered by `test/Portico.Tests/CliAgentContract_Should.cs`. A documented claim with
no test is a claim waiting to become false.
