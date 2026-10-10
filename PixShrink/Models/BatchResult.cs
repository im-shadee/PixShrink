using System;
using System.Collections.Generic;
using System.Linq;

namespace PixShrink.Models;

/// <summary>
/// Outcome of a whole batch. <see cref="FatalError"/> is set when the batch could not run at all
/// (bad input, compressor missing, ...); otherwise <see cref="Files"/> holds one entry per input.
/// </summary>
public record BatchResult(IReadOnlyList<FileResult> Files, string? FatalError, string? LogPath)
{
    public int Succeeded => Files.Count(f => f.Success);
    public int Failed => Files.Count - Succeeded;
 
    public static BatchResult Fatal(string message, string? logPath = null)
        => new(Array.Empty<FileResult>(), message, logPath);
}
