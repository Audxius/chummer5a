# Windows reference outputs

`windows-baseline-42553325872.tar.gz` bundles all 750 normalized outputs from the original Windows engine for 34 character fixtures. Capture passed. The detailed determinism log shows 9 tests passed and 2 rendering tests failed; an allowed failure kept the workflow green. All 443 retained rendered files are byte-identical to their references, exposing a leading UTF-8 marker bug in the comparison read. The harness is corrected locally; a fresh Windows rerun remains pending. [Provenance and the original artifact digest](../docs/native/parity-baseline.json) identify the workflow, engine tag, harness commit and artifact. The bundled archive has its own checksum because it repackages the extracted files.

From the repository root:

```sh
sha256sum -c Chummer.Parity/windows-baseline-42553325872.tar.gz.sha256
mkdir -p /tmp/chummer-windows-reference
tar -xzf Chummer.Parity/windows-baseline-42553325872.tar.gz -C /tmp/chummer-windows-reference
dotnet run --project tools/native/ParityVerify -c Release -- --golden /tmp/chummer-windows-reference --output /tmp/chummer-linux-check --mode saves
```

On NixOS, prefix `dotnet` with `nix shell nixpkgs#dotnet-sdk_8 -c` when an SDK is not installed globally.

The incremental verifier does not run the long `ParityGoldenTests` suite. It compares only the requested workflows and prints progress for each fixture/language. It never writes into the reference directory or the original fixtures. Every actual output is retained under the chosen output directory, with a JSON report of mismatches and unchecked reference files.

- `--mode saves`: synchronous and asynchronous saves.
- `--mode exports --languages en-us`: English print XML, JSON, Squad Manager and the default sheet.
- `--mode exports`: print XML in all six reference languages, plus English exports/default sheet.
- `--mode sheets`: the sheets available for each fixture in the reference capture.
- `--mode all --multi`: all of the above and multi-character print XML.
- `--characters 'Bastion|Soma (Career)|Wesson'`: limit to named fixtures.
- `--after Munin_Career`: resume with fixtures later in ordinal filename order.
- `--compare-existing`: recheck retained `Actual` outputs without loading characters again; useful after several bounded batches.

A batch report covers that batch. A report after `--compare-existing` covers the retained outputs in that directory. Check `distinctOutputs` and `uncheckedReferenceOutputs` before claiming complete coverage.

Saved-character comparison allows two explicitly tested representation differences: root attribute elements can be reordered by attribute name/category, and the numeric scale of `totalcarriedweight` is canonicalized (`0` versus `0.0`). It does not drop attributes, costs, notes, GUID references, arbitrary number strings or other gameplay-list order. Print XML and JSON also compare keyed attributes independently of their order; JSON portrait byte encodings are excluded, matching the shared XML normalizer's image limitation. These equivalences are reported separately and raw actual files remain available. Sheets and Squad Manager outputs require exact agreement after the shared normalizer, including preserved UTF-8 marker characters. Passing a partial batch does not establish full engine or UI parity.
