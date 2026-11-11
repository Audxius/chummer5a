# Contact portrait preparation

`dotnet run --project tools/native/ContactArt -- input.chum5 artwork-directory output.chum5`

The artwork directory contains PNG files and `prompts.json`, an array with `id` (contact GUID) and `slug` (PNG filename without extension). Additional prompt metadata is allowed. The tool prepares a **new** file and refuses to overwrite an existing output or the input. It appends the portraits using Chummer's existing `mugshots`/`mainmugshotindex` fields, retaining older portraits. All contacts in the manifest must exist exactly once.

An optional fourth argument supplies a previous artwork directory: images matching that directory's PNG bytes are replaced in place. Use `--replace-main` instead when explicitly replacing each contact's current main portrait; other portraits remain intact. Already-present matching images are reused. The selected output image is verified before writing.

Before writing, it compares the parsed input and output with only those portrait fields excluded. Unexpected changes abort the operation. A `.source-sha256` sidecar records the input hash. Back up the input and compare its current hash with the sidecar before installing a prepared copy, so newer edits are never replaced. Reload the character in any running editor before saving again.

Personal artwork, reference images and character backups belong in ignored `output/contact-art/`, not in Git.
