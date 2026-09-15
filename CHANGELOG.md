# Changelog

## Android 1.1.0-beta.1 — 2026-09-15

- First public ARM64 Android beta, version code 10, with a permanent release key.
- Removed the temporary developer diagnostics card; friendly startup retry remains.
- Bundled native/Python source archives, transitive notices and checksum files
  accompany the APK. .NET/Android/Avalonia/graphics/Java and font notices are included.
- Maintainer confirms build 7 MP3 and MP4 downloads on S25 Ultra / Android 16.
  Wider device, provider and background/storage acceptance is still beta work.

- Extracted the existing feature-complete UI into the portable Presentation project;
  Windows continues using the same view models, commands and backend services.
- Responsive navigation, forms, format rows, playlist and queue controls, with
  320–1220px headless layout/command verification and live Turkish/English labels.
- Compiled UI bindings and reflection-free settings/provider JSON for mobile/AOT
  preparation; existing desktop settings and download plan schemas are retained.
- Host-supplied settings defaults and catalog-session contracts; Spotify stays disabled.
- Native Android host with pinned embedded Python/yt-dlp/QuickJS/FFmpeg, safe
  runtime extraction, process-group cancellation and platform tool commands.
- SAF folder grants/export, shared URLs, foreground notification/wake-lock
  lifecycle and export retry without redownloading completed media.
- Private Android queue/history checkpoints, paused process-death recovery,
  export-ambiguity protection and preserved corrupt journals. Force-stop/resume
  verified on the emulator with matching output hashes. Download navigation now
  scrolls to the queue controls even when its heading is already visible.
- ARM64 Debug and x86_64 Debug/Release APK builds with development signing.
  Emulator-tested native MP3/Opus, UI MP3 export, pause/HTTP-range resume and
  background completion with matching hashes; 110 unit cases pass.
- Tool diagnostics preserve native startup errors, and processing-required
  transfers check FFmpeg/FFprobe before downloading media.
- Corrected Android Python packaging to exclude SDK linker aliases that shadowed
  Samsung's system crypto library. Private OpenSSL/SQLite dependencies remain
  bundled; 13 packaging-policy regression cases guard against recurrence.
  The maintainer's build 7 physical-device retest passed MP3 and MP4.
- Spotify remains disabled, no iOS host/IPA is implemented, and Windows v1.0.0
  release assets are unchanged. See docs/MOBILE.md for exact beta limitations.

## 1.0.0 — 2026-09-06

- Graceful, localized recovery if the Windows clipboard or folder picker fails.
- Pinned-source build scripts for a smaller LGPL FFmpeg with MP3/Opus/PNG support.
- Exact source and license collection for bundled third-party tools.
- Isolated Windows installer/upgrade/uninstall and portable acceptance workflow.
- Spotify is disabled in the distributed app, with maintainer approval, pending
  service-policy review. Experimental code/tests are retained for future assessment.
- All media tools bundled; no first-launch dependency downloads. Packages are unsigned.
- Source archives, build recipes, upstream notices/declarations and hashes accompany binaries.

## 0.2.1 — MP3 / initial public source preview

- MP3 options for supported platform media, with real FFmpeg encoding.
- MP3 batch preset for YouTube playlists and Spotify-to-YouTube matches.
- Direct HTTP video/audio-to-MP3 conversion when FFmpeg is available.
- Native MP3 inputs are retained without an unnecessary duplicate option.
- Queue entries display output format; conversion size is not invented.
- 76 unit cases and real MP3 codec/container/metadata verification.
- MIT copyright and project/installer metadata linked to ruzgarefe.com.
- Public-source hygiene: build diagnostics/credentials excluded, screenshot
  paths use fixtures, and the Core Downloads source folder is included.
- Bundled binary distribution remains gated by the release checklist.

## 0.2.0 — Collections and catalog preview

- LumeFetch cover, mint download monogram, multi-resolution Windows icon.
- YouTube/YouTube Music playlist preview, item selection and batch quality.
- Per-item analysis, permission confirmation, partial failures and batch cancellation.
- Spotify track/album/playlist metadata through own-client PKCE authentication.
- Independent YouTube search, weighted matching, alternate sources and manual review.
- Turkish/English interface with live switching and persisted preference.
- 64 unit cases plus real local-media, simulated OAuth and headless UI smoke.
- Spotify real-account acceptance and public distribution policy review remain open.

## 0.1.0 — Windows preview

- URL-first Avalonia desktop UI with debounced Smart Paste, thumbnails and real format choices.
- Provider abstraction and adapters for YouTube, Instagram, TikTok, X, Reddit and direct HTTP media.
- Application-owned queue with bounded concurrency, progress, pause/resume, cancel/retry and clean shutdown.
- Job-isolated partial files and atomic, collision-safe output commits.
- Strong-ETag HTTP resume, response validation and inactivity timeouts.
- FFmpeg merge/audio extraction, optional metadata and compatible cover-art embedding.
- Persistent settings and 1–6 configurable parallel downloads.
- SHA-256-pinned tool provisioning, portable Windows ZIP and per-user installer scripts.
- Offline unit tests, real local-media pipeline smoke and headless UI checks.

This preview is unsigned. Dynamic plugins, queue persistence
across restarts and automatic updates are deferred. Public release gates are
documented in docs/RELEASE-CHECKLIST.md.
