# Third-Party Notices

PixShrink uses the following third-party software.

The licenses below apply to the respective third-party components and do not replace or modify their original license terms.

## C# Application

### Avalonia 12.1.3

**License:** MIT License

**Project:** https://github.com/AvaloniaUI/Avalonia

PixShrink uses Avalonia for its cross-platform desktop user interface.

The following PixShrink dependencies are part of the Avalonia framework:

* `Avalonia` 12.1.3
* `Avalonia.Desktop` 12.1.3
* `Avalonia.Themes.Fluent` 12.1.3
* `Avalonia.Fonts.Inter` 12.1.3

Avalonia is licensed under the MIT License. The Avalonia project also includes notices and license information for third-party software incorporated into the framework. See the Avalonia project's `NOTICE.md` for details.

### CommunityToolkit.Mvvm 8.4.2

**License:** MIT License

**Project:** https://github.com/CommunityToolkit/dotnet

PixShrink uses `CommunityToolkit.Mvvm` for MVVM-related functionality.

## Python Compressor

### Pillow 12.3.0

**License:** MIT-CMU License

**Project:** https://github.com/python-pillow/Pillow

PixShrink Compressor uses Pillow for PNG image loading and processing.

Pillow is licensed under the MIT-CMU License and includes code originating from the Python Imaging Library (PIL). The Pillow distribution contains the applicable copyright and license notices.

### imagequant 1.1.5

**License:** BSD 3-Clause License (Python bindings)

**Project:** https://github.com/wanadev/imagequant-python

PixShrink Compressor uses `imagequant` 1.1.5 for PNG color quantization.

The `imagequant` Python bindings are licensed under the BSD 3-Clause License and use the `libimagequant` library described below.

#### libimagequant

**License:** GNU General Public License v3 or later for Free/Libre Open Source Software

**Project:** https://github.com/ImageOptim/libimagequant

`libimagequant` is the underlying color quantization library used by `imagequant`.

For Free/Libre Open Source Software, `libimagequant` is available under the GNU GPL version 3 or later. For closed-source and other non-GPL uses, a separate commercial license is available from the `libimagequant` project.

### pyoxipng 9.1.1

**License:** MIT License

**Project:** https://github.com/nfrasser/pyoxipng

PixShrink Compressor optionally uses `pyoxipng` for an additional lossless PNG optimization pass.

`pyoxipng` is a Python wrapper around the `oxipng` PNG optimizer and is distributed under the MIT License.
