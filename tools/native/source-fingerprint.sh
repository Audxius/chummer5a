#!/usr/bin/env bash
# Shared by the launcher and its regression check. Build outputs and local settings are excluded.
chummer_source_fingerprint() {
  local repo_path="$1"
  local source_file
  local source_paths=(Chummer.Desktop Chummer.Core Chummer/Backend Chummer/7zip Chummer/data Chummer/lang Chummer/customdata
                      Chummer/sheets Chummer/export Chummer/houserules tools/native/chummer-native.sh
                      tools/native/source-fingerprint.sh Directory.Build.props Directory.Build.targets NuGet.Config)
  {
    git -C "$repo_path" rev-parse HEAD
    git -C "$repo_path" diff --binary HEAD -- "${source_paths[@]}"
    while IFS= read -r -d '' source_file; do
      sha256sum "$repo_path/$source_file"
    done < <(git -C "$repo_path" ls-files --others --exclude-standard -z -- "${source_paths[@]}")
  } | sha256sum | cut -d ' ' -f 1
}
