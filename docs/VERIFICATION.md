# Verification record — 2026-09-05

## v1 preparation — 2026-09-06

- Final packaged v1 acceptance passed:
  https://github.com/Ranbon-Kafa/LumeFetch/actions/runs/34041489913, commit
  `194d5922344d70bbb4b50d76e432c4f4e1af6673` (tag `v1.0.0`). Installer, portable
  and corresponding-source archives downloaded from that run match SHA256SUMS.
  Every bundled tool and all 1,079 source archive/input hashes were rechecked.
  Required .NET/font notices, portable marker and source recipes are present;
  portable contains no local settings, credential files or private build path.
  Run `scripts/Verify-ReleaseAssets.ps1 -Directory <downloaded-artifacts> -Commit
  194d5922344d70bbb4b50d76e432c4f4e1af6673` to repeat the read-only archive checks.

- Native Windows application opened; navigation and URL controls exposed an
  accessibility tree. The real Windows folder picker opened successfully.
  This does not constitute a full screen-reader/high-DPI audit.
- Maintainer completed Spotify sign-in manually. In that live session, the app
  retrieved Rick Astley's "Never Gonna Give You Up" title/artist/album from Spotify,
  then found a YouTube candidate scoring 100/100. Permission remained unchecked
  and the queue action remained disabled. No media was downloaded in this test.
- Native text entry triggered Smart Paste analysis. Clipboard-button acceptance
  is separate and is not inferred from typing a URL.
- Release build and all 76 unit cases passed after adding localized handling
  for clipboard and folder-picker failures.
- Source-built FFmpeg 9.0.1 with LAME/Opus/zlib passed real MP3, M4A, Opus, mux,
  PNG-cover and metadata checks. 1,079 original source archives/build inputs were
  collected with hashes, notices and explicit declaration-only provenance for
  13 Rust crates whose upstream provides no separate license file. Collection
  success is not legal certification.
- `Windows release acceptance` tests source-built tools, the real media pipeline,
  installer upgrade, portable extraction and uninstall on a disposable hosted
  Windows worker. Initial acceptance passed on 2026-09-06:
  https://github.com/Ranbon-Kafa/LumeFetch/actions/runs/34039854933.
  This is Windows Server automation, not a Windows 10/11 desktop certification.
- The maintainer approved disabling Spotify in v1. Runtime registration was
  removed; new UI checks confirm rejected Spotify URLs and no OAuth browser launch.
  Its experimental fixtures continue to test the retained source in isolation.
- ruzgarefe.com deployment is explicitly excluded at the maintainer's request.

## Automated checks

- Release compilation with warnings treated as errors.
- 76 xUnit cases: provider priority/routing and lookalike hosts, media analysis,
  generic HTTP content validation, interrupted transfer/range resume, changed
  entities, parallel filename collisions, queue limits, pause/resume, cancel/retry,
  terminal progress guards, shutdown, portable filenames and settings round trips;
  playlist parsing/limits/unavailable rows, real format selection, matcher weights/
  version conflicts, translation key/placeholder parity, Spotify URL routing,
  catalog pagination, API errors, hostile pagination and OAuth state validation.
- MP3 mapping for AAC/Opus/combined/native MP3 sources, best-source selection,
  no-audio rejection, strict batch MP3 selection, FFmpeg availability, and
  conversion failure/retry without committing invalid output.
- Native pipeline smoke: FFmpeg-generated MPEG-4/AAC test clip; loopback HTTP
  download through the real queue; byte-for-byte SHA-256 comparison; real yt-dlp
  JSON and progress parsing; FFmpeg video/audio mux and audio extraction; ffprobe
  verification of M4A audio-only streams and embedded title metadata.
- Real MP3 creation through both yt-dlp and direct HTTP download/FFmpeg; ffprobe
  verifies the mp3 codec/container and absence of video, plus yt-dlp ID3 title.
- Real Avalonia view models under a headless Skia renderer: URL analysis, quality
  selection, enqueue/progress, Settings navigation/save/live queue limit, stale
  analysis suppression and asynchronous shutdown; playlist partial failure/retry,
  Spotify candidate review, low-confidence manual selection, runtime TR/EN label
  updates, persisted language and superseded-batch isolation.
- Actual loopback OAuth callback with simulated browser and mock HTTPS token handler:
  PKCE verifier/challenge, no client secret, token refresh and disconnect.
- Live yt-dlp flat YouTube search for "Kevin MacLeod Carefree official audio"
  returned five public metadata candidates, including titles, durations, channel
  names and verification flags. No audio/video was downloaded in this check.
- Main, playlist, Spotify matching and Settings screens rendered to PNG and reviewed.

The native smoke test exercises a non-ASCII Windows workspace path (Masaüstü).
It caught and fixed subprocess output encoding; paths are now UTF-8 end to end.
Fixture data in screenshots is labeled as sample data.
Published screenshots use fixture folder names instead of a developer's
username or local workspace path.

## Not verified by these checks

- Full native clipboard/folder selection, accessibility and OS window behavior.
- Windows 10/11 clean-machine interactive install and SmartScreen behavior.
- Broad Spotify developer eligibility and live album/playlist access. The single
  live track check above does not establish platform policy approval. See SPOTIFY.md.
- Live downloading from Instagram, TikTok, X or Reddit; only provider routing and
  shared format mapping are covered offline. YouTube metadata was checked during
  the initial implementation, but this is not a full live-platform acceptance run.
- Linux/macOS runtime behavior.
- Independent legal/trademark certification (see RELEASE-CHECKLIST.md).

Run the checks from the repository root. Smoke outputs use unique directories
under artifacts/pipeline-smoke and artifacts/ui-smoke; no user media is overwritten.
