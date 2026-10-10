using System.IO;

namespace PixShrink.Models;

/// <summary>Outcome for a single input file.</summary>
public record FileResult(
    string Input,
    bool Success,
    double KbBefore,
    double KbAfter,
    double ReductionPercent,
    string? OxipngNote,
    string? Error)
{
    public string Name => Path.GetFileName(Input);
}
