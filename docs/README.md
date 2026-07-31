# Documentation

Portico's docs follow the [Diataxis](https://diataxis.fr/) framework: four quadrants, each serving a
different need. If you are evaluating Portico, start with [Why Portico?](explanation/why-portico.md).
If you are ready to build, start with the tutorial.

## Tutorial — learning-oriented

| | |
|---|---|
| [Build your first Portico CLI](tutorial/first-cli.md) | Install, scaffold, run, test, break — fifteen minutes to a green contract test |

## How-to — goal-oriented

| | |
|---|---|
| [Your first operational command](how-to/operational-command.md) | The niche, demonstrated: env fallback, redaction, durations, exit codes, drain-on-SIGTERM |
| [Composing CLIs](how-to/compose-clis.md) | Mount several contracts into one binary |
| [Package command capabilities](how-to/package-command-capabilities.md) | Ship team-owned contracts and implementations as assemblies, then compose an operational binary |
| [Create domain-specific options](how-to/domain-specific-options.md) | Turn derived attributes into a reusable operational vocabulary |
| [Build an operational policy middleware](how-to/operational-policy-middleware.md) | Bundle global control parameters with the policy that interprets them |
| [Migrating from Cocona](how-to/migrate-from-cocona.md) | The concept mapping, and when another framework is the better move |

## Reference — information-oriented

| | |
|---|---|
| [Capabilities](reference/capabilities.md) | The whole surface, every entry backed by a test |
| [Analyzer rules](reference/analyzer-rules.md) | Every live compile-time check, and how to suppress one |

## Explanation — understanding-oriented

| | |
|---|---|
| [Why Portico?](explanation/why-portico.md) | The architectural proposition: compile the operational interface with the .NET system it operates |
| [Extensibility](explanation/extensibility.md) | What you can extend, and what is deliberately sealed |
| [Why reflection-first](explanation/aot.md) | Why runtime discovery is a design choice, its costs, and when AOT should win |
| [The alternatives, honestly](explanation/alternatives.md) | What every competitor is better at, and where Portico's architecture differs |
| [The two agent contracts](explanation/agent-first-contract.md) | Helping agents author correct Portico code and invoke the resulting CLI safely |

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
