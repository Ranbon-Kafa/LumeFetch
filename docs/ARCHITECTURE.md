# Architecture

## Goals

LumeFetch separates product behavior from extraction tools. The queue, user
experience, provider selection, resolver pipeline, settings, and processing
contracts belong to LumeFetch even when a provider delegates extraction to an
external backend.

## Dependency direction

```text
Desktop / Android ------> LumeFetch.Presentation -------> LumeFetch.Core
         |                                                     ^
         +--------------> LumeFetch.Infrastructure ------------+
```

`Core` cannot reference Avalonia, operating-system APIs, FFmpeg, yt-dlp, or a
specific storage implementation. Desktop and Android each have a composition root.

`Presentation` has no dependency on Desktop, Infrastructure or Avalonia.Desktop.
It exposes a `MainView` UserControl, the original command/view-model surface and
the shared theme. Desktop's MainWindow only owns window lifetime and shutdown.
Native hosts must supply working providers, processing, storage and lifecycle
services; they must not reuse desktop subprocess adapters on iOS or label a
UI-only build as a feature-equivalent mobile release. See [MOBILE.md](MOBILE.md).

Settings are injected through `ISettingsStore`; catalog login through
`ICatalogSession`. Settings defaults can point to a host's sandbox instead of
assuming a desktop Downloads directory. Generated JSON metadata and compiled
Avalonia bindings avoid reflection-dependent model access. The tests explicitly
disable reflection-based JSON serialization. This is preparation, not an iOS AOT
build certification.

## Provider contract

An `IMediaProvider` owns four responsibilities:

1. Determine whether it can handle a normalized URI.
2. Analyze that URI and return a platform-neutral `MediaInfo` model.
3. Expose concrete `DownloadOption` choices rather than UI labels.
4. Download the selected option while reporting progress and observing queue
   controls.

Provider ordering is deterministic. A platform provider should use a higher
priority than the generic HTTP fallback.

## Resolver contract

An `IMediaResolver` returns a catalog of metadata-only tracks. It never returns
protected bytes. Spotify, Apple Music, and Deezer integrations belong here.
`ITrackSearch` searches independent sources after the user chooses to search.
`TrackMatcher` scores normalized title/artist, duration and source heuristics.
ISRC is optional metadata, not a currently implemented matching signal.
Only the reviewed candidate URI enters the normal provider/download pipeline.

Spotify uses an own-Client-ID PKCE public client with a state-validated loopback
callback. Credentials remain in memory. API pagination is limited to Spotify's
HTTPS API host, and the composition root disables redirects for that HTTP client.
See SPOTIFY.md for access and policy limitations.

## Collections and localization

`IMediaCollectionProvider` expands a playlist into ordered `CollectionEntry`
rows, preserving unavailable items. The shared presentation layer snapshots selected rows,
destination and quality before sequential per-item analysis. It checks cancellation
before enqueueing each actual format. Jobs then use the application's normal
bounded queue. Stopping preparation does not cancel already queued downloads.
Source changes invalidate old preparations; retried selections exclude rows
already enqueued. The current preview caps collections at 200 entries.

Core embeds key-based English/Turkish dictionaries with English fallback.
The presentation layer's localizer invalidates label bindings at runtime.
Translated strings are not used as durable queue states or format IDs.

## Plugin boundary

The first release compiles built-in providers directly for a smaller attack
surface. The contracts are intentionally assembly-neutral. The planned plugin
loader will discover signed or explicitly trusted assemblies from an isolated
plugin directory, validate an API version, and register implementations through
the same registry used by built-in providers.

Third-party plugins will not receive unrestricted secrets by default. Network,
filesystem, cookies, and credential access require explicit capabilities.

## Download lifecycle

```text
Queued -> Downloading -> Processing -> Completed
   |          |              |
   |          +-> Paused     +-> Failed
   |          +-> Failed
   +-> Canceled
```

The manager owns concurrency, state transitions, cancellation, retry, and UI
notifications. Providers own protocol-specific transfer behavior. Pausing cancels
the active attempt, keeps its job ID and partial files, and releases its slot.
Resume/retry queues a new attempt only after the old attempt has fully exited.
Processing can be canceled, but is not pauseable.

Direct HTTP uses a job-isolated partial file plus a strong ETag. Resume requests
send Range and If-Range, validate the returned range, and restart safely if the
entity or range support changed. Outputs use atomic no-overwrite moves; parallel
jobs never share a staging path. Terminal state cannot be reverted by late progress.
Desktop keeps its existing in-memory queue. Android injects `IDownloadQueueStore`,
using a private schema-v1 JSON checkpoint with same-directory atomic replacement
and a flushed temporary file. Job IDs, formats, destinations, progress and history
survive process death. Unfinished jobs restore paused and never auto-start.
Progress checkpoints are throttled to two seconds; state transitions are not.
The provider's actual partial file/ETag remains authoritative on resume.

`IDownloadOutput` separates provider completion from platform publication. Android
uses a persisted SAF tree grant, private transfer staging and a fresh document
per export. Known export failures retain the completed local file for retry.
An export-intent checkpoint precedes any external document creation; if killed
in that window, the restored row requires manual folder review and cannot retry.
This avoids silently duplicating potentially committed files. SAF is not an
atomic filesystem rename, and cleanup of an interrupted document may be manual.

Unreadable/unsupported journals are preserved, with new work blocked. Save
failures latch the scheduler closed and surface a UI warning. The current journal
is bounded to 2,000 jobs / 16 MiB; history pruning/recovery UI remains future work.
These are process-death guarantees, not a power-loss durability certification.
Paths are host-validated; Android records stay in private storage with backup
disabled. URLs may contain access parameters, so do not include journals in
public diagnostics. Settings are also schema-versioned JSON saved atomically.

## External tools

- FFmpeg is invoked through `IFFmpegService`; arguments are never assembled by
  the UI.
- YtDlpMediaProvider implements shared format mapping; concrete social providers
  register exact host allowlists and stable IDs. YtDlpClient owns subprocess
  invocation, UTF-8 JSON/progress parsing, cancellation and per-job staging.
  It ignores global/user downloader config and external backend plugins.
  It is an extraction backend, not the application architecture.
- Tool discovery and version checks are centralized so portable bundles and
  system installations behave consistently.

## Versioning

- Application releases follow semantic versioning.
- Plugin API compatibility uses a separate integer contract version.
- Persisted settings and optional queue checkpoints each use their own schema
  version 1. Unsupported queue schemas are not overwritten.
