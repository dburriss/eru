#!/bin/sh
# Runs eru against this sample with an empty, throwaway config and cache dir, so your own global
# sources and cache never leak into the generated site. Usage: ./run.sh site generate -o /tmp/out
# Set ERU to a different command if `eru` is not on the PATH (for example "dotnet run --project ../../src/Eru.Cli --").
here=$(cd "$(dirname "$0")" && pwd)
iso="${ERU_SAMPLE_HOME:-${TMPDIR:-/tmp}/eru-theme-site-home}"
mkdir -p "$iso/config" "$iso/cache"
cd "$here" || exit 1
XDG_CONFIG_HOME="$iso/config" XDG_CACHE_HOME="$iso/cache" ${ERU:-eru} "$@"
