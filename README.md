# brainyz

[![CI](https://github.com/FavitoX/brainyz/actions/workflows/ci.yml/badge.svg)](https://github.com/FavitoX/brainyz/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

> Your technical memory, global by default.

A local-first knowledge layer for developers who work with AI agents.
Records decisions, principles, and reference notes once — retrievable
from any project, by any agent, for as long as you want to remember.

```bash
brainz add decision "Use LibSQL over SQLite for sync-ready vectors"
brainz search "resilience patterns"
brainz link <id-a> relates_to <id-b>
```

---

## Why

If you work with AI coding agents like Claude Code, you've probably hit this:

- You explain the same architectural decision to a new chat, again.
- You re-derive the same tradeoff you already settled three months ago.
- You paste the same "we do things this way" context into a dozen CLAUDE.md files.
- When you finally want to know *why* past-you chose X, the answer is gone.

Existing memory tools (Engram, Mem0, most MCP memory servers) are **per-project**
or **per-session**. They live inside the context they serve, which is backwards.
Your knowledge should be the center; your projects should be filtered views of it.

**brainyz** inverts the model: one global database, scoped filtering by project
when you want it. Decisions carry across repos. Principles apply everywhere
unless you say otherwise. Notes stay searchable forever.

## What's in it

Three kinds of content, each with clear semantics:

| Entity | Answers | Example |
|---|---|---|
| **Decision** | Why did we choose X over Y? | *"Chose LibSQL over SQLite because we need native vectors and sync"* |
| **Principle** | What rule do we always apply? | *"Auth mandatory on every handler"* |
| **Note** | What reference info do I need? | *"Stripe v2024-01 changed the refunds endpoint behavior"* |

Everything is:

- **Tagged** (hierarchical paths like `ailang/auth`, `payments/idempotency`).
- **Linked** (decisions supersede each other, principles derive from decisions,
  notes inform decisions — a real knowledge graph).
- **Searchable** both by text (FTS5) and by meaning (local embeddings via Ollama).
- **Versioned** (every edit is kept — you can always ask "what did this say last month?").
- **Portable** (it's a single LibSQL file you can copy, back up, or sync).

## Usage

```bash
# Register the current repo as a project (also writes .brain file)
brainz init --project ailang --name "AILang"

# Add things. The body comes from --text or from stdin.
brainz add decision "Use LibSQL" --text "Embedded LibSQL with native vectors" --confidence high
echo "Auth is mandatory on every handler" | brainz add principle "Auth mandatory"
brainz add note "Stripe refunds changed" --text "Returns 202, requires polling" --source https://stripe.com/changelog/2024-01

# Find things (hybrid FTS5 + semantic; --text-only forces FTS5 only)
brainz search "retry policies"
brainz list decisions
brainz show <id>

# Connect things
brainz link <decision-id> informed_by <note-id>
brainz link <principle-id> derived_from <decision-id>

# Debug scope resolution
brainz scope                      # prints: source / project / hint

# Run as an MCP server (stdio) — exposes 11 tools to Claude Code / Cursor / …
brainz mcp
```

Register with Claude Code once (globally):

```bash
claude mcp add brainyz --scope user brainz mcp
```

From then on, every Claude Code session in any repo has tools like
`search`, `find_similar`, `get_principles`, `add_decision`, etc.

### Configuration via environment

| Variable | Default | Purpose |
|---|---|---|
| `BRAINYZ_CONFIG_DIR` | platform default | Override where `brainyz.db` and `config.toml` live. |
| `BRAINYZ_OLLAMA_HOST` | `http://localhost:11434` | Alternate Ollama endpoint. |
| `BRAINYZ_EMBEDDING_MODEL` | `nomic-embed-text:v1.5` | Embedding model (must match the schema's `F32_BLOB(768)` — pending config for other dims). |

## How it works

- **Storage**: single [LibSQL](https://github.com/tursodatabase/libsql) file at
  `~/.config/brainyz/brainyz.db` (or platform equivalent). SQLite-compatible;
  open it with any SQLite tool.
- **Search**: FTS5 for text matches + local vector embeddings
  ([nomic-embed-text](https://ollama.com/library/nomic-embed-text) via Ollama)
  for semantic similarity. Results fused.
- **Scope resolution**: from current directory → `.brain` file → git remote →
  config path map → global fallback.
- **MCP integration**: a single MCP server registered globally in Claude Code.
  No per-project setup.
- **Sync** (optional): your own [libsql-server](https://github.com/tursodatabase/libsql)
  on any VPS. No vendor lock-in, no subscription.

## Installation

> **brainyz is in early development.** Expect breaking changes until v1.0.

### From GitHub Releases (recommended)

Download the archive for your platform from
[Releases](https://github.com/FavitoX/brainyz/releases), extract, and put
`brainz` on your `PATH`.

Supported runtimes: `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`.

Then pull the embedding model once:

```bash
ollama pull nomic-embed-text
```

(Ollama is optional — the CLI and MCP server work without it, but search falls
back to FTS5 only. Entries added while Ollama is down are picked up by
`brainz reindex` once it's back.)

### From source

Requires: .NET 10 SDK, and a platform C/C++ toolchain for NativeAOT
([prerequisites](https://aka.ms/nativeaot-prerequisites)).

```bash
git clone https://github.com/FavitoX/brainyz
cd brainyz
dotnet publish src/Brainyz.Cli -c Release -r <your-runtime>
# Copy the binary to your PATH as `brainz`
```

## Project status

**v0.1 — core functional, wiring up release.**

- CLI: `init`, `scope`, `add (decision|principle|note)`, `list`, `search`,
  `show`, `link`, `reindex`, `mcp`. ✅
- MCP server: 11 tools over stdio — `search`, `find_similar`, `get`,
  `get_principles`, `list_projects`, `get_links`, `resolve_scope`,
  `add_decision`, `add_principle`, `add_note`, `link`. ✅
- Embeddings: Ollama + nomic-embed-text, best-effort on writes, FTS5
  fallback when unavailable. ✅

Missing for v1.0: edit/delete commands, front-end, sync activation,
plugin SDK. See [brainyz-decisions.md](brainyz-decisions.md) for the
design trail.

If you want to follow along or try it early, watch the repo.
If you want to contribute, open an issue before a large PR — see
[CONTRIBUTING.md](CONTRIBUTING.md).

## Stack

- **Language**: C# on .NET 10 LTS, compiled with NativeAOT (single binary, no runtime).
- **Storage**: LibSQL (SQLite-compatible, native vectors, sync-ready).
- **Embeddings**: Ollama + nomic-embed-text (local, offline, Apache 2.0).
- **Protocol**: Model Context Protocol (MCP) for AI agent integration.

## License

[Apache License 2.0](LICENSE) — use it, modify it, ship it, sell it.
Just keep the notices and don't sue contributors over patents.

---

Built by [Favio Andres Leyva](https://github.com/favitox) because his brain kept
dropping things he'd already figured out.
