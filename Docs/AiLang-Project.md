# AiLang Project Command

Status: alpha.

`ailang project` exposes metadata from a project manifest. This is separate
from `ailang --version`, which reports the version of the AiLang toolset.

## Version

```bash
ailang project version <project-dir>
```

The command reads:

```text
<project-dir>/project.aiproj
```

and prints the `Project` node's `version` attribute.

Errors:

- `AILANG001`: missing project path
- `AILANG007`: `project.aiproj` was not found
- `AILANG012`: project version was missing or empty
