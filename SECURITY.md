# Security Policy

## Supported versions

Portico is **0.x**. Only the **latest released version** is supported. There are no long-term-support
branches and no backports: if a fix is needed, it ships in the next release, and you upgrade. When
1.0 arrives this policy will be revised — it will be stated here before it is relied upon.

| Version | Supported |
|---|---|
| latest 0.x | ✅ |
| any earlier 0.x | ❌ |

## Reporting a vulnerability

**Do not open a public issue for a security problem.**

Report it privately, either way:

- **GitHub private vulnerability reporting** — use the **Report a vulnerability** button on the
  repository's *Security* tab. This is the preferred route; it keeps the report, the discussion and
  the eventual advisory in one place.
- **Email** — <alexey.evlampiev@gmail.com>, subject line starting with `SECURITY:`.

Please include what you would want if you were on the receiving end: the version, what an attacker
can do, and the smallest reproduction you have.

## What to expect

Portico is maintained by **one person**, so here is the honest version rather than a service-level
agreement nobody is on call to honour:

- I will acknowledge a report within **7 days**.
- I will tell you whether I consider it a vulnerability, and why, rather than going quiet.
- A confirmed vulnerability is fixed in a release, and credited to you in the advisory and the
  changelog unless you would rather it were not.

If you do not hear back within 7 days, assume the mail went astray and open a GitHub issue saying
only *"sent a security report, no reply"* — with **no details** — to prompt me.

## Scope

Portico is a CLI framework: it parses argv, routes to your methods, and binds values. The parts worth
your attention are the ones that read untrusted input or write output that ends up in a log:

- argument and option parsing (a crafted command line);
- type conversion and the option materializer;
- what the framework echoes back — help, errors, trace and timing output. Values of options marked
  `Sensitive = true` are redacted there, and **a leak of a sensitive value into any framework-emitted
  string is a vulnerability**, not a cosmetic bug. Two have been found and fixed already.

Out of scope: what *your* command handlers do with the values they are handed. That code is yours.

## How a release is authorised

Worth stating, because "where did this package come from" is a fair question to ask of any dependency.

- **No long-lived publishing credential exists.** Packages are pushed with
  [nuget.org Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
  `.github/workflows/release.yml` exchanges a short-lived GitHub OIDC token for an API key valid for
  one hour, one use. There is no `NUGET_API_KEY` secret in the repository and there should never be
  one.
- **Publishing is gated on a GitHub environment.** The `publish` job runs in the `release`
  environment, so the OIDC token carries that claim and the nuget.org policy is scoped to it — the
  authorisation is not merely "can push a `v*` tag".
- **Nothing is published without the full verify job passing**, on both supported TFMs and on both
  Linux and Windows. `release.yml` calls the same reusable workflow CI calls on every push.
- **A nuget.org version is immutable, and the release path treats it that way.** `--skip-duplicate` is
  deliberately off: a corrective re-tag must fail loudly rather than silently leave the original
  artifacts being served.
- Builds are deterministic with Source Link and embedded untracked sources, so a published assembly
  can be traced back to the commit it came from.
