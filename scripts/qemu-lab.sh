#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG_FILE="${AIVM_QEMU_CONFIG:-${ROOT_DIR}/scripts/qemu-lab.env}"

load_config() {
  if [[ -f "${CONFIG_FILE}" ]]; then
    # shellcheck disable=SC1090
    source "${CONFIG_FILE}"
  fi

  : "${AIVM_QEMU_LAB_DIR:=.tmp/qemu-lab}"
  : "${AIVM_QEMU_MACHINE:=virt}"
  : "${AIVM_QEMU_ACCEL:=hvf}"
  : "${AIVM_QEMU_CPU:=host}"
  : "${AIVM_QEMU_LINUX_NAME:=linux-arm64}"
  : "${AIVM_QEMU_WINDOWS_NAME:=windows-arm64}"
  : "${AIVM_QEMU_LINUX_SSH_PORT:=2222}"
  : "${AIVM_QEMU_WINDOWS_SSH_PORT:=2223}"
  : "${AIVM_QEMU_LINUX_CPUS:=4}"
  : "${AIVM_QEMU_WINDOWS_CPUS:=6}"
  : "${AIVM_QEMU_LINUX_RAM_MB:=8192}"
  : "${AIVM_QEMU_WINDOWS_RAM_MB:=12288}"
  : "${AIVM_QEMU_LINUX_DISK_GB:=64}"
  : "${AIVM_QEMU_WINDOWS_DISK_GB:=96}"
  : "${AIVM_QEMU_LINUX_USER:=ailang}"
  : "${AIVM_QEMU_WINDOWS_USER:=ailang}"

  LAB_DIR="${ROOT_DIR}/${AIVM_QEMU_LAB_DIR}"
  KEYS_DIR="${LAB_DIR}/keys"
  SSH_KEY_PATH="${KEYS_DIR}/id_ed25519"
  SSH_PUB_KEY_PATH="${SSH_KEY_PATH}.pub"
}

require_cmd() {
  local cmd="$1"
  if ! command -v "${cmd}" >/dev/null 2>&1; then
    echo "missing required command: ${cmd}" >&2
    exit 1
  fi
}

require_file() {
  local path="$1"
  if [[ -z "${path}" || ! -f "${path}" ]]; then
    echo "missing required file: ${path}" >&2
    exit 1
  fi
}

print_cmd_path() {
  local name="$1"
  local path
  path="$(command -v "${name}" || true)"
  echo "${name}=${path}"
}

ensure_dirs() {
  mkdir -p "${LAB_DIR}" "${KEYS_DIR}" \
    "${LAB_DIR}/${AIVM_QEMU_LINUX_NAME}" \
    "${LAB_DIR}/${AIVM_QEMU_WINDOWS_NAME}"
}

ensure_ssh_key() {
  require_cmd ssh-keygen
  if [[ ! -f "${SSH_KEY_PATH}" ]]; then
    ssh-keygen -t ed25519 -N "" -f "${SSH_KEY_PATH}" >/dev/null
  fi
}

guest_dir() {
  local guest="$1"
  echo "${LAB_DIR}/${guest}"
}

guest_vars_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/efi-vars.fd"
}

ensure_guest_vars() {
  local guest="$1"
  local dst
  dst="$(guest_vars_path "${guest}")"
  require_file "${AIVM_QEMU_EFI_VARS_TEMPLATE:-}"
  if [[ ! -f "${dst}" ]]; then
    cp "${AIVM_QEMU_EFI_VARS_TEMPLATE}" "${dst}"
  fi
}

guest_ssh_common() {
  local port="$1"
  echo "-o" "StrictHostKeyChecking=no" \
       "-o" "UserKnownHostsFile=/dev/null" \
       "-o" "LogLevel=ERROR" \
       "-i" "${SSH_KEY_PATH}" \
       "-p" "${port}"
}

cmd_doctor() {
  load_config
  echo "host_os=$(uname -s)"
  echo "host_arch=$(uname -m)"
  echo "config_file=${CONFIG_FILE}"
  echo "lab_dir=${ROOT_DIR}/${AIVM_QEMU_LAB_DIR}"
  print_cmd_path qemu-system-aarch64
  print_cmd_path qemu-img
  print_cmd_path ssh
  print_cmd_path ssh-keygen
  print_cmd_path hdiutil
  print_cmd_path screencapture
  echo "efi_code=${AIVM_QEMU_EFI_CODE:-}"
  echo "efi_vars_template=${AIVM_QEMU_EFI_VARS_TEMPLATE:-}"
  echo "linux_image=${AIVM_QEMU_LINUX_IMAGE:-}"
  echo "windows_image=${AIVM_QEMU_WINDOWS_IMAGE:-}"
}

cmd_init() {
  load_config
  ensure_dirs
  ensure_ssh_key
  echo "lab initialized at ${LAB_DIR}"
  echo "ssh public key:"
  cat "${SSH_PUB_KEY_PATH}"
}

cmd_linux_cloud_init() {
  local guest_dir_path meta_data user_data iso_path pubkey
  load_config
  ensure_dirs
  ensure_ssh_key
  guest_dir_path="$(guest_dir "${AIVM_QEMU_LINUX_NAME}")"
  meta_data="${guest_dir_path}/meta-data"
  user_data="${guest_dir_path}/user-data"
  iso_path="${guest_dir_path}/cloud-init.iso"
  pubkey="$(cat "${SSH_PUB_KEY_PATH}")"

  cat >"${meta_data}" <<EOF
instance-id: ${AIVM_QEMU_LINUX_NAME}
local-hostname: ${AIVM_QEMU_LINUX_NAME}
EOF

  cat >"${user_data}" <<EOF
#cloud-config
users:
  - name: ${AIVM_QEMU_LINUX_USER}
    shell: /bin/bash
    sudo: ALL=(ALL) NOPASSWD:ALL
    ssh_authorized_keys:
      - ${pubkey}
package_update: true
packages:
  - openssh-server
  - git
  - cmake
  - clang
  - build-essential
  - xdotool
runcmd:
  - systemctl enable ssh
  - systemctl start ssh
EOF

  if command -v hdiutil >/dev/null 2>&1; then
    rm -f "${iso_path}"
    hdiutil makehybrid -quiet -o "${iso_path}" "${guest_dir_path}" -iso -joliet >/dev/null
    echo "wrote ${iso_path}"
  else
    echo "wrote ${meta_data}"
    echo "wrote ${user_data}"
    echo "hdiutil not found; cloud-init ISO not created"
  fi
}

cmd_linux_create_disk() {
  load_config
  ensure_dirs
  require_cmd qemu-img
  require_file "${AIVM_QEMU_LINUX_CLOUD_IMAGE:-}"
  if [[ -z "${AIVM_QEMU_LINUX_IMAGE:-}" ]]; then
    AIVM_QEMU_LINUX_IMAGE="$(guest_dir "${AIVM_QEMU_LINUX_NAME}")/disk.qcow2"
  fi
  if [[ -f "${AIVM_QEMU_LINUX_IMAGE}" ]]; then
    echo "linux disk already exists: ${AIVM_QEMU_LINUX_IMAGE}"
    return 0
  fi
  qemu-img create -f qcow2 -F qcow2 -b "${AIVM_QEMU_LINUX_CLOUD_IMAGE}" "${AIVM_QEMU_LINUX_IMAGE}" "${AIVM_QEMU_LINUX_DISK_GB}G" >/dev/null
  echo "created ${AIVM_QEMU_LINUX_IMAGE}"
}

cmd_linux_run() {
  local vars_path guest_dir_path cloud_init_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_LINUX_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_LINUX_NAME}"
  vars_path="$(guest_vars_path "${AIVM_QEMU_LINUX_NAME}")"
  guest_dir_path="$(guest_dir "${AIVM_QEMU_LINUX_NAME}")"
  cloud_init_path="${guest_dir_path}/cloud-init.iso"

  exec qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_LINUX_CPUS}" \
    -m "${AIVM_QEMU_LINUX_RAM_MB}" \
    -display cocoa \
    -device virtio-gpu-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_LINUX_IMAGE}" \
    ${AIVM_QEMU_LINUX_EXTRA_CDROM:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_LINUX_EXTRA_CDROM}"} \
    $( [[ -f "${cloud_init_path}" ]] && printf '%s ' -drive if=virtio,media=cdrom,file="${cloud_init_path}" ) \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_LINUX_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0
}

cmd_windows_create_disk() {
  load_config
  ensure_dirs
  require_cmd qemu-img
  if [[ -z "${AIVM_QEMU_WINDOWS_IMAGE:-}" ]]; then
    AIVM_QEMU_WINDOWS_IMAGE="$(guest_dir "${AIVM_QEMU_WINDOWS_NAME}")/disk.qcow2"
  fi
  if [[ -f "${AIVM_QEMU_WINDOWS_IMAGE}" ]]; then
    echo "windows disk already exists: ${AIVM_QEMU_WINDOWS_IMAGE}"
    return 0
  fi
  qemu-img create -f qcow2 "${AIVM_QEMU_WINDOWS_IMAGE}" "${AIVM_QEMU_WINDOWS_DISK_GB}G" >/dev/null
  echo "created ${AIVM_QEMU_WINDOWS_IMAGE}"
}

cmd_windows_run() {
  local vars_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_WINDOWS_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_WINDOWS_NAME}"
  vars_path="$(guest_vars_path "${AIVM_QEMU_WINDOWS_NAME}")"

  exec qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_WINDOWS_CPUS}" \
    -m "${AIVM_QEMU_WINDOWS_RAM_MB}" \
    -display cocoa \
    -device virtio-gpu-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_WINDOWS_IMAGE}" \
    ${AIVM_QEMU_WINDOWS_INSTALL_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_INSTALL_ISO}"} \
    ${AIVM_QEMU_WINDOWS_VIRTIO_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_VIRTIO_ISO}"} \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_WINDOWS_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0
}

cmd_linux_ssh() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  exec ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1"
}

cmd_linux_exec() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  exec ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" "$@"
}

cmd_windows_ssh() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  exec ssh $(guest_ssh_common "${AIVM_QEMU_WINDOWS_SSH_PORT}") "${AIVM_QEMU_WINDOWS_USER}@127.0.0.1"
}

cmd_windows_exec() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  exec ssh $(guest_ssh_common "${AIVM_QEMU_WINDOWS_SSH_PORT}") "${AIVM_QEMU_WINDOWS_USER}@127.0.0.1" "$@"
}

cmd_screenshot() {
  local out_path
  load_config
  require_cmd screencapture
  out_path="${1:-${LAB_DIR}/host-screenshot-$(date +%Y%m%d-%H%M%S).png}"
  mkdir -p "$(dirname "${out_path}")"
  screencapture -x "${out_path}"
  echo "wrote ${out_path}"
}

usage() {
  cat <<EOF
Usage: scripts/qemu-lab.sh <command> [args...]

Commands:
  doctor               Show host prerequisites and configured paths
  init                 Create repo-local lab directories and SSH key
  linux-cloud-init     Generate Linux cloud-init files and ISO
  linux-create-disk    Create Linux qcow2 disk from configured cloud image
  linux-run            Launch Linux ARM guest
  linux-ssh            SSH into Linux guest on forwarded port
  linux-exec <cmd...>  Run command inside Linux guest over SSH
  windows-create-disk  Create Windows qcow2 disk
  windows-run          Launch Windows ARM guest
  windows-ssh          SSH into Windows guest on forwarded port
  windows-exec <cmd...> Run command inside Windows guest over SSH
  screenshot [path]    Capture a host screenshot

Config:
  Copy scripts/qemu-lab.env.example to scripts/qemu-lab.env and fill in paths.
EOF
}

main() {
  local cmd="${1:-}"
  shift || true
  case "${cmd}" in
    doctor) cmd_doctor ;;
    init) cmd_init ;;
    linux-cloud-init) cmd_linux_cloud_init ;;
    linux-create-disk) cmd_linux_create_disk ;;
    linux-run) cmd_linux_run ;;
    linux-ssh) cmd_linux_ssh ;;
    linux-exec) cmd_linux_exec "$@" ;;
    windows-create-disk) cmd_windows_create_disk ;;
    windows-run) cmd_windows_run ;;
    windows-ssh) cmd_windows_ssh ;;
    windows-exec) cmd_windows_exec "$@" ;;
    screenshot) cmd_screenshot "$@" ;;
    ""|-h|--help|help) usage ;;
    *) echo "unknown command: ${cmd}" >&2; usage; exit 1 ;;
  esac
}

main "$@"
