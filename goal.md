<# Goal: Production Release Gate

> This file is the **single source of truth** for shipping. Every release build
> must pass every **[BLOCKER]** item before it is tagged, signed, and published
> to GitHub. Run `/goal` (or the release pipeline) against this file; the agent
> must verify each item with evidence (command output, log, or hash) and not
> just tick the box.

**Product:** JamesOptimizer
**Publisher name (everywhere):** `James God`
**Distribution:** Open source, GitHub Releases
**Release rule:** Any failed [BLOCKER] = release is rejected. No exceptions.

---

## 0. How to run this gate

1. Build from a clean checkout (`git status` must be clean, on a tagged commit).
2. Run the verification script (`Installer\Verify-Release.ps1`) which covers
   sections 1 to 6 below and writes `release-report.md`.
3. The agent reviews `release-report.md`, fixes failures, and re-runs until all
   [BLOCKER] items pass.
4. Only then: publish the GitHub Release with the hashes from Section 6.

---

## 1. App <-> Service pipeline checks [BLOCKER]

- [ ] Service installs, starts, stops, restarts, and uninstalls cleanly (no leftover service entry).
- [ ] Service starts automatically after reboot (if configured as auto-start).
- [ ] UI detects service state correctly: Not installed / Stopped / Running / Failed.
- [ ] UI <-> service channel (named pipe / RPC / IPC) connects, reconnects after service restart, and times out gracefully.
- [ ] Channel is access-restricted: only authorized local users/admin can send commands (no open pipe to everyone).
- [ ] Every command the UI can send is validated by the service (type, range, allow-list). Unknown commands are rejected and logged.
- [ ] Service runs with least privilege; elevation only for the specific actions that need it.
- [ ] Version handshake: UI and service versions are checked; mismatch shows a clear message instead of failing silently.
- [ ] Every tweak is reversible: **Apply -> Revert** returns the system to the exact prior state (registry, services, power plan, etc.).
- [ ] A restore point or backup of changed settings is created before the first apply.
- [ ] Service survives UI crash, and UI survives service crash.
- [ ] No data is sent over the network unless explicitly documented in README (default: **zero telemetry**).

**Evidence required:** test log for each item, service event log excerpt, revert diff.

---

## 2. Digital signature: everything is signed as "James God" [BLOCKER]

Everything that is included in the app **and installed on the PC** must carry a valid
Authenticode signature with publisher/subject name **James God**:

- [ ] Installer (`*.exe` / `*.msi`)
- [ ] Main application `.exe`
- [ ] Service `.exe`
- [ ] Every `.dll` and `.sys` (if any) that we build
- [ ] Uninstaller
- [ ] Any helper/updater executables and signed scripts (`.ps1`) if shipped
- [ ] Third-party binaries that we bundle are listed in `THIRD_PARTY.md` with their original signer (we do **not** re-sign other vendors' files as "James God").

Signature requirements:

- [ ] SHA-256 file digest + RFC 3161 **timestamp** on every signature (so it stays valid after the certificate expires).
- [ ] `Get-AuthenticodeSignature <file>` returns `Status = Valid` for every file.
- [ ] Signer subject contains `James God` on every file (script enumerates all PE files in the install dir and fails if any is unsigned or has a different signer).
- [ ] Installed-files check: after a real install on a clean VM, re-scan `C:\Program Files\<App>` and `%ProgramData%\<App>`; 100% of our binaries are signed.
- [ ] Signing happens **after** the final build and **before** hashing/packaging; nothing is modified after signing.
- [ ] Private key / certificate is never committed to the repo. Use GitHub Actions secrets or a cloud signing service.

Verification commands:

```powershell
# Every PE file in the install folder
Get-ChildItem "$env:ProgramFiles\<App>" -Recurse -Include *.exe,*.dll,*.sys |
  Get-AuthenticodeSignature |
  Select-Object Path, Status, @{n='Signer';e={$_.SignerCertificate.Subject}}

# Strict check with signtool
signtool verify /pa /all /v "<file>"
```

> **Note on the name "James God":** the signing certificate's Subject must
> actually be issued with that name. A self-signed certificate will show the
> name but Windows SmartScreen and antivirus will still treat the file as
> untrusted. For open source, free options such as **SignPath Foundation**
> or **Azure Trusted Signing** are worth checking, and a paid OV/EV
> certificate gives the best SmartScreen reputation. Note that CAs validate
> the legal identity, so confirm the issued Subject text before building the
> pipeline around it.

---

## 3. Crash and stability [BLOCKER]

- [ ] Zero unhandled exceptions in a 30-minute soak test (UI open, service running, apply/revert cycles).
- [ ] Global exception handlers in UI and service: error is logged and a friendly message is shown; the app never exits silently.
- [ ] Crash dumps/logs written locally to `%LocalAppData%\<App>\logs` (never auto-uploaded) with rotation.
- [ ] Tested on: clean Windows 11 (latest), previous Windows 11 build, Windows 10 (if supported), low-RAM VM (4 GB), non-admin user, admin user.
- [ ] Tested on: Intel and AMD CPU, NVIDIA / AMD / Intel GPU (or document unsupported combos).
- [ ] Installer handles: no admin rights, missing runtime (VC++ / .NET / WinUI), upgrade over previous version, repair, and uninstall.
- [ ] Missing dependencies are bundled or auto-detected with a clear message (no "app failed to start" with no explanation).
- [ ] Low-disk-space, locked file, and read-only registry key errors are handled.
- [ ] Static analysis passes: no critical warnings (`/W4` or analyzer equivalent), memory checks (AddressSanitizer / Application Verifier) pass for native code.
- [ ] No memory leak growth > 5% over the soak test.
- [ ] Clean uninstall: no leftover services, scheduled tasks, startup entries, registry keys, or folders.

---

## 4. False-positive / antivirus detection [BLOCKER]

System optimizers touch the registry and services, so they are commonly flagged by
mistake. Reduce that risk and prove it:

- [ ] Windows Defender full scan of the installer, installed folder, and portable zip: **0 detections**.
- [ ] VirusTotal scan of every released file: **0 detections from major vendors** (Microsoft, Kaspersky, ESET, Bitdefender, Avast, Malwarebytes, Norton, McAfee, CrowdStrike, Sophos). Link saved in release notes.
- [ ] Binaries are not packed or obfuscated (no UPX, no protectors). Open source = no reason to hide code.
- [ ] No suspicious behaviors: no code injection, no `WriteProcessMemory` into other processes, no disabling of Defender / Windows Update / firewall / SmartScreen, no hidden persistence, no unsigned downloaded executables.
- [ ] No hidden network calls; if any (update check), it is HTTPS only, documented, and can be turned off.
- [ ] Complete, accurate PE metadata: CompanyName `James God`, ProductName, FileDescription, FileVersion, Copyright, icon, and manifest (`requestedExecutionLevel` set correctly).
- [ ] Build is reproducible from the tagged source (same source = same unsigned binary hash where possible).
- [ ] If any engine flags the release: submit a false-positive report to that vendor (Microsoft Security Intelligence, etc.), record the ticket in `release-report.md`, and **do not publish until cleared** or the flag is documented in the README.
- [ ] SmartScreen check: downloaded installer opens without a "Windows protected your PC" block (or the reputation status is documented honestly).

---

## 5. Open-source / GitHub hygiene [BLOCKER]

- [ ] `LICENSE` present and correct.
- [ ] `README.md` explains what every tweak does, what it changes, and how to revert it.
- [ ] `SECURITY.md` with a vulnerability reporting contact.
- [ ] `THIRD_PARTY.md` listing all dependencies and licenses.
- [ ] `CHANGELOG.md` updated for this version.
- [ ] No secrets, tokens, certificates, `.pfx` files, passwords, or personal paths in the repo or git history (run `gitleaks`).
- [ ] Dependency scan has no known critical/high vulnerabilities.
- [ ] Release built by **GitHub Actions** from a tagged commit (not from a local machine), with build provenance/attestation if available.
- [ ] `.gitignore` excludes `Installer/Output/`, `.vs/`, build artifacts, and keys.

---

## 6. Release artifacts and integrity [BLOCKER]

- [ ] Installer file name format: `JamesOptimizer-vX.Y.Z-Setup.exe`
- [ ] `SHA256SUMS.txt` published with the release, generated **after** signing.
- [ ] Hash of the downloaded asset matches the published hash.
- [ ] Release notes include: version, changes, supported Windows versions, hashes, VirusTotal links, known issues.
- [ ] Version number identical in installer, app "About" page, file properties, and Git tag.
- [ ] In-app updater (if any) verifies the signature and hash before running anything.

---

## 7. Final sign-off

The release is **approved** only when:

1. Sections 1 to 6 show all [BLOCKER] items passed with evidence.
2. `release-report.md` is generated and committed to the release (not the repo root).
3. A clean-VM install, run, revert, and uninstall was completed once by hand.

```
Release version : ______
Date            : ______
Gate result     : PASS / FAIL
Failed items    : ______
Approved by     : James God
```

---

## Instructions for the agent

- Verify each checklist item with real commands and show the output; do not mark anything as passed from assumption.
- If an item cannot be verified in the current environment (e.g. needs a clean VM or the signing certificate), say so explicitly and list it as **UNVERIFIED**, not passed.
- Fix failures in code, rebuild, and re-run the full gate, because a fix can break a previously passing item.
- Never weaken or delete a [BLOCKER] item to make a release pass.>
