# Documentation

Portico's docs follow the [Diataxis](https://diataxis.fr/) framework: four quadrants, each serving a
different need. Start with the tutorial if you are new; reach for reference when you need the answer
to a specific question.

## Tutorial — learning-oriented

| | |
|---|---|
| [Build your first Portico CLI](tutorial/first-cli.md) | Install, scaffold, run, test, break — fifteen minutes to a green contract test |

## How-to — goal-oriented

| | |
|---|---|
| [Your first operational command](how-to/operational-command.md) | The niche, demonstrated: env fallback, redaction, durations, exit codes, drain-on-SIGTERM |
| [Composing CLIs](how-to/compose-clis.md) | Mount several contracts into one binary |
| [Migrating from Cocona](how-to/migrate-from-cocona.md) | The concept mapping, and when another framework is the better move |

## Reference — information-oriented

| | |
|---|---|
| [Capabilities](reference/capabilities.md) | The whole surface, every entry backed by a test |
| [Analyzer rules](reference/analyzer-rules.md) | Every live compile-time check, and how to suppress one |

## Explanation — understanding-oriented

| | |
|---|---|
| [Extensibility](explanation/extensibility.md) | What you can extend, and what is deliberately sealed |
| [AOT](explanation/aot.md) | Why not, and what would change our mind |
| [The alternatives, honestly](explanation/alternatives.md) | What every competitor is better at, and the one claim we make |
| [The agent-first CLI contract, scored](explanation/agent-first-contract.md) | What Portico answers, what it declines, and why |

## Project internals and decisions

These documents explain how Portico itself is governed and evaluated. They are useful when
contributing or assessing the project's direction; they are not prerequisites for using the API.

| | |
|---|---|
| [Charter](explanation/charter.md) | The design constitution, and the invariants it will not trade |
| [Public surface classification](explanation/public-surface.md) | Every exported type, classified |
| [Analyzer message audit](explanation/analyzer-message-audit.md) | Actionability assessment of every live diagnostic |
| [Agent grounding benchmark](explanation/agent-grounding-benchmark.md) | Does shipping the guide help? A measured answer |
| [Roadmap](ROADMAP.md) | The parked list — what is deliberately not being built, and why |
