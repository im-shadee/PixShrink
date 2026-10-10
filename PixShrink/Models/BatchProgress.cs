namespace PixShrink.Models;

/// <summary>Reported after each file finishes.</summary>
public record BatchProgress(int Done, int Total);
