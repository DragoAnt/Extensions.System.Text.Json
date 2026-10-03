# Agent skills

The [`skills/`](../skills) folder holds three [Agent Skills](https://agentskills.io) that teach an AI coding agent to use these packages correctly — the rules, the defaults that surprise people, and runnable examples. Each skill is a folder with a `SKILL.md` (a `name` and a `description` in its frontmatter, then plain Markdown) and companion files the agent opens when it needs them. They use no agent-specific features, so any agent that reads `SKILL.md` files can use them.

| Skill | Use it for |
| --- | --- |
| [`json-observer-masking`](../skills/json-observer-masking/SKILL.md) | writing masking or extraction rules with `DragoAnt.System.Text.Json.Observer`, choosing a default policy, shapes from DTOs, cut-off bodies, the UTF-8 hot path, upgrading from 1.x |
| [`json-observer-http-logging`](../skills/json-observer-http-logging/SKILL.md) | logging masked `HttpClient` bodies with `DragoAnt.System.Text.Json.Observer.Http` — registration, masker providers, sinks, body markers |
| [`json-observer-testing`](../skills/json-observer-testing/SKILL.md) | xUnit tests proving the masking never leaks: golden, secret-absent, fuzz, allocation, concurrency and HTTP logging tests |

Every C# block in the skills compiles and runs in this repository's test suite, against the source of the version they ship with.

## Install

Copy (or symlink) the skill folders into the directory your agent reads. `<name>` is a folder from `skills/`; copy all three unless you want fewer.

**`.agents/skills/` in your repository is read by Codex, GitHub Copilot, Cursor, Gemini CLI and Antigravity**; Claude Code reads `.claude/skills/`. Commit the folder to share the skills with your team, or use the personal folder to have them in every project.

| Agent | Project folder | Personal folder |
| --- | --- | --- |
| Claude Code | `.claude/skills/<name>/` | `~/.claude/skills/<name>/` |
| OpenAI Codex | `.agents/skills/<name>/` (any folder from the working directory up to the repository root) | `~/.agents/skills/<name>/` |
| GitHub Copilot (CLI, VS Code, coding agent) | `.github/skills/<name>/`, `.agents/skills/<name>/` or `.claude/skills/<name>/` | `~/.copilot/skills/<name>/` or `~/.agents/skills/<name>/` |
| Cursor | `.agents/skills/<name>/` or `.cursor/skills/<name>/` (also reads `.claude/skills/` and `.codex/skills/`) | `~/.agents/skills/<name>/` or `~/.cursor/skills/<name>/` |
| Gemini CLI | `.gemini/skills/<name>/` or `.agents/skills/<name>/` | `~/.gemini/skills/<name>/` or `~/.agents/skills/<name>/` |
| Antigravity | `.agents/skills/<name>/` | `~/.gemini/config/skills/<name>/` (Antigravity CLI: `~/.gemini/antigravity-cli/skills/<name>/`) |

Paths were checked against each agent's documentation on 2026-10-03; agents add locations over time, so check yours if a skill does not show up.

### Copy the skills into a repository

From the root of your repository (shell):

```sh
git clone --depth 1 --filter=blob:none --sparse https://github.com/DragoAnt/Extensions.System.Text.Json.git .skills-src
git -C .skills-src sparse-checkout set skills
mkdir -p .agents/skills
cp -R .skills-src/skills/. .agents/skills/
rm -rf .skills-src
```

PowerShell:

```powershell
git clone --depth 1 --filter=blob:none --sparse https://github.com/DragoAnt/Extensions.System.Text.Json.git .skills-src
git -C .skills-src sparse-checkout set skills
New-Item -ItemType Directory -Force .agents/skills | Out-Null
Copy-Item -Recurse -Force .skills-src/skills/* .agents/skills/
Remove-Item -Recurse -Force .skills-src
```

For Claude Code, use `.claude/skills` instead of `.agents/skills`. Gemini CLI can also install from a Git repository with `gemini skills install`, and link a local folder with `/skills link <path>`.

### An agent without skill support

Paste the `SKILL.md` into the conversation, or add one line to the instructions file the agent reads (`AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `.github/copilot-instructions.md`):

```text
Before writing code with DragoAnt.System.Text.Json.Observer, read .agents/skills/json-observer-masking/SKILL.md and follow its links when needed.
```

## Update

The skills change with the library. Re-run the copy above after upgrading the package, and pin the source to the version you use by cloning its release tag: add `--branch v<version>` to the `git clone` line. If you symlinked a clone instead of copying, `git pull` (or `git checkout v<version>`) in that clone updates every project that links to it.

## Contributing

Skills live in `skills/<name>/`. Keep `SKILL.md` short (when to use it, the decision path, the rules that matter, pitfalls) and put detail in companion files linked with `./`. Every `csharp` block must be a complete program whose `// Output:` comment is what it prints, or a complete xUnit test class that passes; mark a deliberate fragment with `<!-- doc-test: skip -->` on the line above it. `DragoAnt.System.Text.Json.Observer.Skills.Tests` compiles and runs them all, and checks the frontmatter and the relative links.
