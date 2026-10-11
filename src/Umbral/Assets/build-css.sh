#!/usr/bin/env bash
# Compiles umbral.css from umbral.input.css and tailwind.config.js.
#
# Why this exists: an identity provider's pages carry credentials, so they load
# no third-party code. The Tailwind Play CDN did exactly that (and compiled the
# CSS in every visitor's browser), so the CSS is compiled once, here, committed,
# and served by Umbral itself. The standalone Tailwind CLI is a single binary:
# no Node, no npm, no bundler. It is pinned by version and SHA-256 and cached
# under ${XDG_CACHE_HOME:-$HOME/.cache}/umbral.
#
# Usage:
#   src/Umbral/Assets/build-css.sh           regenerate umbral.css (then commit it)
#   src/Umbral/Assets/build-css.sh --check   fail if the committed umbral.css is stale
#
# Run it after changing a view, tailwind.config.js or umbral.input.css. The Docker
# build runs `--check`, so an image cannot ship CSS that lacks a class a view uses.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

version="3.4.19"

case "$(uname -s)/$(uname -m)" in
  Linux/x86_64 | Linux/amd64)
    platform="linux-x64"
    expected="4af3198c015616ea7d6617974ec3d70d987ecc00c1ca8463b0a30fd65cc7c06e"
    ;;
  Linux/aarch64 | Linux/arm64)
    platform="linux-arm64"
    expected="e5b2d27694daa80cc52ec29553ba2c6bd43d86bd51a9d633ed24058b9c05a676"
    ;;
  Darwin/arm64)
    platform="macos-arm64"
    expected="7fdeb00818b6214a337383063282b2361ecb08bbc08f8c8a7ba97ee1e2eaa4fe"
    ;;
  Darwin/x86_64)
    platform="macos-x64"
    expected="a597f407e0f1f03535731f5b42f1576a8152cb5fffc2f38e754722bc0c280045"
    ;;
  *)
    echo "build-css.sh: unsupported platform $(uname -s)/$(uname -m); the Tailwind CLI is published for Linux and macOS on x64 and arm64." >&2
    exit 1
    ;;
esac

sha256() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | cut -d' ' -f1
  else
    shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

cache="${XDG_CACHE_HOME:-$HOME/.cache}/umbral"
cli="$cache/tailwindcss-$version-$platform"

if [ ! -x "$cli" ]; then
  mkdir -p "$cache"
  tmp="$(mktemp "$cache/download.XXXXXX")"
  trap 'rm -f "$tmp"' EXIT
  curl -fsSL "https://github.com/tailwindlabs/tailwindcss/releases/download/v$version/tailwindcss-$platform" -o "$tmp"
  actual="$(sha256 "$tmp")"
  if [ "$actual" != "$expected" ]; then
    echo "build-css.sh: tailwindcss-$platform $version has SHA-256 $actual, expected $expected; not using it." >&2
    exit 1
  fi
  chmod +x "$tmp"
  mv "$tmp" "$cli"
fi

if [ "${1:-}" = "--check" ]; then
  out="$(mktemp)"
  trap 'rm -f "$out"' EXIT
  "$cli" -c tailwind.config.js -i umbral.input.css -o "$out" --minify
  if ! cmp -s "$out" umbral.css; then
    echo "src/Umbral/Assets/umbral.css is stale: run src/Umbral/Assets/build-css.sh and commit the result" >&2
    exit 1
  fi
elif [ $# -eq 0 ]; then
  "$cli" -c tailwind.config.js -i umbral.input.css -o umbral.css --minify
else
  echo "usage: build-css.sh [--check]" >&2
  exit 2
fi
