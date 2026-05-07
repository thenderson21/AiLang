#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

TMP_DIR="${ROOT_DIR}/.tmp/airun-init-smoke"
CLI_DIR="${TMP_DIR}/cli"
CLI_ARGS_DIR="${TMP_DIR}/cli-args"
MULTI_AGENT_DIR="${TMP_DIR}/multi-agent"
CLAUDE_DIR="${TMP_DIR}/claude"

rm -rf "${TMP_DIR}"
mkdir -p "${TMP_DIR}"

./tools/airun init "${CLI_DIR}"
./tools/airun init "${CLI_ARGS_DIR}" --template cli-args
./tools/airun init "${MULTI_AGENT_DIR}" --agents claude,cursor,gemini,copilot,windsurf
./tools/airun init "${CLAUDE_DIR}" --agent claude

test -f "${CLI_DIR}/project.aiproj"
test -f "${CLI_DIR}/AGENTS.md"
test ! -f "${CLI_DIR}/CLAUDE.md"
test -f "${CLI_DIR}/.gitignore"
test -f "${CLI_DIR}/src/app.aos"
test -f "${CLI_ARGS_DIR}/src/app.aos"
test -f "${MULTI_AGENT_DIR}/AGENTS.md"
test -f "${MULTI_AGENT_DIR}/CLAUDE.md"
test -f "${MULTI_AGENT_DIR}/GEMINI.md"
test -f "${MULTI_AGENT_DIR}/.cursor/rules/ailang.mdc"
test -f "${MULTI_AGENT_DIR}/.github/copilot-instructions.md"
test -f "${MULTI_AGENT_DIR}/.windsurfrules"
test -f "${CLAUDE_DIR}/AGENTS.md"
test -f "${CLAUDE_DIR}/CLAUDE.md"

CLI_OUT="$(./tools/airun run "${CLI_DIR}/" 2>&1)"
CLI_ARGS_OUT="$(./tools/airun run "${CLI_ARGS_DIR}/" -- hello 2>&1)"

printf '%s\n' "${CLI_OUT}" | rg -q 'Hello from cli'
printf '%s\n' "${CLI_ARGS_OUT}" | rg -q '^hello$'
rg -q 'AGENTS.md' "${MULTI_AGENT_DIR}/CLAUDE.md"
rg -q 'AGENTS.md' "${MULTI_AGENT_DIR}/GEMINI.md"
rg -q 'AGENTS.md' "${MULTI_AGENT_DIR}/.cursor/rules/ailang.mdc"
rg -q 'AGENTS.md' "${MULTI_AGENT_DIR}/.github/copilot-instructions.md"
rg -q 'AGENTS.md' "${MULTI_AGENT_DIR}/.windsurfrules"
