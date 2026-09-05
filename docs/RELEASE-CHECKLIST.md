# Windows preview release checklist

The generated installer and portable zip are unsigned local preview artifacts.
Neither this script nor the workflow publishes a GitHub Release.

- [x] Pin tool versions and verify archive SHA-256 before extraction/execution.
- [x] Build self-contained Windows x64; users do not need a .NET SDK.
- [x] Include FFmpeg/ffprobe (shared LGPL build), yt-dlp and Deno.
- [x] Preserve user downloads and settings during uninstall.
- [ ] Test installation, upgrade, uninstall, folder picker and clipboard on a clean Windows 10 22H2 / Windows 11 machine.
- [ ] Smoke-test authorized, public single-video examples for each social provider. Routing tests do not prove a platform remains available.
- [ ] Test an authorized YouTube playlist end-to-end, including unavailable items and cancellation.
- [ ] Complete real-account Spotify PKCE/catalog/matching acceptance, quota eligibility and developer policy review (including cross-service use and official branding/attribution). No Spotify approval is implied.
- [ ] Check Turkish/English with native keyboard, screen reader and high-DPI scaling.
- [ ] Obtain a code-signing identity and sign both app and installer. Unsigned previews can trigger SmartScreen.
- [ ] Complete third-party redistribution audit. Include corresponding sources, patches, build configuration and required notices for the exact FFmpeg build and its dependencies, and all bundled standalone runtimes. Mirror required source archives alongside the binaries on GitHub Releases and on the website.
- [ ] Review project name availability, choose the actual GitHub remote and website URL, and set release links.
- [x] Maintainer-approved publication target: Ranbon-Kafa/LumeFetch; website: https://ruzgarefe.com. MIT terms retained.
- [ ] Publish release notes with known limitations and SHA256SUMS.txt.

FFmpeg source revision: 5c8e7e2433, build recipe: BtbN/FFmpeg-Builds autobuild-2026-09-05-13-10.
The pinned binary manifest is in packaging/windows/dependencies.json.
See [FFmpeg's redistribution guidance](https://ffmpeg.org/legal.html) and [upstream build scripts](https://github.com/BtbN/FFmpeg-Builds/tree/autobuild-2026-09-05-13-10).
The presence of a license text or an upstream URL alone is not a completed source-distribution audit.

## Settings and partial downloads

The installer uses %LOCALAPPDATA%/LumeFetch/settings.json. Portable builds contain
portable.flag and use data/settings.json beside LumeFetch.exe. Save settings explicitly.
Queue history is in memory in v0.2. Paused/canceled jobs retain partial files inside
the chosen folder's .lumefetch/<job-id>/ directory; retry/resume works in the same
app session. These partial files are not automatically deleted on cancel or uninstall.
After closing LumeFetch, users may remove only unneeded per-job partial directories.
