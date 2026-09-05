# Changelog

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
