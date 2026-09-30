# Compact review generator

Run from the workspace root after the terrain, attachments, dressing, navigation, audits, passive Play observation, and final captures have been saved:

```powershell
python Tools/WorldCompact/build_review.py --self-test
python Tools/WorldCompact/build_review.py --check
python Tools/WorldCompact/build_review.py
```

The final command atomically replaces `Art/World/WorldMacro/Compact/REVIEW.html` and `REPORT.md`. It does not run Unity, change a scene or asset, or approve the result. Until that command runs, the existing review files remain untouched. `--check` and `--self-test` never write review files. The default command refuses incomplete or mismatched 88-chunk terrain progress; `--allow-incomplete` explicitly permits an unfinished draft.

The generated pages are UTF-8 static local documents with relative file links and no JavaScript, fetch, remote fonts, or external images. Keep the output folder and workspace together so the source scene, compact scene, asset, and receipt links remain usable under `file:///`.

Freshness is conservative. Each allowed PNG must have a matching capture JSON, image SHA-256, compact scene, source-preserved flag, and UTC newer than every saved geometry/visual dependency considered by the generator. When a capture sidecar supplies `gradeHash`, that must also match. A later scene, dressing, material, attachment, or terrain write can therefore require recapture. Capture after the final writes. The aerial `TerrainDraft_overview` remains an explicitly wash-disabled diagnostic, even when fresh; it does not certify normal scene colors. Missing or stale mine, inn, capital, and overview captures become placeholders.

Navigation output files and original NavData hashes, current grade/relief/fit hashes, newest road progress or report, independent fine-grid dressing, and saved-scene Play receipts are labelled separately. An old report can still show its recorded counts, with an explicit stale marker. Road failures, execution errors, missing ribbon support, unverified native input, and pending visual approval remain visible. The 36-route 3D length sum includes overlapping routes and is not a travel-time measurement. Relief edge statistics are candidate calculations rather than final collider or driving certification.

Optional older source-world overview:

```powershell
python Tools/WorldCompact/build_review.py --source-overview "C:/absolute/path/to/source-overview.png"
```

It receives an explicit older, different-camera caption. Do not use this option for a compact capture lacking a current receipt.
