# PixShrink

> PixShrink is a desktop PNG optimization tool designed to reduce image file sizes while preserving visual quality.

Licensing: The desktop application is MIT-licensed; the pngopt compression engine is GPLv3-or-later. See [Licensing](#-Licensing) for details.

[How to use](#-usage) • [Optimization details](#optimization-options)

It combines a desktop interface made in C# using the Avalonia framework with a standalone Python compression script, communicating through a JSON-based process interface.

## 🧩 Features
- PNG optimization through color quantization (imagequant) and lossless optimization (oxipng)
- Header/metadata trimming for optimized file size
- Palette trimming
- 3 color modes, inspired by GIMP (RGB, Grayscale, Indexed)
- Posterization
- Alpha channel removal
- Success/error reporting with stats

## 🛠️ Architecture
PixShrink separates its UI from its processing engine. The application launches the Python script as a separate process, passing it the arguments configured in the interface.

The Python process communicates its result through JSON written to stdout. The C# application parses these results and displays, accordingly:
- An error message with the exact message thrown by Python

  <img width="548" height="289" alt="error_log" src="https://github.com/user-attachments/assets/364fbf40-d0f7-4939-89f7-f6079ff3167d" />

- A success message with stats

  <img width="539" height="287" alt="success_log" src="https://github.com/user-attachments/assets/a04f4197-125b-4db2-84af-b7577e7a2307" />

The user is free to play with the settings and find what works best for them, all packed into one practical interface.

## 📕 Usage
1. Extract and launch PixShrink
2. Select the PNG images you want to optimize

<img width="635" height="220" alt="file_select" src="https://github.com/user-attachments/assets/0bb10e9a-8511-4bfe-a13a-454bbaa9426e" />

3. Configure the optimization options
4. Click on "Compress". This will start the optimization process.

## Optimization Options
Here is a small presentation of the optimization options of PixShrink

As explained [earlier](#-features), there are 3 color modes you can use:

<img width="191" height="186" alt="color_modes" src="https://github.com/user-attachments/assets/a4b8f35a-d9e0-487e-90eb-9d5998c836df" />

- RGB preserves the original color space used by the image
- Grayscale turns the image into levels of gray instead of colors. This mode is usually used for pngs that are already grayscale, allowing the color channels to be stripped.
- Indexed lets you limit the amount of colors an image can have. This can significantly reduce file size, but may result in a decrease in visual quality. This is particularly useful for textures, where reducing the number of colors can reduce image sizes by several megabytes in game-development projects.

The indexed color mode lets you input the amount of colors you wish to keep.

<img width="627" height="143" alt="Capture d&#39;écran 2026-10-07 211628" src="https://github.com/user-attachments/assets/1b7c8a50-de42-4865-bb5a-0328ea463a6e" />

---
You can also turn on posterization. This setting allows you to limit the range of each color channel, a process similar to indexing, but less destructive for an image as long as posterization levels are kept high enough.

<img width="633" height="111" alt="Capture d&#39;écran 2026-10-07 205014" src="https://github.com/user-attachments/assets/04ab9bd5-8020-4943-8dd2-d2397b873766" />

The tradeoff for posterization is much less interesting than indexing, but can still cut a few dozen kilobytes of size, making it interesting for small game UI or similar elements.

> ⚠️ Note that using dithering with posterization might add excessive noise, resulting in a worse looking image at best, and potential size increases at worst, which is why the app warns the user if the setting is kept too high.

<img width="642" height="256" alt="Capture d&#39;écran 2026-10-07 211612" src="https://github.com/user-attachments/assets/ef52db0b-3c44-4423-92be-73c345b02f5b" />


---
You are also able to set the level of DEFLATE compression (higher values mean increased computing time but smaller file size). DEFLATE is a lossless algorithm combining LZ77 Algorithm and Huffman Coding allowing for heavy image compression without quality loss.

PixShrink lets you control the compression level, trading processing time for potentially smaller files without affecting image quality.

<img width="631" height="57" alt="image" src="https://github.com/user-attachments/assets/573ff83e-e7eb-4ec6-9fde-fe3ec2d35da5" />

## 📄 Licensing
The PixShrink desktop application is licensed under the MIT License. See [LICENSE](./LICENSE) for more details.

The pngopt PNG optimization engine is licensed under the GNU General Public License v3 or later (see [LICENSE](./pngopt/LICENSE) for the full text). It uses libimagequant, which is available under the GPLv3 or later for free/libre open-source software.

Third-party dependencies retain their respective licenses. See [THIRD-PARTY-NOTICES](./THIRD-PARTY-NOTICES.md).
