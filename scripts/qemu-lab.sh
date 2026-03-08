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
  : "${AIVM_QEMU_DISPLAY:=cocoa}"
  : "${AIVM_QEMU_BG_DISPLAY:=none}"
  : "${AIVM_QEMU_LINUX_DISPLAY:=${AIVM_QEMU_DISPLAY}}"
  : "${AIVM_QEMU_LINUX_BG_DISPLAY:=${AIVM_QEMU_BG_DISPLAY}}"
  : "${AIVM_QEMU_WINDOWS_DISPLAY:=${AIVM_QEMU_DISPLAY}}"
  : "${AIVM_QEMU_WINDOWS_BG_DISPLAY:=${AIVM_QEMU_BG_DISPLAY}}"
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

  if [[ -z "${AIVM_QEMU_EFI_CODE:-}" && -f /opt/homebrew/share/qemu/edk2-aarch64-code.fd ]]; then
    AIVM_QEMU_EFI_CODE=/opt/homebrew/share/qemu/edk2-aarch64-code.fd
  fi
  if [[ -z "${AIVM_QEMU_EFI_VARS_TEMPLATE:-}" && -f /opt/homebrew/share/qemu/edk2-arm-vars.fd ]]; then
    AIVM_QEMU_EFI_VARS_TEMPLATE=/opt/homebrew/share/qemu/edk2-arm-vars.fd
  fi

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

guest_pid_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/vm.pid"
}

guest_log_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/vm.log"
}

guest_serial_log_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/serial.log"
}

guest_vars_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/efi-vars.fd"
}

guest_is_running() {
  local guest="$1"
  local pid_path pid
  pid_path="$(guest_pid_path "${guest}")"
  if [[ ! -f "${pid_path}" ]]; then
    return 1
  fi
  pid="$(cat "${pid_path}")"
  [[ -n "${pid}" ]] && kill -0 "${pid}" >/dev/null 2>&1
}

guest_stop() {
  local guest="$1"
  local pid_path pid
  pid_path="$(guest_pid_path "${guest}")"
  if ! guest_is_running "${guest}"; then
    rm -f "${pid_path}"
    echo "${guest}: not running"
    return 0
  fi
  pid="$(cat "${pid_path}")"
  kill "${pid}" >/dev/null 2>&1 || true
  sleep 1
  if kill -0 "${pid}" >/dev/null 2>&1; then
    kill -9 "${pid}" >/dev/null 2>&1 || true
  fi
  rm -f "${pid_path}"
  echo "${guest}: stopped"
}

guest_status() {
  local guest="$1"
  local pid_path log_path
  pid_path="$(guest_pid_path "${guest}")"
  log_path="$(guest_log_path "${guest}")"
  if guest_is_running "${guest}"; then
    echo "${guest}: running pid=$(cat "${pid_path}") log=${log_path}"
  else
    echo "${guest}: stopped log=${log_path}"
  fi
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
       "-o" "BatchMode=yes" \
       "-o" "ConnectTimeout=5" \
       "-o" "ConnectionAttempts=1" \
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
    hdiutil makehybrid -quiet -o "${iso_path}" "${guest_dir_path}" -iso -joliet -default-volume-name cidata >/dev/null
    echo "wrote ${iso_path}"
  elif command -v mkisofs >/dev/null 2>&1; then
    rm -f "${iso_path}"
    mkisofs -quiet -output "${iso_path}" -volid cidata -joliet -rock "${guest_dir_path}" >/dev/null 2>&1
    echo "wrote ${iso_path}"
  elif command -v genisoimage >/dev/null 2>&1; then
    rm -f "${iso_path}"
    genisoimage -quiet -output "${iso_path}" -volid cidata -joliet -rock "${guest_dir_path}" >/dev/null 2>&1
    echo "wrote ${iso_path}"
  else
    echo "wrote ${meta_data}"
    echo "wrote ${user_data}"
    echo "no ISO tool found; cloud-init ISO not created"
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
  local vars_path guest_dir_path cloud_init_path serial_log_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_LINUX_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_LINUX_NAME}"
  vars_path="$(guest_vars_path "${AIVM_QEMU_LINUX_NAME}")"
  guest_dir_path="$(guest_dir "${AIVM_QEMU_LINUX_NAME}")"
  cloud_init_path="${guest_dir_path}/cloud-init.iso"
  serial_log_path="$(guest_serial_log_path "${AIVM_QEMU_LINUX_NAME}")"

  exec qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_LINUX_CPUS}" \
    -m "${AIVM_QEMU_LINUX_RAM_MB}" \
    -display "${AIVM_QEMU_LINUX_DISPLAY}" \
    -device virtio-gpu-pci \
    -device virtio-rng-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -serial "file:${serial_log_path}" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_LINUX_IMAGE}" \
    ${AIVM_QEMU_LINUX_EXTRA_CDROM:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_LINUX_EXTRA_CDROM}"} \
    $( [[ -f "${cloud_init_path}" ]] && printf '%s ' -drive if=virtio,media=cdrom,file="${cloud_init_path}" ) \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_LINUX_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0
}

cmd_linux_start() {
  local vars_path guest_dir_path cloud_init_path pid_path log_path serial_log_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_LINUX_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_LINUX_NAME}"
  if guest_is_running "${AIVM_QEMU_LINUX_NAME}"; then
    guest_status "${AIVM_QEMU_LINUX_NAME}"
    return 0
  fi
  vars_path="$(guest_vars_path "${AIVM_QEMU_LINUX_NAME}")"
  guest_dir_path="$(guest_dir "${AIVM_QEMU_LINUX_NAME}")"
  cloud_init_path="${guest_dir_path}/cloud-init.iso"
  pid_path="$(guest_pid_path "${AIVM_QEMU_LINUX_NAME}")"
  log_path="$(guest_log_path "${AIVM_QEMU_LINUX_NAME}")"
  serial_log_path="$(guest_serial_log_path "${AIVM_QEMU_LINUX_NAME}")"
  : >"${serial_log_path}"

  qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_LINUX_CPUS}" \
    -m "${AIVM_QEMU_LINUX_RAM_MB}" \
    -daemonize \
    -pidfile "${pid_path}" \
    -display "${AIVM_QEMU_LINUX_BG_DISPLAY}" \
    -device virtio-gpu-pci \
    -device virtio-rng-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -serial "file:${serial_log_path}" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_LINUX_IMAGE}" \
    ${AIVM_QEMU_LINUX_EXTRA_CDROM:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_LINUX_EXTRA_CDROM}"} \
    $( [[ -f "${cloud_init_path}" ]] && printf '%s ' -drive if=virtio,media=cdrom,file="${cloud_init_path}" ) \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_LINUX_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0 \
    -D "${log_path}"
  guest_status "${AIVM_QEMU_LINUX_NAME}"
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
  local vars_path serial_log_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_WINDOWS_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_WINDOWS_NAME}"
  vars_path="$(guest_vars_path "${AIVM_QEMU_WINDOWS_NAME}")"
  serial_log_path="$(guest_serial_log_path "${AIVM_QEMU_WINDOWS_NAME}")"

  exec qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_WINDOWS_CPUS}" \
    -m "${AIVM_QEMU_WINDOWS_RAM_MB}" \
    -display "${AIVM_QEMU_WINDOWS_DISPLAY}" \
    -device virtio-gpu-pci \
    -device virtio-rng-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -serial "file:${serial_log_path}" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_WINDOWS_IMAGE}" \
    ${AIVM_QEMU_WINDOWS_INSTALL_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_INSTALL_ISO}"} \
    ${AIVM_QEMU_WINDOWS_VIRTIO_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_VIRTIO_ISO}"} \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_WINDOWS_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0
}

cmd_windows_start() {
  local vars_path pid_path log_path serial_log_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_WINDOWS_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_WINDOWS_NAME}"
  if guest_is_running "${AIVM_QEMU_WINDOWS_NAME}"; then
    guest_status "${AIVM_QEMU_WINDOWS_NAME}"
    return 0
  fi
  if [[ "${AIVM_QEMU_WINDOWS_BG_DISPLAY}" != "none" ]]; then
    echo "windows-start requires AIVM_QEMU_WINDOWS_BG_DISPLAY=none; use windows-run for visible installer sessions" >&2
    return 1
  fi
  vars_path="$(guest_vars_path "${AIVM_QEMU_WINDOWS_NAME}")"
  pid_path="$(guest_pid_path "${AIVM_QEMU_WINDOWS_NAME}")"
  log_path="$(guest_log_path "${AIVM_QEMU_WINDOWS_NAME}")"
  serial_log_path="$(guest_serial_log_path "${AIVM_QEMU_WINDOWS_NAME}")"
  : >"${serial_log_path}"

  qemu-system-aarch64 \
    -machine "${AIVM_QEMU_MACHINE},accel=${AIVM_QEMU_ACCEL}" \
    -cpu "${AIVM_QEMU_CPU}" \
    -smp "${AIVM_QEMU_WINDOWS_CPUS}" \
    -m "${AIVM_QEMU_WINDOWS_RAM_MB}" \
    -daemonize \
    -pidfile "${pid_path}" \
    -display "${AIVM_QEMU_WINDOWS_BG_DISPLAY}" \
    -device virtio-gpu-pci \
    -device virtio-rng-pci \
    -device qemu-xhci \
    -device usb-kbd \
    -device usb-tablet \
    -serial "file:${serial_log_path}" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_WINDOWS_IMAGE}" \
    ${AIVM_QEMU_WINDOWS_INSTALL_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_INSTALL_ISO}"} \
    ${AIVM_QEMU_WINDOWS_VIRTIO_ISO:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_WINDOWS_VIRTIO_ISO}"} \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_WINDOWS_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0 \
    -D "${log_path}"
  guest_status "${AIVM_QEMU_WINDOWS_NAME}"
}

cmd_linux_stop() {
  load_config
  guest_stop "${AIVM_QEMU_LINUX_NAME}"
}

cmd_windows_stop() {
  load_config
  guest_stop "${AIVM_QEMU_WINDOWS_NAME}"
}

cmd_status() {
  load_config
  guest_status "${AIVM_QEMU_LINUX_NAME}"
  guest_status "${AIVM_QEMU_WINDOWS_NAME}"
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

cmd_linux_gui_bootstrap() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" '
    set -euo pipefail
    sudo cloud-init status --wait >/dev/null 2>&1 || true
    sudo apt-get update
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y \
      xfce4 lightdm xdotool scrot x11-apps xterm
    sudo install -d -m 0755 /etc/lightdm/lightdm.conf.d
    cat <<EOF | sudo tee /etc/lightdm/lightdm.conf.d/50-ailang-autologin.conf >/dev/null
[Seat:*]
autologin-user='"${AIVM_QEMU_LINUX_USER}"'
autologin-user-timeout=0
user-session=xfce
greeter-hide-users=false
EOF
    sudo systemctl enable lightdm
    sudo systemctl set-default graphical.target
    sudo systemctl restart lightdm || sudo systemctl start lightdm
  '
}

cmd_linux_gui_status() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  exec ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" '
    set -euo pipefail
    printf "lightdm_enabled=%s\n" "$(systemctl is-enabled lightdm 2>/dev/null || echo no)"
    printf "lightdm_active=%s\n" "$(systemctl is-active lightdm 2>/dev/null || echo no)"
    printf "default_target=%s\n" "$(systemctl get-default 2>/dev/null || echo unknown)"
    loginctl list-sessions --no-legend || true
    if command -v xdpyinfo >/dev/null 2>&1; then
      DISPLAY=:0 xdpyinfo >/dev/null 2>&1 && echo "display_0=ready" || echo "display_0=not-ready"
    fi
  '
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
  linux-start          Launch Linux ARM guest in background
  linux-run            Launch Linux ARM guest
  linux-stop           Stop Linux ARM guest
  linux-ssh            SSH into Linux guest on forwarded port
  linux-exec <cmd...>  Run command inside Linux guest over SSH
  linux-gui-bootstrap  Install and enable Linux desktop + GUI automation tools
  linux-gui-status     Show Linux guest desktop/session status
  windows-create-disk  Create Windows qcow2 disk
  windows-start        Launch Windows ARM guest in background
  windows-run          Launch Windows ARM guest
  windows-stop         Stop Windows ARM guest
  windows-ssh          SSH into Windows guest on forwarded port
  windows-exec <cmd...> Run command inside Windows guest over SSH
  status               Show Linux/Windows guest status
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
    linux-start) cmd_linux_start ;;
    linux-run) cmd_linux_run ;;
    linux-stop) cmd_linux_stop ;;
    linux-ssh) cmd_linux_ssh ;;
    linux-exec) cmd_linux_exec "$@" ;;
    linux-gui-bootstrap) cmd_linux_gui_bootstrap ;;
    linux-gui-status) cmd_linux_gui_status ;;
    windows-create-disk) cmd_windows_create_disk ;;
    windows-start) cmd_windows_start ;;
    windows-run) cmd_windows_run ;;
    windows-stop) cmd_windows_stop ;;
    windows-ssh) cmd_windows_ssh ;;
    windows-exec) cmd_windows_exec "$@" ;;
    status) cmd_status ;;
    screenshot) cmd_screenshot "$@" ;;
    ""|-h|--help|help) usage ;;
    *) echo "unknown command: ${cmd}" >&2; usage; exit 1 ;;
  esac
}

main "$@"
