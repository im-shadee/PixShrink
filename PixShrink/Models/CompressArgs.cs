using System;
using System.Collections.Generic;
using PixShrink.ViewModels;

namespace PixShrink.Models;

public record CompressArgs(
    IReadOnlyList<string> Inputs,
    string OutputDir,
    string Suffix,
    eColorMode ColorMode,
    int Colors,
    int Deflate,
    double Dither,
    int Posterize,
    IProgress<BatchProgress>? Progress = null
);
    