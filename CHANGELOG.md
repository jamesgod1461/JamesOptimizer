# Changelog

All notable changes to **JamesOptimizer** are documented in this file.

## [3.0.0] - 2026-11-25

### Added
- Native **WinUI 3 + Win2D** desktop client (`JamesOptimizer.WinUI`) paired with hardened **Windows Background Service** (`JamesOptimizer.Service`).
- Atomic **Apply -> Revert** snapshot engine (`TweakBackupService` + `WriteAheadJournal`) covering Registry values/tombstones, Windows Services, Power Plans, BCD flags, HOSTS entries, and power settings.
- Authenticode-verified Named Pipe IPC (`\\.\pipe\JamesOptimizerPipe`) with `LocalSystem` + `Administrators` ACLs, `Network` SID deny, allow-listed commands, rate limiting, and version handshake (`HELLO`).
- DPAPI-protected HMAC-SHA256 v2 profile export/import with dry-run preview and confirmation gate for unverified/legacy profiles.
- Production release verification script (`Installer\Verify-Release.ps1`), unified PE metadata (`James God` / `3.0.0`), and GitHub Actions release workflow.
