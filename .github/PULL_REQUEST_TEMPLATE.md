## Summary

- 

## Scope

- [ ] Language/compiler/toolset change
- [ ] Core library or SDK change
- [ ] Specification or documentation change
- [ ] Package/init/build/run/publish workflow change

## Verification

- [ ] `./test.sh`
- [ ] Installed SDK / examples validation when package or SDK behavior changed
- [ ] Specs and golden outputs updated when semantics changed

## Architecture Checklist

- [ ] No C VM/runtime/native launcher code was added to AiLang
- [ ] No new syscall was added for deterministic language or library behavior
- [ ] Generated files are not included (`.toolchain/`, `.tmp/`, `.artifacts/`,
      `app.aibc1`, local SDK files, local notes)
- [ ] No backward compatibility layer was added before the first major/minor
      release unless explicitly requested
