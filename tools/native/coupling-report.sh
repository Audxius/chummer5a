#!/usr/bin/env bash
# Counts how much of the engine (Chummer/Backend) still depends on WinForms / Windows.
# Phase 1 of the native port is done when every row reaches 0 (the engine can then move to Chummer.Core).
# UI helper files that will move out of Backend wholesale are reported separately.
set -eu
cd "$(dirname "$0")/../../Chummer/Backend"

ui_helpers='Static/Extensions/WinFormsExtensions.cs|Static/Extensions/DispatcherExtensions.cs|Helpers/CenterableMessageBox.cs|Helpers/TooltipFactory.cs|Helpers/HoverDisplayCoordinator.cs|Helpers/DataGridViewTextBoxColumnTranslated.cs|Datastructures/ListViewItemWithValue.cs'

count() { # $1 = label, $2 = regex
    local sites files
    sites=$(grep -rE --include=*.cs "$2" . | grep -vE "$ui_helpers" | grep -vE "^[^:]+:\s*//" | wc -l)
    files=$(grep -rE --include=*.cs "$2" . | grep -vE "$ui_helpers" | grep -vE "^[^:]+:\s*//" | cut -d: -f1 | sort -u | wc -l)
    printf '%-46s %6s sites %5s files\n' "$1" "$sites" "$files"
}

echo "Engine coupling report ($(git rev-parse --short HEAD 2>/dev/null || echo '?'))"
count 'using System.Windows.Forms'                  '^using System\.Windows\.Forms'
count 'Program.* (WinForms host)'                   '\bProgram\.[A-Z]'
count 'Message boxes (Program.ShowScrollable...)'   'ShowScrollableMessageBox'
count 'Dialogs (ThreadSafeForm / ShowDialogSafe)'   'ThreadSafeForm<|ShowDialogSafe'
count 'UI thread (RunOnMainThread / DoThreadSafe)'  'RunOnMainThread|DoThreadSafe'
count 'Message pump (DoEvents)'                     'DoEvents'
count 'Tree/menu building (TreeNode, ContextMenu)'  '\bTreeNode\b|ContextMenuStrip|\bTreeView\b'
count 'Cursor (CursorWait / UseWaitCursor)'         'CursorWait|UseWaitCursor'
count 'Images (System.Drawing.Image/Bitmap)'        '\bImage\b|\bBitmap\b'
count 'Registry'                                    '\bRegistry(Key)?\b'
count 'Colors / ColorManager'                       'ColorManager\.'
count 'P/Invoke (NativeMethods)'                    'NativeMethods\.|DllImport'
echo
echo "UI helper files still under Backend (move out wholesale):"
for f in ${ui_helpers//|/ }; do [ -f "$f" ] && echo "  $f"; done
