# ADR 0001: Project name and foundation

- Status: accepted
- Date: 2026-09-05

## Decision

Use **LumeFetch** as the working product and repository name. Build the desktop
client on .NET 10 and Avalonia 12 with a UI-independent core, infrastructure
adapters, and a thin desktop composition root.

## Rationale

The name is short, pronounceable in Turkish and English, and does not tie the
product to video, a specific platform, or a single extraction backend. Keeping
the product name out of provider IDs and persisted contract values makes a
future rename manageable.

.NET 10 is the active LTS line and provides the compiler required by Avalonia
12's current source generators. The dependency direction keeps later Linux and
macOS packaging practical.

## Consequences

- Public namespaces begin with `LumeFetch`.
- Provider IDs use stable source-oriented values such as `generic-http`.
- Platform APIs cannot leak into `LumeFetch.Core`.
- A trademark and domain review is still required before a public launch.
