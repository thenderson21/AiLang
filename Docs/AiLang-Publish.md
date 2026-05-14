# AiLang Publish Command

Status: alpha bootstrap contract.

## Command

```bash
ailang publish <project-dir> [--mode <mode>] [--target <rid>] [--kind <kind>] [--out <dir>]
```

Defaults:

- `--mode framework-dependent`
- `--kind posix`
- `--out <project-dir>/dist`

Supported alpha modes:

- `framework-dependent`: emits app payload and launchers that resolve installed
  `aivm`.
- `self-contained`: emits app payload, launchers, and a bundled target-specific
  `aivm`. This mode requires `--target`.

Supported target IDs:

- `host`
- `osx-arm64`
- `osx-x64`
- `linux-x64`
- `linux-arm64`
- `win-x64`
- `win-arm64`
- `wasm-browser`
- `wasm-wasi`

For self-contained alpha publishing, runtimes are resolved from the selected
SDK:

```text
<toolchain-root>/runtimes/<target>/aivm
```

`host` resolves to `<toolchain-root>/runtimes/host/aivm`, falling back to the
locally available AiVM binary during local bootstrap work. `AILANG_RUNTIME_ROOT`
is a temporary override for testing or release packaging; when set, publish
looks under `$AILANG_RUNTIME_ROOT/<target>/`. Runtime resolution accepts both
`aivm` and `aivm.exe`, so one SDK can publish Windows and non-Windows targets.

If the target is known but no staged runtime exists, publish returns
`AILANG019`.

Supported alpha kinds:

- `posix`: Unix prefix-style app layout.
- `target-preferred`: accepted as a contract term and currently resolves to
  `posix` for alpha CLI projects.

## POSIX Layout

Framework-dependent:

```text
dist/
  bin/
    app
    app.cmd
  lib/
    ailang/
      app/
        app.aibe
        ailang.publish.toml
```

Self-contained:

```text
dist/
  bin/
    app
    app.cmd
  lib/
    ailang/
      app/
        app.aibe
        ailang.publish.toml
        runtime/
          aivm
```

For Windows targets, the bundled runtime is emitted as `runtime/aivm.exe`.
`bin/app` is the POSIX launcher. `bin/app.cmd` is emitted for non-POSIX launch
scenarios in framework-dependent and self-contained output. The payload is
stored as `app.aibe`; it is not the user-facing executable.

The current implementation marks POSIX launchers and bundled runtimes
executable by invoking the host `chmod` command.

## Validation

`<project-dir>` must contain `project.aiproj`. The manifest must declare a
non-empty `entryFile` and `entryExport`, and `entryFile` must point at an
existing source file under the project directory.

During the alpha bootstrap phase, `publish` still delegates source compilation
to the same bootstrap compiler resolution path used by `ailang build`, then
assembles the publish layout in the AiLang-authored CLI.

Publish follows the same reachable-code rule as build. Framework-dependent
publish emits the app payload and launchers only. Self-contained publish emits
the app payload, launchers, explicitly declared publish assets, and the runtime
for the selected target only. It must not include all SDK runtimes, all restored
package sources, or unused package assets.

## Errors

- Missing path returns `AILANG001`.
- Missing `project.aiproj` returns `AILANG007`.
- Missing or empty `entryFile` returns `AILANG008`.
- Missing or empty `entryExport` returns `AILANG009`.
- Missing entry source file returns `AILANG010`.
- Bootstrap compiler build failure returns `AILANG014`.
- Unsupported publish mode returns `AILANG016`.
- Self-contained publish without `--target` returns `AILANG017`.
- Unsupported publish kind returns `AILANG018`.
- Missing bundled `aivm` for self-contained publish returns `AILANG019`.
- Unknown publish target returns `AILANG020`.
