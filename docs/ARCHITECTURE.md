# Architecture

## Goals

LumeFetch separates product behavior from extraction tools. The queue, user
experience, provider selection, resolver pipeline, settings, and processing
contracts belong to LumeFetch even when a provider delegates extraction to an
external backend.

## Dependency direction

```text
LumeFetch.Desktop --------> LumeFetch.Core
         |                         ^
         +--> LumeFetch.Infrastructure
                                    |
                                    +--> LumeFetch.Core
```

`Core` cannot reference Avalonia, operating-system APIs, FFmpeg, yt-dlp, or a
specific storage implementation. `Desktop` is the composition root.

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
rows, preserving unavailable items. The desktop layer snapshots selected rows,
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
The queue is in memory; settings are schema-versioned JSON saved atomically.

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
- Persisted settings currently use schema version 1; future persisted queue state
  will have its own schema version.
