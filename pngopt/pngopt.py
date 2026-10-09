# PixShrink PNG Compressor
# Copyright (C) 2026 Shade
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program. If not, see <https://www.gnu.org/licenses/>.

"""
pngopt.py - PNG optimizer with GIMP-style color modes.

Single file:
    python pngopt.py --input in.png --output out.png --mode indexed --colors 12
                     [--dither 1.0] [--posterize 4] [--deflate 9]

Batch (many files, one process start):
    python pngopt.py --input-list files.txt --output-dir out/ [--suffix _compressed]
                     --mode indexed --colors 12 ...
    files.txt is UTF-8, one input path per line (avoids command-line length limits).
    Each output is <output-dir>/<input name><suffix>.png

Modes:
    rgb        keep full color (no palette); lossless recompression only
    grayscale  convert to grayscale (alpha is kept if the image has transparency)
    indexed    quantize to a palette of at most --colors entries (uses --dither)

--posterize N reduces each color channel to N levels (2-256) BEFORE the mode is applied.
Alpha is never posterized.

Output (stdout):
  Single file: exactly one JSON line
    {"success": true, "originalBytes": ..., "optimizedBytes": ..., "reductionPercent": ...,
     "mode": "indexed", "quantizer": "imagequant", "oxipng": "ok", "bitDepth": 4, "posterize": null}
    {"success": false, "error": "message"}

  Batch: one JSON line per file, as each finishes (flushed immediately), then a summary line
    {"success": true, "index": 1, "total": 3, "input": "...", "output": "...", ...same fields as above}
    {"success": false, "index": 2, "total": 3, "input": "...", "error": "message"}
    {"done": true, "total": 3, "succeeded": 2, "failed": 1, "originalBytes": ..., "optimizedBytes": ...}
  A batch-level failure (e.g. unreadable list) is a single {"success": false, "error": ...} line.

Exit code: 0 if everything succeeded, 1 otherwise. Warnings go to stderr.
"""
import argparse
import json
import os
import sys
from pathlib import Path

from PIL import Image
import imagequant

try:
    import oxipng  # optional extra lossless pass
except ImportError:
    oxipng = None


VALID_MODES = ("rgb", "grayscale", "indexed")


def log(msg: str) -> None:
    print(msg, file=sys.stderr)


def emit(obj: dict) -> None:
    """Print one JSON line and flush, so a reader on the other end of the pipe sees it immediately."""
    print(json.dumps(obj), flush=True)


def bits_for(n_colors: int) -> int:
    """Smallest PNG palette bit depth that can index n_colors entries."""
    if n_colors <= 2:
        return 1
    if n_colors <= 4:
        return 2
    if n_colors <= 16:
        return 4
    return 8


def posterize(im: Image.Image, levels: int) -> Image.Image:
    """Snap every color channel to `levels` evenly spaced values. Alpha is left untouched."""
    last = levels - 1
    lut = [int(int(v / 255 * last + 0.5) * 255 / last + 0.5) for v in range(256)]
    identity = list(range(256))

    tables = {
        "RGB": lut * 3,
        "RGBA": lut * 3 + identity,   # R, G, B posterized; A unchanged
        "L": lut,
        "LA": lut + identity,         # L posterized; A unchanged
    }
    return im.point(tables[im.mode])


def trim_palette(im: Image.Image, opaque: bool) -> Image.Image:
    """Remap a P image so its palette only holds colors actually used."""
    used = sorted(idx for _, idx in im.getcolors(256))
    pal = im.getpalette("RGBA")

    lut = [0] * 256
    for new, old in enumerate(used):
        lut[old] = new
    out = im.point(lut)

    new_pal = []
    for old in used:
        r, g, b, a = pal[old * 4: old * 4 + 4]
        new_pal += [r, g, b] if opaque else [r, g, b, a]
    out.putpalette(new_pal, rawmode="RGB" if opaque else "RGBA")

    if opaque:
        out.info.pop("transparency", None)  # avoid a useless tRNS chunk
    return out


def index_image(rgba: Image.Image, dither: float, posterize_levels: int, max_colors: int, has_alpha: bool):
    if posterize_levels:
        rgba = posterize(rgba, posterize_levels)

    quantizer = "imagequant"
    try:
        quantized = imagequant.quantize_pil_image(
            rgba, dithering_level=dither, max_colors=max_colors
        )
    except Exception as e:
        quantizer = "pillow-fallback"
        log(f"imagequant failed ({e!r}), falling back to Pillow")

        if has_alpha:
            quantized = rgba.quantize(colors=max_colors, method=Image.Quantize.FASTOCTREE)
        else:
            quantized = rgba.convert("RGB").quantize(colors=max_colors, method=Image.Quantize.MEDIANCUT)

    if quantized.mode != "P":
        quantized = quantized.convert("RGBA").quantize(colors=max_colors, method=Image.Quantize.FASTOCTREE)

    result = trim_palette(quantized, opaque=not has_alpha)

    # Pick the bit depth ourselves instead of trusting Pillow's auto-detection
    bit_depth = bits_for(len(result.getcolors(256)))

    # Result, bitdepth, quantizer used
    return result, bit_depth, quantizer


def optimize_png(input_path: str, output_path: str, mode: str = "indexed",
                 max_colors: int = 256, deflate_level: int = 9,
                 dither: float = 1.0, posterize_levels: int = 0) -> dict:
    # Mode validation
    if mode not in VALID_MODES:
        raise ValueError(f"Mode must be one of: {', '.join(VALID_MODES)}.")
    if mode == "indexed" and not (1 <= max_colors <= 256):
        raise ValueError("Color count must be between 1 and 256.")

    # Deflate/dither validation
    if not (1 <= deflate_level <= 9):
        raise ValueError("DEFLATE level must be between 1 and 9.")

    if not (0.0 <= dither <= 1.0):
        raise ValueError("Dithering must be between 0 and 1.")

    # Posterize validation
    if posterize_levels and not (2 <= posterize_levels <= 256):
        raise ValueError("Posterize levels must be between 2 and 256.")
    if not os.path.exists(input_path):
        raise FileNotFoundError(f"Input file not found ({input_path})")

    # Get the output directory or create it if it doesn't exist yet
    output_dir = os.path.dirname(output_path)
    if output_dir:
        os.makedirs(output_dir, exist_ok=True)

    # Initialize default values for non-indexed modes
    bit_depth = None
    quantizer = None

    with Image.open(input_path) as img:
        rgba = img.convert("RGBA")
        has_alpha = rgba.getchannel("A").getextrema()[0] < 255
        result = None

        match mode:
            case "grayscale":
                gray = rgba.convert("L")
                result = Image.merge("LA", (gray, rgba.getchannel("A"))) if has_alpha else gray

                if posterize_levels:
                    result = posterize(result, posterize_levels)

            case "rgb":
                result = rgba if has_alpha else rgba.convert("RGB")

                if posterize_levels:
                    result = posterize(result, posterize_levels)

            case "indexed":
                result, bit_depth, quantizer = index_image(
                    rgba, dither, posterize_levels, max_colors, has_alpha)

        save_args = dict(format="PNG", optimize=True, compress_level=deflate_level)

        if bit_depth is not None:
            save_args["bits"] = bit_depth
        result.save(output_path, **save_args)

    # Lossless pass. Pillow doesn't filter palette images and won't repack grayscale bit depths,
    # so this is where that happens. Report what happened instead of hiding it.
    oxipng_status = "not installed"
    if oxipng is not None:
        try:
            oxipng.optimize(output_path, level=6)
            oxipng_status = "ok"
        except Exception as e:
            oxipng_status = f"failed: {e!r}"
            log(f"oxipng pass failed ({e!r})")
    else:
        log("pyoxipng is not available; skipping the lossless pass")

    original = os.path.getsize(input_path)
    optimized = os.path.getsize(output_path)
    reduction = (original - optimized) / original * 100 if original else 0.0
    return {
        "success": True,
        "originalBytes": original,
        "optimizedBytes": optimized,
        "reductionPercent": round(reduction, 2),
        "mode": mode,
        "quantizer": quantizer,
        "oxipng": oxipng_status,
        "bitDepth": bit_depth,
        "posterize": posterize_levels or None,
    }


def read_input_list(list_path: str) -> list[str]:
    """One path per line, UTF-8 (a BOM is tolerated). Blank lines are skipped."""
    with open(list_path, encoding="utf-8-sig") as f:
        return [line.rstrip("\r\n") for line in f if line.strip()]


def unique_output_path(output_dir: str, stem: str, suffix: str, used: set) -> str:
    """<output_dir>/<stem><suffix>.png, adding _2, _3... if two inputs would collide."""
    candidate = os.path.join(output_dir, f"{stem}{suffix}.png")
    n = 2
    while os.path.normcase(candidate) in used:
        candidate = os.path.join(output_dir, f"{stem}{suffix}_{n}.png")
        n += 1
    used.add(os.path.normcase(candidate))
    return candidate


def run_batch(args) -> int:
    paths = read_input_list(args.input_list)
    if not paths:
        raise ValueError("The input list is empty.")

    total = len(paths)
    used: set = set()
    succeeded = 0
    sum_before = 0
    sum_after = 0

    for index, path in enumerate(paths, start=1):
        try:
            out_path = unique_output_path(args.output_dir, Path(path).stem, args.suffix, used)

            # Never overwrite the file we're reading from
            if os.path.normcase(os.path.abspath(out_path)) == os.path.normcase(os.path.abspath(path)):
                raise ValueError("The output file would overwrite the input file.")

            result = optimize_png(
                path, out_path,
                mode=args.mode,
                max_colors=args.colors,
                deflate_level=args.deflate,
                dither=args.dither,
                posterize_levels=args.posterize,
            )
            result.update({"index": index, "total": total, "input": path, "output": out_path})
            succeeded += 1
            sum_before += result["originalBytes"]
            sum_after += result["optimizedBytes"]
        except Exception as e:
            # One bad file must not stop the rest of the batch
            result = {"success": False, "index": index, "total": total, "input": path, "error": str(e)}

        emit(result)

    emit({
        "done": True,
        "total": total,
        "succeeded": succeeded,
        "failed": total - succeeded,
        "originalBytes": sum_before,
        "optimizedBytes": sum_after,
    })
    return 0 if succeeded == total else 1


def main() -> int:
    parser = argparse.ArgumentParser(description="Optimize PNG files.")
    # Single-file mode
    parser.add_argument("--input", "-i")
    parser.add_argument("--output", "-o")
    # Batch mode
    parser.add_argument("--input-list",
                        help="UTF-8 text file with one input path per line (batch mode)")
    parser.add_argument("--output-dir", help="Output folder (batch mode)")
    parser.add_argument("--suffix", default="_compressed",
                        help="Added to each input name in batch mode (before .png)")
    # Shared options
    parser.add_argument("--mode", "-m", choices=VALID_MODES, default="indexed")
    parser.add_argument("--colors", "-c", type=int, default=256,
                        help="Palette size 1-256 (indexed mode only)")
    parser.add_argument("--deflate", "-d", type=int, default=9)
    parser.add_argument("--posterize", type=int, default=0,
                        help="Reduce each color channel to N levels (2-256); 0 = off")
    parser.add_argument("--dither", type=float, default=1.0,
                        help="Dithering level 0.0-1.0 (indexed mode only). Lower often compresses better.")

    try:
        args = parser.parse_args()
    except SystemExit:
        # argparse already wrote usage to stderr; still give the GUI a JSON line
        emit({"success": False, "error": "Invalid command-line arguments."})
        return 1

    try:
        if args.input_list is not None:
            if not args.output_dir:
                raise ValueError("--output-dir is required with --input-list.")
            return run_batch(args)

        if not args.input or not args.output:
            raise ValueError("--input and --output are required (or use --input-list with --output-dir).")

        emit(optimize_png(
            args.input,
            args.output,
            mode=args.mode,
            max_colors=args.colors,
            deflate_level=args.deflate,
            dither=args.dither,
            posterize_levels=args.posterize,
        ))
        return 0
    except Exception as e:
        emit({"success": False, "error": str(e)})
        return 1


if __name__ == "__main__":
    sys.exit(main())
    