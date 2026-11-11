# Notes pages and legacy compatibility

The native notebook offers named pages, reorder and delete actions. All page text remains in the existing `Character.Notes` string and normal `<notes>` XML field. No external files or new XML schema are required. Unedited legacy notes are never rewritten merely by opening the notebook; a single unnamed legacy page retains its original plain text on edit.

Multiple pages or a renamed page use readable markers:

```text
<!-- chummer-note:v1 30bd81ac-516b-4c5b-811c-a880334a4899 "Session one" -->
Meet the fixer.
<!-- /chummer-note:30bd81ac-516b-4c5b-811c-a880334a4899 -->
```

The quoted title uses JSON string escaping. Body lines beginning with marker syntax are escaped with one backslash; the parser removes that escape. Text added outside recognized sections in legacy Chummer becomes an Imported notes page. Malformed or incomplete markers remain readable text. Original Chummer can read and edit the entire Notes field, including these visible markers; its UI does not display separate pages.

Page title and body bindings update immediately, with a short serialization debounce. Save, Save As, export and close flush pending notebook changes and await the existing ordered metadata writes. Tests cover legacy text, Unicode, duplicate titles, stable page identities, order, marker-like text, external edits and ordinary character-file save/reload. The headless interaction review also types and immediately saves with both the mouse and Ctrl+S, then adds, renames, reorders and deletes a page before reloading.
