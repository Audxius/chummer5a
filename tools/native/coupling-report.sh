#!/usr/bin/env bash
# Guard: the engine (Chummer.Core, sources in Chummer/Backend) must stay free of UI toolkits.
# Phase 1 of the native port finished with the engine compiling as a net8.0 library without WinForms/WPF or
# System.Drawing.Common; this fails if any such dependency creeps back in.
set -eu
cd "$(dirname "$0")/../../Chummer/Backend"
status=0
for pattern in 'System\.Windows\.Forms' 'System\.Windows\.Threading' 'System\.Drawing\.(Image|Bitmap|Graphics|Icon)\b' 'Microsoft\.Win32' '\bProgram\.[A-Z]'; do
    hits=$(grep -rnE --include=*.cs "$pattern" . | grep -vE '^[^:]+:[0-9]+:\s*//' || true)
    if [ -n "$hits" ]; then
        echo "Engine references UI/platform API ($pattern):"
        echo "$hits" | head -20
        status=1
    fi
done
[ $status -eq 0 ] && echo "Engine is UI-toolkit free."
exit $status
