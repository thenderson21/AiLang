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
  : "${AIVM_QEMU_WINDOWS_PASSWORD:=AiLangQemu!23}"
  : "${AIVM_QEMU_WINDOWS_ATTACH_UNATTEND:=1}"
  : "${AIVM_QEMU_WINDOWS_ATTACH_VIRTIO:=0}"

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

timestamp_utc() {
  date -u +"%Y%m%d-%H%M%S"
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

guest_monitor_path() {
  local guest="$1"
  echo "$(guest_dir "${guest}")/monitor.sock"
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
  local pid_path log_path monitor_path
  pid_path="$(guest_pid_path "${guest}")"
  log_path="$(guest_log_path "${guest}")"
  monitor_path="$(guest_monitor_path "${guest}")"
  if guest_is_running "${guest}"; then
    echo "${guest}: running pid=$(cat "${pid_path}") log=${log_path} monitor=${monitor_path}"
  else
    echo "${guest}: stopped log=${log_path} monitor=${monitor_path}"
  fi
}

guest_monitor_send() {
  local guest="$1" command="$2"
  local monitor_path
  monitor_path="$(guest_monitor_path "${guest}")"
  require_cmd nc
  if [[ ! -S "${monitor_path}" ]]; then
    echo "missing monitor socket: ${monitor_path}" >&2
    return 1
  fi
  printf '%s\n' "${command}" | nc -U "${monitor_path}" >/dev/null
}

cmd_linux_monitor() {
  load_config
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh linux-monitor <qemu-monitor-command>" >&2
    return 1
  fi
  guest_monitor_send "${AIVM_QEMU_LINUX_NAME}" "$*"
}

cmd_windows_monitor() {
  load_config
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh windows-monitor <qemu-monitor-command>" >&2
    return 1
  fi
  guest_monitor_send "${AIVM_QEMU_WINDOWS_NAME}" "$*"
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

guest_scp_common() {
  local port="$1"
  echo "-o" "StrictHostKeyChecking=no" \
       "-o" "UserKnownHostsFile=/dev/null" \
       "-o" "LogLevel=ERROR" \
       "-o" "BatchMode=yes" \
       "-i" "${SSH_KEY_PATH}" \
       "-P" "${port}"
}

WINDOWS_STORAGE_ARGS=()
WINDOWS_CONTROLLER_ARGS=()

build_windows_storage_args() {
  local guest_dir_path
  guest_dir_path="$(guest_dir "${AIVM_QEMU_WINDOWS_NAME}")"
  WINDOWS_CONTROLLER_ARGS=()
  WINDOWS_STORAGE_ARGS=(
    -drive "if=none,id=win_disk,format=qcow2,file=${AIVM_QEMU_WINDOWS_IMAGE}"
    -device nvme,serial=ailang-win,drive=win_disk,bootindex=1
  )

  if [[ -n "${AIVM_QEMU_WINDOWS_INSTALL_ISO:-}" ]]; then
    WINDOWS_STORAGE_ARGS+=(
      -drive "if=none,id=win_install,media=cdrom,file=${AIVM_QEMU_WINDOWS_INSTALL_ISO}"
      -device usb-storage,drive=win_install,bootindex=0
    )
  fi

  if [[ "${AIVM_QEMU_WINDOWS_ATTACH_UNATTEND}" == "1" && -f "${guest_dir_path}/autounattend.iso" ]]; then
    WINDOWS_STORAGE_ARGS+=(
      -drive "if=none,id=win_autounattend,media=cdrom,file=${guest_dir_path}/autounattend.iso"
      -device usb-storage,drive=win_autounattend
    )
  fi

  if [[ "${AIVM_QEMU_WINDOWS_ATTACH_VIRTIO}" == "1" && -n "${AIVM_QEMU_WINDOWS_VIRTIO_ISO:-}" ]]; then
    WINDOWS_STORAGE_ARGS+=(
      -drive "if=none,id=win_virtio,media=cdrom,file=${AIVM_QEMU_WINDOWS_VIRTIO_ISO}"
      -device usb-storage,drive=win_virtio
    )
  fi
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
  local vars_path guest_dir_path cloud_init_path serial_log_path monitor_path
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
  monitor_path="$(guest_monitor_path "${AIVM_QEMU_LINUX_NAME}")"
  rm -f "${monitor_path}"

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
    "${WINDOWS_CONTROLLER_ARGS[@]}" \
    -serial "file:${serial_log_path}" \
    -monitor "unix:${monitor_path},server,nowait" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    -drive if=virtio,format=qcow2,file="${AIVM_QEMU_LINUX_IMAGE}" \
    ${AIVM_QEMU_LINUX_EXTRA_CDROM:+-drive if=virtio,media=cdrom,file="${AIVM_QEMU_LINUX_EXTRA_CDROM}"} \
    $( [[ -f "${cloud_init_path}" ]] && printf '%s ' -drive if=virtio,media=cdrom,file="${cloud_init_path}" ) \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_LINUX_SSH_PORT}-:22" \
    -device virtio-net-pci,netdev=net0
}

cmd_linux_start() {
  local vars_path guest_dir_path cloud_init_path pid_path log_path serial_log_path monitor_path
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
  monitor_path="$(guest_monitor_path "${AIVM_QEMU_LINUX_NAME}")"
  : >"${serial_log_path}"
  rm -f "${monitor_path}"

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
    "${WINDOWS_CONTROLLER_ARGS[@]}" \
    -serial "file:${serial_log_path}" \
    -monitor "unix:${monitor_path},server,nowait" \
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

cmd_windows_prepare() {
  cmd_windows_create_disk
  cmd_windows_unattend
}

cmd_windows_unattend() {
  local guest_dir_path unattend_path setup_path key_path iso_path user password
  load_config
  ensure_dirs
  ensure_ssh_key
  guest_dir_path="$(guest_dir "${AIVM_QEMU_WINDOWS_NAME}")"
  unattend_path="${guest_dir_path}/Autounattend.xml"
  setup_path="${guest_dir_path}/windows-firstboot.ps1"
  key_path="${guest_dir_path}/authorized_keys"
  iso_path="${guest_dir_path}/autounattend.iso"
  user="${AIVM_QEMU_WINDOWS_USER}"
  password="${AIVM_QEMU_WINDOWS_PASSWORD}"

  cat >"${setup_path}" <<EOF
\$ErrorActionPreference = 'Stop'
\$user = '${user}'
\$password = '${password}'
\$sshKey = Get-Content 'A:\\authorized_keys' -Raw

if (-not (Get-LocalUser -Name \$user -ErrorAction SilentlyContinue)) {
  \$securePassword = ConvertTo-SecureString \$password -AsPlainText -Force
  New-LocalUser -Name \$user -Password \$securePassword -AccountNeverExpires -PasswordNeverExpires
}

Add-LocalGroupMember -Group 'Administrators' -Member \$user -ErrorAction SilentlyContinue
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Start-Service sshd
Set-Service -Name sshd -StartupType Automatic

New-Item -ItemType Directory -Force -Path "C:\\Users\\\$user\\.ssh" | Out-Null
Set-Content -Path "C:\\Users\\\$user\\.ssh\\authorized_keys" -Value \$sshKey -NoNewline

icacls "C:\\Users\\\$user\\.ssh" /inheritance:r | Out-Null
icacls "C:\\Users\\\$user\\.ssh" /grant "\${user}:(OI)(CI)F" | Out-Null
icacls "C:\\Users\\\$user\\.ssh\\authorized_keys" /inheritance:r | Out-Null
icacls "C:\\Users\\\$user\\.ssh\\authorized_keys" /grant "\${user}:F" | Out-Null
netsh advfirewall firewall add rule name='OpenSSH Server (sshd)' dir=in action=allow protocol=TCP localport=22 | Out-Null
EOF

  cat "${SSH_PUB_KEY_PATH}" >"${key_path}"

  cat >"${unattend_path}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="arm64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <SetupUILanguage>
        <UILanguage>en-US</UILanguage>
      </SetupUILanguage>
      <InputLocale>en-US</InputLocale>
      <SystemLocale>en-US</SystemLocale>
      <UILanguage>en-US</UILanguage>
      <UserLocale>en-US</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" processorArchitecture="arm64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <ImageInstall>
        <OSImage>
          <InstallToAvailablePartition>true</InstallToAvailablePartition>
        </OSImage>
      </ImageInstall>
      <UserData>
        <AcceptEula>true</AcceptEula>
      </UserData>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" processorArchitecture="arm64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <InputLocale>en-US</InputLocale>
      <SystemLocale>en-US</SystemLocale>
      <UILanguage>en-US</UILanguage>
      <UserLocale>en-US</UserLocale>
    </component>
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="arm64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
      <AutoLogon>
        <Enabled>true</Enabled>
        <Username>${user}</Username>
        <Password>
          <Value>${password}</Value>
          <PlainText>true</PlainText>
        </Password>
        <LogonCount>2</LogonCount>
      </AutoLogon>
      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideLocalAccountScreen>true</HideLocalAccountScreen>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <NetworkLocation>Work</NetworkLocation>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <TimeZone>UTC</TimeZone>
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount wcm:action="add" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
            <Name>${user}</Name>
            <Group>Administrators</Group>
            <DisplayName>${user}</DisplayName>
            <Password>
              <Value>${password}</Value>
              <PlainText>true</PlainText>
            </Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
          <Order>1</Order>
          <Description>Run AiLang guest bootstrap</Description>
          <CommandLine>powershell -ExecutionPolicy Bypass -File A:\windows-firstboot.ps1</CommandLine>
        </SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>
EOF

  rm -f "${iso_path}"
  if command -v hdiutil >/dev/null 2>&1; then
    hdiutil makehybrid -quiet -o "${iso_path}" "${guest_dir_path}" -iso -joliet -default-volume-name AUTOUNATTEND >/dev/null
  elif command -v mkisofs >/dev/null 2>&1; then
    mkisofs -quiet -output "${iso_path}" -volid AUTOUNATTEND -joliet -rock "${guest_dir_path}" >/dev/null 2>&1
  elif command -v genisoimage >/dev/null 2>&1; then
    genisoimage -quiet -output "${iso_path}" -volid AUTOUNATTEND -joliet -rock "${guest_dir_path}" >/dev/null 2>&1
  else
    echo "no ISO tool found; Windows autounattend ISO not created" >&2
    return 1
  fi

  echo "wrote ${unattend_path}"
  echo "wrote ${setup_path}"
  echo "wrote ${iso_path}"
}

cmd_windows_run() {
  local vars_path serial_log_path monitor_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_WINDOWS_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_WINDOWS_NAME}"
  build_windows_storage_args
  vars_path="$(guest_vars_path "${AIVM_QEMU_WINDOWS_NAME}")"
  serial_log_path="$(guest_serial_log_path "${AIVM_QEMU_WINDOWS_NAME}")"
  monitor_path="$(guest_monitor_path "${AIVM_QEMU_WINDOWS_NAME}")"
  rm -f "${monitor_path}"

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
    -monitor "unix:${monitor_path},server,nowait" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    "${WINDOWS_STORAGE_ARGS[@]}" \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_WINDOWS_SSH_PORT}-:22" \
    -device e1000,netdev=net0
}

cmd_windows_start() {
  local vars_path pid_path log_path serial_log_path monitor_path
  load_config
  ensure_dirs
  require_cmd qemu-system-aarch64
  require_file "${AIVM_QEMU_EFI_CODE:-}"
  require_file "${AIVM_QEMU_WINDOWS_IMAGE:-}"
  ensure_guest_vars "${AIVM_QEMU_WINDOWS_NAME}"
  build_windows_storage_args
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
  monitor_path="$(guest_monitor_path "${AIVM_QEMU_WINDOWS_NAME}")"
  : >"${serial_log_path}"
  rm -f "${monitor_path}"

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
    -monitor "unix:${monitor_path},server,nowait" \
    -drive if=pflash,format=raw,readonly=on,file="${AIVM_QEMU_EFI_CODE}" \
    -drive if=pflash,format=raw,file="${vars_path}" \
    "${WINDOWS_STORAGE_ARGS[@]}" \
    -netdev "user,id=net0,hostfwd=tcp::${AIVM_QEMU_WINDOWS_SSH_PORT}-:22" \
    -device e1000,netdev=net0 \
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

cmd_guest_log_tail() {
  local guest="$1" lines="${2:-40}" kind="${3:-qemu}" path
  load_config
  case "${kind}" in
    qemu) path="$(guest_log_path "${guest}")" ;;
    serial) path="$(guest_serial_log_path "${guest}")" ;;
    *) echo "unknown log kind: ${kind}" >&2; return 1 ;;
  esac
  if [[ ! -f "${path}" ]]; then
    echo "missing ${kind} log: ${path}" >&2
    return 1
  fi
  tail -n "${lines}" "${path}"
}

cmd_linux_log_tail() {
  local lines="${1:-40}" kind="${2:-qemu}"
  load_config
  cmd_guest_log_tail "${AIVM_QEMU_LINUX_NAME}" "${lines}" "${kind}"
}

cmd_windows_log_tail() {
  local lines="${1:-40}" kind="${2:-qemu}"
  load_config
  cmd_guest_log_tail "${AIVM_QEMU_WINDOWS_NAME}" "${lines}" "${kind}"
}

cmd_guest_screendump() {
  local guest="$1" out_path="${2:-}" dump_path
  load_config
  if [[ -z "${out_path}" ]]; then
    out_path="${LAB_DIR}/${guest}-screen-$(timestamp_utc).ppm"
  fi
  mkdir -p "$(dirname "${out_path}")"
  if [[ "${out_path}" = /* ]]; then
    dump_path="${out_path}"
  else
    dump_path="${ROOT_DIR}/${out_path}"
  fi
  guest_monitor_send "${guest}" "screendump ${dump_path}"
  if [[ ! -f "${out_path}" ]]; then
    echo "missing screendump output: ${out_path}" >&2
    return 1
  fi
  echo "wrote ${out_path}"
}

cmd_guest_screen_hash() {
  local guest="$1"
  local tmp_path
  load_config
  require_cmd shasum
  tmp_path="${LAB_DIR}/${guest}-screen-hash-$$.ppm"
  cmd_guest_screendump "${guest}" "${tmp_path}" >/dev/null
  shasum "${tmp_path}" | awk '{print $1}'
  rm -f "${tmp_path}"
}

cmd_linux_screendump() {
  load_config
  cmd_guest_screendump "${AIVM_QEMU_LINUX_NAME}" "${1:-}"
}

cmd_windows_screendump() {
  load_config
  cmd_guest_screendump "${AIVM_QEMU_WINDOWS_NAME}" "${1:-}"
}

cmd_linux_screen_hash() {
  load_config
  cmd_guest_screen_hash "${AIVM_QEMU_LINUX_NAME}"
}

cmd_windows_screen_hash() {
  load_config
  cmd_guest_screen_hash "${AIVM_QEMU_WINDOWS_NAME}"
}

cmd_linux_sendkey() {
  load_config
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh linux-sendkey <qemu-key-name>" >&2
    return 1
  fi
  cmd_linux_monitor "sendkey $*"
}

cmd_windows_sendkey() {
  load_config
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh windows-sendkey <qemu-key-name>" >&2
    return 1
  fi
  cmd_windows_monitor "sendkey $*"
}

cmd_guest_wait_ssh() {
  local user="$1" port="$2" timeout="${3:-120}" name="$4"
  local end
  load_config
  ensure_ssh_key
  require_cmd ssh
  end=$((SECONDS + timeout))
  while (( SECONDS < end )); do
    # shellcheck disable=SC2048,SC2086
    if ssh $(guest_ssh_common "${port}") "${user}@127.0.0.1" "printf ready" >/dev/null 2>&1; then
      printf "%s ssh=ready port=%s\n" "${name}" "${port}"
      return 0
    fi
    sleep 2
  done
  printf "%s ssh=timeout port=%s timeout=%s\n" "${name}" "${port}" "${timeout}" >&2
  return 1
}

cmd_linux_wait_ssh() {
  local timeout="${1:-120}"
  load_config
  cmd_guest_wait_ssh "${AIVM_QEMU_LINUX_USER}" "${AIVM_QEMU_LINUX_SSH_PORT}" "${timeout}" "${AIVM_QEMU_LINUX_NAME}"
}

cmd_windows_wait_ssh() {
  local timeout="${1:-300}"
  load_config
  cmd_guest_wait_ssh "${AIVM_QEMU_WINDOWS_USER}" "${AIVM_QEMU_WINDOWS_SSH_PORT}" "${timeout}" "${AIVM_QEMU_WINDOWS_NAME}"
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
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" '
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

cmd_linux_gui_screenshot() {
  local out_path remote_path
  load_config
  ensure_ssh_key
  require_cmd ssh
  require_cmd scp
  out_path="${1:-${LAB_DIR}/${AIVM_QEMU_LINUX_NAME}-screenshot-$(timestamp_utc).png}"
  remote_path="/tmp/ailang-qemu-shot-$$.png"
  mkdir -p "$(dirname "${out_path}")"
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "DISPLAY=:0 scrot '${remote_path}'"
  # shellcheck disable=SC2048,SC2086
  scp $(guest_scp_common "${AIVM_QEMU_LINUX_SSH_PORT}") \
    "${AIVM_QEMU_LINUX_USER}@127.0.0.1:${remote_path}" "${out_path}" >/dev/null
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "rm -f '${remote_path}'" >/dev/null
  echo "wrote ${out_path}"
}

cmd_linux_gui_windows() {
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" '
    set -euo pipefail
    DISPLAY=:0 bash -lc '"'"'
      ids=$(xdotool search --onlyvisible --name ".*" 2>/dev/null || true)
      for id in $ids; do
        name=$(xdotool getwindowname "$id" 2>/dev/null || true)
        cls=$(xprop -id "$id" WM_CLASS 2>/dev/null | awk -F" = " "/WM_CLASS/ {print \$2}" || true)
        if [[ -z "$name" && -z "$cls" ]]; then
          continue
        fi
        printf "%s\t%s\t%s\n" "$id" "$name" "$cls"
      done
    '"'"'
  '
}

cmd_linux_gui_find() {
  local pattern="${1:-}"
  if [[ -z "${pattern}" ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-find <pattern>" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" "
    DISPLAY=:0 bash -lc '
      xdotool search --onlyvisible --name \"${pattern}\" 2>/dev/null || true
    '
  "
}

cmd_linux_gui_wait() {
  local pattern="${1:-}" timeout="${2:-15}"
  if [[ -z "${pattern}" ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-wait <pattern> [timeout-seconds]" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" "
    DISPLAY=:0 bash -lc '
      end=\$((SECONDS + ${timeout}))
      while (( SECONDS < end )); do
        id=\$(xdotool search --onlyvisible --name \"${pattern}\" 2>/dev/null | head -n1 || true)
        if [[ -n \"\$id\" ]]; then
          echo \"\$id\"
          exit 0
        fi
        sleep 1
      done
      exit 1
    '
  "
}

cmd_linux_gui_launch() {
  local guest_cmd remote_cmd
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-launch <command...>" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  printf -v guest_cmd '%q ' "$@"
  guest_cmd="${guest_cmd% }"
  remote_cmd="DISPLAY=:0 nohup ${guest_cmd} >/tmp/ailang-qemu-gui-launch.log 2>&1 </dev/null &"
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "bash -lc $(printf '%q' "$remote_cmd")"
}

cmd_linux_gui_focus() {
  local target="${1:-}"
  if [[ -z "${target}" ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-focus <window-id>" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "DISPLAY=:0 xdotool windowactivate --sync '${target}'"
}

cmd_linux_gui_key() {
  if [[ $# -lt 1 ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-key <key-sequence>" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "DISPLAY=:0 xdotool key --delay 50 $*"
}

cmd_linux_gui_type() {
  local text="${1:-}"
  if [[ -z "${text}" ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-type <text>" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "DISPLAY=:0 xdotool type --delay 30 -- '$text'"
}

cmd_linux_gui_click() {
  local x="${1:-}" y="${2:-}" button="${3:-1}"
  if [[ -z "${x}" || -z "${y}" ]]; then
    echo "usage: scripts/qemu-lab.sh linux-gui-click <x> <y> [button]" >&2
    return 1
  fi
  load_config
  ensure_ssh_key
  require_cmd ssh
  # shellcheck disable=SC2048,SC2086
  ssh $(guest_ssh_common "${AIVM_QEMU_LINUX_SSH_PORT}") "${AIVM_QEMU_LINUX_USER}@127.0.0.1" \
    "DISPLAY=:0 xdotool mousemove --sync '${x}' '${y}' click '${button}'"
}

cmd_linux_gui_smoke() {
  local title="AiLangQemuGuiSmoke"
  local text="AiLang QEMU GUI smoke"
  local out_path id

  load_config
  ensure_ssh_key
  require_cmd ssh
  require_cmd scp
  out_path="${1:-${LAB_DIR}/linux-gui-smoke-$(date +%Y%m%d-%H%M%S).png}"

  cmd_linux_gui_launch xterm -title "${title}" -hold -e sh -lc "printf '%s\n' '${text}'; sleep 10" >/dev/null
  id="$(cmd_linux_gui_wait "${title}" 15 | tr -d '\r')"
  if [[ -z "${id}" ]]; then
    echo "failed to detect smoke window" >&2
    return 1
  fi

  cmd_linux_gui_focus "${id}" >/dev/null
  cmd_linux_gui_type "${text}" >/dev/null
  cmd_linux_gui_key Return >/dev/null
  cmd_linux_gui_screenshot "${out_path}" >/dev/null

  printf "window_id=%s\n" "${id}"
  printf "screenshot=%s\n" "${out_path}"
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
  linux-log-tail [lines] [qemu|serial] Tail Linux guest log
  linux-screendump [path] Capture host-side QEMU framebuffer dump for Linux guest
  linux-screen-hash    Capture Linux guest framebuffer and print content hash
  linux-monitor <cmd...> Send raw QEMU monitor command to Linux guest
  linux-sendkey <key>    Send QEMU key to Linux guest
  linux-wait-ssh [timeout] Wait for Linux guest SSH readiness
  linux-ssh            SSH into Linux guest on forwarded port
  linux-exec <cmd...>  Run command inside Linux guest over SSH
  linux-gui-bootstrap  Install and enable Linux desktop + GUI automation tools
  linux-gui-status     Show Linux guest desktop/session status
  linux-gui-screenshot [path]  Capture guest screenshot and copy it to host
  linux-gui-windows    List visible guest X11 windows
  linux-gui-find <pattern> Find visible guest windows by title regex
  linux-gui-wait <pattern> [timeout] Wait for a visible guest window to appear
  linux-gui-launch <cmd...> Launch a GUI app inside the guest
  linux-gui-focus <id> Focus guest window by X11 window id
  linux-gui-key <keys> Send xdotool key sequence to the guest
  linux-gui-type <text> Type text into the focused guest window
  linux-gui-click <x> <y> [button] Click inside the guest display
  linux-gui-smoke [path] Run guest GUI smoke flow and capture screenshot
  windows-create-disk  Create Windows qcow2 disk
  windows-prepare      Create Windows disk and unattended seed ISO
  windows-unattend     Generate Windows unattended install seed ISO
  windows-start        Launch Windows ARM guest in background
  windows-run          Launch Windows ARM guest
  windows-stop         Stop Windows ARM guest
  windows-log-tail [lines] [qemu|serial] Tail Windows guest log
  windows-screendump [path] Capture host-side QEMU framebuffer dump for Windows guest
  windows-screen-hash  Capture Windows guest framebuffer and print content hash
  windows-monitor <cmd...> Send raw QEMU monitor command to Windows guest
  windows-sendkey <key>  Send QEMU key to Windows guest
  windows-wait-ssh [timeout] Wait for Windows guest SSH readiness
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
    linux-log-tail) cmd_linux_log_tail "$@" ;;
    linux-screendump) cmd_linux_screendump "$@" ;;
    linux-screen-hash) cmd_linux_screen_hash ;;
    linux-monitor) cmd_linux_monitor "$@" ;;
    linux-sendkey) cmd_linux_sendkey "$@" ;;
    linux-wait-ssh) cmd_linux_wait_ssh "$@" ;;
    linux-ssh) cmd_linux_ssh ;;
    linux-exec) cmd_linux_exec "$@" ;;
    linux-gui-bootstrap) cmd_linux_gui_bootstrap ;;
    linux-gui-status) cmd_linux_gui_status ;;
    linux-gui-screenshot) cmd_linux_gui_screenshot "$@" ;;
    linux-gui-windows) cmd_linux_gui_windows ;;
    linux-gui-find) cmd_linux_gui_find "$@" ;;
    linux-gui-wait) cmd_linux_gui_wait "$@" ;;
    linux-gui-launch) cmd_linux_gui_launch "$@" ;;
    linux-gui-focus) cmd_linux_gui_focus "$@" ;;
    linux-gui-key) cmd_linux_gui_key "$@" ;;
    linux-gui-type) cmd_linux_gui_type "$@" ;;
    linux-gui-click) cmd_linux_gui_click "$@" ;;
    linux-gui-smoke) cmd_linux_gui_smoke "$@" ;;
    windows-create-disk) cmd_windows_create_disk ;;
    windows-prepare) cmd_windows_prepare ;;
    windows-unattend) cmd_windows_unattend ;;
    windows-start) cmd_windows_start ;;
    windows-run) cmd_windows_run ;;
    windows-stop) cmd_windows_stop ;;
    windows-log-tail) cmd_windows_log_tail "$@" ;;
    windows-screendump) cmd_windows_screendump "$@" ;;
    windows-screen-hash) cmd_windows_screen_hash ;;
    windows-monitor) cmd_windows_monitor "$@" ;;
    windows-sendkey) cmd_windows_sendkey "$@" ;;
    windows-wait-ssh) cmd_windows_wait_ssh "$@" ;;
    windows-ssh) cmd_windows_ssh ;;
    windows-exec) cmd_windows_exec "$@" ;;
    status) cmd_status ;;
    screenshot) cmd_screenshot "$@" ;;
    ""|-h|--help|help) usage ;;
    *) echo "unknown command: ${cmd}" >&2; usage; exit 1 ;;
  esac
}

main "$@"
