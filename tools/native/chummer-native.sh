#!/usr/bin/env bash
# Launch Chummer Native on NixOS: builds Chummer.Desktop when sources changed, then runs it with the native libraries
# Avalonia needs (Wayland/X11, EGL/GL, fontconfig, xkbcommon) on the library path.
#   CHUMMER_BACKEND=x11 ./chummer-native.sh   # force X11 (XWayland) instead of native Wayland
set -euo pipefail
repo="$(cd "$(dirname "$(readlink -f "$0")")/../.." && pwd)"
out="$repo/Chummer.Desktop/bin/Release/net8.0"
stamp="$out/.built-from"
# Hash the source contents, including new files. A hash of `git status` misses edits to files
# that were already modified when the last build ran.
source "$repo/tools/native/source-fingerprint.sh"
rev="$(chummer_source_fingerprint "$repo")"

pkgs="fontconfig.lib freetype libglvnd mesa wayland libxkbcommon libX11 libICE libSM libXcursor libXext libXi libXrandr libXrender"
libpath=""
for p in $pkgs; do
  d="$(nix build --no-link --print-out-paths "nixpkgs#$p" 2>/dev/null | grep -v -e "-man$" -e "-dev$" -e "-doc$" -e "-bin$" | head -1)"
  [ -n "$d" ] && libpath="$libpath:$d/lib"
done
sdk="$(nix build --no-link --print-out-paths nixpkgs#dotnet-sdk_8 2>/dev/null | head -1)"
export DOTNET_ROOT="$sdk/share/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if [ ! -f "$stamp" ] || [ "$(cat "$stamp")" != "$rev" ]; then
  echo "Building Chummer Native..."
  dotnet build "$repo/Chummer.Desktop/Chummer.Desktop.csproj" -c Release -v q --nologo
  echo "$rev" > "$stamp"
fi

export LD_LIBRARY_PATH="${libpath#:}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec dotnet "$out/chummer.dll" "$@"
