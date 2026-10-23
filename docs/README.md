# Maintainer documentation

Start with [handoff](HANDOFF.md), then [development](DEVELOPMENT.md), [architecture](ARCHITECTURE.md) and [security, provenance and game terms](SECURITY.md). The root [README](../README.md) is the user overview.

Update relevant documentation and the [work log](WORKLOG.md) for each meaningful change: what changed, why, actual checks, unresolved issues and next action. Keep confirmed observations separate from assumptions. Never include secrets, packet captures or private combat logs.

Rules every change follows are in [AGENTS.md](../AGENTS.md): absolute paths derived from trusted roots, never edit `upstream/`, only the engine references upstream, PR plus squash merge into `main`.
