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
- SSH wrappers for guest command execution
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
./scripts/qemu-lab.sh windows-ssh
./scripts/qemu-lab.sh windows-exec pwd
```

## Current limitations

This first iteration does not yet provide:

- guest window enumeration
- deterministic host-side click/key injection into guest windows
- guest-side screenshot capture helpers
- VM lifecycle wrappers beyond direct `qemu-system-aarch64` launch
- automatic guest provisioning for Windows

Those should be the next steps after QEMU is installed and both guests boot.

## Recommended next steps after QEMU install

1. Bring up Linux ARM64 guest and verify SSH.
2. Add guest-side Linux automation helpers:
   - screenshot
   - window list
   - focus
   - key/click injection
3. Bring up Windows ARM64 guest and verify SSH.
4. Add guest-side Windows automation helpers:
   - screenshot
   - window list
   - focus
   - key/click injection
5. Add repo-level smoke commands that run AiLang build/test flows inside guests.
