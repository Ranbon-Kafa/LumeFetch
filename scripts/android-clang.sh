#!/usr/bin/env bash
set -euo pipefail
exec "${LUMEFETCH_NDK_CLANG:?}" --target="${LUMEFETCH_ANDROID_TARGET:?}26" "$@"
