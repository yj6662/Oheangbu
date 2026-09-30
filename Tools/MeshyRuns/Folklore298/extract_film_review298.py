"""Extract one actual decoded MP4 frame per role for direct visual inspection.

Does not declare visual approval. All videos must already have successful encoder
receipts. Death uses the last encoded frame; other roles use their midpoint.
"""
import json
from pathlib import Path

import encode_films298 as E


def main():
    ffmpeg = E.ffmpeg_path(None)
    rows = []
    manifest = json.loads((E.BASE / "import-manifest.json").read_text(encoding="utf-8-sig"))
    for model in manifest["rows"]:
        folder = E.BASE / "Captures" / model["id"]
        movie_receipt = json.loads((folder / "video-receipt.json").read_text(encoding="utf-8-sig"))
        capture_receipt = json.loads((folder / "film-receipt.json").read_text(encoding="utf-8-sig"))
        if E.sha(folder / "film-receipt.json") != movie_receipt["receiptSha256"]:
            raise ValueError("Latest movie and capture receipts differ: " + model["id"])
        mp4 = next(o for o in movie_receipt["outputs"] if o["format"] == "mp4")
        video = folder / "animation.mp4"
        if E.sha(video) != mp4["sha256"]:
            raise ValueError("Convenience video differs from validated output")
        fps = mp4["decode"]["fps"]
        count = mp4["decode"]["decoded_frames"]
        indices = [min(count - 1, int((c["timelineStart"] + c["seconds"] * (.999999 if c["role"] == "death" else .5)) * fps)) for c in capture_receipt["clips"]]
        if len(set(indices)) != 6:
            raise ValueError("Six distinct role sample frames expected")
        expression = "+".join("eq(n\\,%d)" % n for n in indices)
        target = (E.ROOT / mp4["path"]).parent / "decoded-six-role-review.png"
        E.run([ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", video,
               "-vf", "select=" + expression + ",tile=3x2:nb_frames=6", "-frames:v", "1", "-threads", "2", target])
        rows.append({"id": model["id"], "sourceMp4": str(video), "sourceSha256": E.sha(video),
                     "decodedFrameIndices": indices, "containerFps": fps,
                     "sheet": str(target), "sheetSha256": E.sha(target), "directlyViewed": False,
                     "scope": "Six decoded MP4 frames: one per role, final Death pose. Extraction alone is not visual approval or continuous video viewing."})
    E.dump(E.BASE / "Analysis/film-decoded-review-frames.json", rows)
    print(json.dumps([{"id": r["id"], "sheet": r["sheet"]} for r in rows], ensure_ascii=False))


if __name__ == "__main__":
    main()
