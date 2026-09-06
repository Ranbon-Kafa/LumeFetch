# Windows v1 release checklist

## Completed engineering checks

- [x] Maintainer-approved target: Ranbon-Kafa/LumeFetch. Website is excluded from this task.
- [x] Pin official yt-dlp/Deno assets and verify SHA-256 before extraction/execution.
- [x] Build FFmpeg from exact sources; include MP3, Opus and PNG cover support.
- [x] Self-contained Windows x64; .NET/FFmpeg/ffprobe/yt-dlp/Deno are bundled.
- [x] 76 unit cases and real local-media pipeline: queue, hashes, M4A/MP3/Opus, mux and metadata/cover.
- [x] TR/EN bindings and persistence, collection partial failure/retry/cancel in headless Avalonia.
- [x] Installer, version upgrade, portable self-check, uninstall and preserved data on a disposable
  GitHub-hosted Windows Server runner. Initial acceptance run: 34039854933.
- [x] Preserve exact source archives, source hashes, notices, upstream declarations and build recipes.
  See CORRESPONDING-SOURCES.md; this inventory is not a legal opinion.
- [x] Disable Spotify registration/authentication/URL resolution in v1, approved by maintainer.
- [x] Document unsigned status and supported/unsupported scope.
- [x] Inspect final v1 acceptance run 34041489913 and downloaded asset checksums. Tested
  commit: `194d5922344d70bbb4b50d76e432c4f4e1af6673`. All bundled tool and source hashes match.
- [x] Published installer, portable ZIP, corresponding sources, SHA256SUMS and release notes
  together as [v1.0.0](https://github.com/Ranbon-Kafa/LumeFetch/releases/tag/v1.0.0)
  on 2026-09-06. GitHub's hashes match the verified artifacts; release is public,
  non-prerelease and marked latest. Packages remain explicitly unsigned.

## Explicitly unverified / deferred

These are not represented as completed tests or certifications.

- Windows 10 22H2 / Windows 11 clean-machine interactive testing and SmartScreen reputation.
- Full native clipboard/folder-selection persistence, screen-reader and high-DPI audit.
  The native folder picker was opened during local inspection; that alone is not full acceptance.
- Live authorized downloads from each social platform and a complete live YouTube playlist.
  Offline/provider-routing tests do not prove current website availability.
- Code signing. v1 has no signing identity; packages are unsigned. Do not disable Windows security.
- Spotify policy approval and broad account/album/playlist acceptance; the feature is disabled.
- Name/trademark clearance, independent legal review, Linux/macOS packages, dynamic plugins,
  automatic updates and persistence of queue history across restarts.

## User data

Installed settings: `%LOCALAPPDATA%/LumeFetch/settings.json`.
Portable settings: `data/settings.json` beside the executable, selected by `portable.flag`.
Save preferences explicitly. Queue history is in memory. Pause/resume/retry uses
per-job partial files under the chosen folder's `.lumefetch/<job-id>` during the
same session. Cancellation/uninstall does not delete these files or user downloads.
Remove only unneeded per-job directories after closing LumeFetch.

Every binary mirror must also provide the corresponding-source ZIP and notices.
The presence of a license URL alone is not a substitute for required source distribution.
