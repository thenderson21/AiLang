# AiLang Init

Status: alpha command contract.

`ailang init` creates a new AiLang project from installed SDK template files.
Template content is rendered by AiLang code. Missing template assets are errors;
there are no built-in fallback templates.

## Commands

```bash
ailang init <path> [--template <name>] [--agent <name>] [--agents <list|all>] [--force]
ailang template list
ailang template show <name>
ailang template path <name>
ailang agent list
```

## Templates

Installed project templates are registered in:

```text
templates/projects/index.toml
```

Template assets live under:

```text
templates/projects/<name>
```

Each project template must include:

```text
template.toml
project.aiproj.tpl
src/app.aos.tpl
AGENTS.md.tpl
.gitignore.tpl
```

The current SDK registry includes:

- `cli`
- `cli-args`

Template variables currently rendered:

- `{{project.name}}`
- `{{project.version}}`
- `{{project.entryFile}}`
- `{{project.entryExport}}`

## Agents

Default agent behavior is Codex-oriented through the generated `AGENTS.md`.
Additional agent files can be requested with `--agent` or `--agents`.

Supported agents:

- `codex`
- `claude`
- `cursor`
- `gemini`
- `copilot`
- `windsurf`

`--agents all` emits all non-default agent instruction files.

## Errors

- Missing path returns `AILANG001`.
- Unknown command returns `AILANG002`.
- Existing directory without `--force` returns `AILANG003`.
- Unknown template returns `AILANG004`.
- Missing installed template files return `AILANG005`.
- Unknown agent returns `AILANG006`.
