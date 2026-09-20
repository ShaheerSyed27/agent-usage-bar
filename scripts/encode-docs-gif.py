"""Encode offscreen demo frames with one palette, without GIF color speckling.

Documentation-only helper. Requires Pillow. Never imported by the Windows app.
"""
import argparse
from pathlib import Path

from PIL import Image, ImageChops


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("frames", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    # Exact, fixed frame range avoids including unrelated or stale files.
    frames = []
    for index in range(80):
        with Image.open(args.frames / f"frame-{index:03}.png") as image:
            frames.append(image.convert("RGB"))
    try:
        width, height = frames[0].size
        # Learn both states and several ember positions. Most pixels stay still,
        # so a shared adaptive palette keeps every background solid and clean.
        samples = [frames[index] for index in (0, 10, 20, 30, 40, 50, 60, 70)]
        atlas = Image.new("RGB", (width, height * len(samples)))
        for index, sample in enumerate(samples):
            atlas.paste(sample, (0, index * height))
        palette = atlas.quantize(colors=256, method=Image.Quantize.MEDIANCUT)
        encoded = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
        try:
            encoded[0].save(args.output, save_all=True, append_images=encoded[1:],
                            duration=100, loop=0, optimize=True, disposal=1)
        finally:
            for frame in encoded:
                frame.close()
            palette.close()
            atlas.close()

        with Image.open(args.output) as check:
            assert check.size == (1680, 640), check.size
            assert check.n_frames == 80, check.n_frames
            assert check.info.get("loop") == 0
            duration = 0
            snapshots = []
            for index in range(check.n_frames):
                check.seek(index)
                duration += check.info["duration"]
                if index in (0, 10, 40, 50):
                    snapshots.append(check.convert("RGB"))
            assert duration == 8000, duration
            assert ImageChops.difference(snapshots[0], snapshots[1]).getbbox(), "Amber ember did not move"
            assert ImageChops.difference(snapshots[2], snapshots[3]).getbbox(), "Red ember did not move"
            for snapshot in snapshots:
                snapshot.close()
        print(f"Verified: {args.output.name}, 1680 x 640, 80 frames, 8 seconds, loops forever")
    finally:
        for frame in frames:
            frame.close()


if __name__ == "__main__":
    main()
