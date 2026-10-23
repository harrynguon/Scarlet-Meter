# Maintainer documentation

Start with [handoff](HANDOFF.md), then [development](DEVELOPMENT.md), [architecture](ARCHITECTURE.md) and [security, provenance and game terms](SECURITY.md), and read the [lessons](LESSONS.md): mistakes made so far and what now guards against them. The root [README](../README.md) is the user overview.

Update relevant documentation and the [work log](WORKLOG.md) for each meaningful change: what changed, why, actual checks, unresolved issues and next action. Keep confirmed observations separate from assumptions. Never include secrets (API keys, tokens, passwords), packet captures or private combat logs. When a mistake is found, add it to the lessons with the fix.

Rules every change follows are in [AGENTS.md](../AGENTS.md): security first, absolute paths derived from trusted roots, never edit `upstream/`, only the engine references upstream, PR plus squash merge into `main` after the audit and every review comment (Codex's included) are dealt with.
