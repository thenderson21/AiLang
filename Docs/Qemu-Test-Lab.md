# QEMU Test Lab

This repo-local lab is for native Linux and Windows testing on the current host:

- host OS: macOS
- host arch: Apple Silicon (`arm64`)
- guest direction: Linux ARM64 first, Windows ARM64 second

The goal is to create a repeatable automation environment for:

- native command execution
- native build/test flows
- GUI launch and screenshot capture
- later input automation and artifact collection

## Scope of the first iteration

The first iteration in this repo provides:

- repo-local configuration template
- repo-local SSH key generation
- Linux cloud-init generation
- Linux and Windows ARM guest launch commands
- background guest start/stop/status helpers
- SSH wrappers for guest command execution
- Linux desktop bootstrap for GUI automation
- Linux GUI status checks
- host screenshot capture helper

Files:

- `scripts/qemu-lab.sh`
- `scripts/qemu-lab.env.example`

## Host prerequisites

Required on the macOS host:

- `qemu-system-aarch64`
- `qemu-img`
- `ssh`
- `ssh-keygen`
- `hdiutil`
- `screencapture`

Optional but useful:

- `virt-manager`
- `UTM` as a front-end while still standardizing on QEMU underneath

## Why ARM guests first

This host is Apple Silicon.

That means:

- Linux ARM64 guests are the cleanest starting point.
- Windows ARM64 guests are realistic.
- x86 guest emulation is possible but slower and is not the right first lab.

## Initial setup

1. Install QEMU on the host.
2. Copy:
   - `scripts/qemu-lab.env.example`
   - to `scripts/qemu-lab.env`
3. Fill in:
   - `AIVM_QEMU_EFI_CODE`
   - `AIVM_QEMU_EFI_VARS_TEMPLATE`
   - Linux image paths
   - Windows image paths
   - optional per-guest display overrides if one guest should be visible and the other headless
4. Initialize the lab:

```bash
./scripts/qemu-lab.sh init
```

5. Generate Linux cloud-init:

```bash
./scripts/qemu-lab.sh linux-cloud-init
```

6. Create guest disks:

```bash
./scripts/qemu-lab.sh linux-create-disk
./scripts/qemu-lab.sh windows-create-disk
```

## Linux guest bring-up

The current working path on Apple Silicon is:

```bash
./scripts/qemu-lab.sh init
./scripts/qemu-lab.sh linux-cloud-init
./scripts/qemu-lab.sh linux-create-disk
./scripts/qemu-lab.sh linux-start
./scripts/qemu-lab.sh linux-log-tail 40 serial
./scripts/qemu-lab.sh linux-screendump .tmp/qemu-lab/linux-screen.ppm
./scripts/qemu-lab.sh linux-screen-hash
./scripts/qemu-lab.sh linux-boot-probe
./scripts/qemu-lab.sh linux-sendkey ret
./scripts/qemu-lab.sh linux-wait-ssh 120
./scripts/qemu-lab.sh linux-ssh
```

Once SSH works, bootstrap the desktop session:

```bash
./scripts/qemu-lab.sh linux-gui-bootstrap
./scripts/qemu-lab.sh linux-gui-status
```

What this does:

- uses a NoCloud `cidata` seed ISO
- injects the repo-local SSH public key for user `ailang`
- installs `xfce4`, `lightdm`, `xdotool`, `scrot`, `xterm`
- sets the guest default target to `graphical.target`
- brings up a `DISPLAY=:0` session suitable for automation

Current verified Linux guest checks:

- SSH login works as `ailang`
- `DISPLAY=:0` is ready
- `xdotool` works
- `scrot` works

Current repo-level Linux GUI helper commands:

```bash
./scripts/qemu-lab.sh linux-gui-status
./scripts/qemu-lab.sh linux-gui-screenshot
./scripts/qemu-lab.sh linux-gui-windows
./scripts/qemu-lab.sh linux-gui-find "XTerm"
./scripts/qemu-lab.sh linux-gui-wait "CodexGuiSmoke" 10
./scripts/qemu-lab.sh linux-gui-launch xterm -T CodexGuiSmoke
./scripts/qemu-lab.sh linux-gui-focus <window-id>
./scripts/qemu-lab.sh linux-gui-key Ctrl+l
./scripts/qemu-lab.sh linux-gui-type "hello"
./scripts/qemu-lab.sh linux-gui-click 640 400
./scripts/qemu-lab.sh linux-gui-smoke
```

`linux-gui-smoke` is the first end-to-end guest GUI check. It launches a titled `xterm`,
waits for the window, focuses it, types text, presses `Return`, and copies a guest
screenshot back to the host under `.tmp/qemu-lab/` unless you pass an explicit output
path.

These are thin wrappers over guest-side `xdotool` and `scrot` running against `DISPLAY=:0`.

## Linux guest recommendation

Use Linux first.

Recommended guest traits:

- Ubuntu ARM64 desktop
- X11 session if possible
- `openssh-server`
- `git`
- `cmake`
- `clang` or `gcc`
- `xdotool`

Why X11 first:

- GUI automation is easier than Wayland for initial lab bring-up.

Once Linux guest SSH is working, these should become normal flows:

```bash
./scripts/qemu-lab.sh linux-ssh
./scripts/qemu-lab.sh linux-exec pwd
./scripts/qemu-lab.sh linux-exec ./scripts/test.sh
./scripts/qemu-lab.sh linux-gui-status
```

## Windows guest recommendation

Use Windows ARM64 second.

Recommended guest traits:

- Windows 11 ARM64
- OpenSSH Server installed and enabled
- PowerShell
- MSVC Build Tools
- CMake
- Git

Once guest SSH is working, these should become normal flows:

```bash
./scripts/qemu-lab.sh windows-wait-ssh 300
./scripts/qemu-lab.sh windows-log-tail 80 serial
./scripts/qemu-lab.sh windows-screendump .tmp/qemu-lab/windows-screen.ppm
./scripts/qemu-lab.sh windows-screen-hash
./scripts/qemu-lab.sh windows-boot-probe
./scripts/qemu-lab.sh windows-sendkey ret
./scripts/qemu-lab.sh windows-ssh
./scripts/qemu-lab.sh windows-exec pwd
```

Windows unattended seed support now exists in the repo:

```bash
./scripts/qemu-lab.sh windows-prepare
./scripts/qemu-lab.sh windows-unattend
```

What it generates:

- `Autounattend.xml`
- `windows-firstboot.ps1`
- `autounattend.iso`

Current intent of that seed:

- create the configured local Windows user
- auto-log in for first boot
- install and enable OpenSSH Server
- write the repo-local SSH public key into the configured user profile
- bootstrap with inbox-friendly devices first; virtio media is optional and disabled by default

Current default local credentials in the example config:

- user: `ailang`
- password: `AiLangQemu!23`

You can override both in `scripts/qemu-lab.env`.

For installer bring-up on macOS, use `windows-run` for the visible session. `windows-start` is intended for headless/background use only.

If you need to isolate media issues, you can temporarily disable the unattended seed attach:

```bash
AIVM_QEMU_WINDOWS_ATTACH_UNATTEND=0 ./scripts/qemu-lab.sh windows-start
```

Visible installer session:

```bash
./scripts/qemu-lab.sh windows-unattend
./scripts/qemu-lab.sh windows-run
```

Headless/background session after install:

```bash
AIVM_QEMU_WINDOWS_BG_DISPLAY=none ./scripts/qemu-lab.sh windows-start
```

## Current limitations

This first iteration does not yet provide:

- guest window enumeration
- deterministic host-side click/key injection into guest windows
- repo-level guest-side screenshot wrapper commands
- automatic guest provisioning for Windows
- post-install validation that OpenSSH came up exactly as seeded

Those are the next steps after QEMU is installed and the Windows guest image is available.

## Recommended next steps after QEMU install

1. Add guest-side Linux automation helpers:
   - screenshot
   - window list
   - focus
   - key/click injection
2. Bring up Windows ARM64 guest and verify SSH.
3. Add guest-side Windows automation helpers:
   - screenshot
   - window list
   - focus
   - key/click injection
4. Add repo-level smoke commands that run AiLang build/test flows inside guests.
