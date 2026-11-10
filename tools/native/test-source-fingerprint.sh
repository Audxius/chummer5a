#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/source-fingerprint.sh"
scratch="$(mktemp -d /tmp/chummer-source-fingerprint.XXXXXX)"
trap 'rm -rf -- "$scratch"' EXIT
git -C "$scratch" init -q
mkdir -p "$scratch/Chummer.Desktop"
printf 'bin/\n' > "$scratch/.gitignore"
printf 'original\n' > "$scratch/Chummer.Desktop/Source.cs"
git -C "$scratch" add .
git -C "$scratch" -c user.name=Test -c user.email=test@example.invalid commit -qm 'Initial fixture'
initial="$(chummer_source_fingerprint "$scratch")"
printf 'first edit\n' > "$scratch/Chummer.Desktop/Source.cs"
first="$(chummer_source_fingerprint "$scratch")"
test "$initial" != "$first"
printf 'second edit\n' > "$scratch/Chummer.Desktop/Source.cs"
second="$(chummer_source_fingerprint "$scratch")"
test "$first" != "$second"
touch "$scratch/Chummer.Desktop/Source.cs"
test "$second" = "$(chummer_source_fingerprint "$scratch")"
printf 'new source\n' > "$scratch/Chummer.Desktop/New.cs"
untracked="$(chummer_source_fingerprint "$scratch")"
test "$second" != "$untracked"
printf 'new source, edited\n' > "$scratch/Chummer.Desktop/New.cs"
edited="$(chummer_source_fingerprint "$scratch")"
test "$untracked" != "$edited"
mkdir -p "$scratch/Chummer.Desktop/bin"
printf 'build output\n' > "$scratch/Chummer.Desktop/bin/output"
printf 'private local settings\n' > "$scratch/Beggers.xml"
test "$edited" = "$(chummer_source_fingerprint "$scratch")"
printf 'Source fingerprint regression checks passed.\n'
