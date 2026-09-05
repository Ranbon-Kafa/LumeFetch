# Contributing to LumeFetch

Thanks for helping build a respectful, maintainable media tool.

## Before opening a pull request

1. Discuss large behavior or public-contract changes in an issue first.
2. Confirm the source permits the behavior your provider implements.
3. Do not add DRM circumvention, paywall bypasses, credential harvesting, or
   stealth measures.
4. Keep platform-specific code outside `LumeFetch.Core`.
5. Add focused tests for URL matching, option mapping, and error handling.

## Development workflow

```powershell
dotnet restore LumeFetch.sln
dotnet build LumeFetch.sln --configuration Release
dotnet test LumeFetch.sln --configuration Release
```

Format touched C# files with `dotnet format` before submitting. Use small,
descriptive commits and explain user-visible changes in the pull request.

## Adding a provider

- Implement `IMediaProvider`.
- Use a narrow `CanHandle` rule; the generic provider is the fallback.
- Return structured formats, codecs, dimensions, and estimated size where the
  source exposes them.
- Support cancellation. Support pause/resume when the transport can do so
  safely.
- Never silently select a lower-quality or unrelated item.
- Include URLs that must match and URLs that must not match in unit tests.

## Reporting security issues

Please follow [SECURITY.md](SECURITY.md) rather than opening a public issue for
vulnerabilities.
