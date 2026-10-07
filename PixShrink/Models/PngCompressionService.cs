using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PixShrink.ViewModels;

namespace PixShrink.Models;

public record CompressionResult(
    bool Success, 
    double KbBefore, 
    double KbAfter, 
    double ReductionPercent, 
    string? OxipngNote, 
    string? ErrorMessage
);

public class PngCompressionService
{
    private static readonly string s_ExePath = Path.Combine(
        AppContext.BaseDirectory, 
        "Process", 
        OperatingSystem.IsWindows() ? "pngopt.exe" : "pngopt"
    );
    
    public async Task<CompressionResult> CompressAsync(
        string inputPath, 
        string outputPath,
        eColorMode mode,
        int colors, 
        int deflate, 
        double dither,
        int posterize)
    {
        if (!File.Exists(s_ExePath))
        {
            return new CompressionResult(false, 0, 0, 0, null, 
                $"Compressor executable not found at:\n{s_ExePath}");
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(s_ExePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* Fallback to Process.Start error if read-only */ }
        }

        ProcessStartInfo startInfo = new(s_ExePath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode.ToString());
        startInfo.ArgumentList.Add("--colors");
        startInfo.ArgumentList.Add(colors.ToString());
        startInfo.ArgumentList.Add("--deflate");
        startInfo.ArgumentList.Add(deflate.ToString());
        startInfo.ArgumentList.Add("--posterize");
        startInfo.ArgumentList.Add(posterize.ToString());
        startInfo.ArgumentList.Add("--dither");
        startInfo.ArgumentList.Add(dither.ToString("0.0", CultureInfo.InvariantCulture));

        try
        {
            using Process? proc = Process.Start(startInfo);
            if (proc == null)
            {
                return new CompressionResult(
                    Success: false, 
                    KbBefore: 0, 
                    KbAfter: 0, 
                    ReductionPercent: 0, 
                    null, 
                    "Failed to start compressor process."
                    );
            }

            string line = await proc.StandardOutput.ReadLineAsync() ?? string.Empty;
            await proc.WaitForExitAsync();

            using JsonDocument doc = JsonDocument.Parse(line);
            bool ok = doc.RootElement.GetProperty("success").GetBoolean();

            if (!ok)
            {
                string? err = doc.RootElement.GetProperty("error").GetString();
                return new CompressionResult(
                    Success: false, 
                    KbBefore: 0, 
                    KbAfter: 0, 
                    ReductionPercent: 0, 
                    null, 
                    err ?? "Unknown compression error."
                    );
            }

            double kbBefore = doc.RootElement.GetProperty("originalBytes").GetInt64() / 1024.0;
            double kbAfter = doc.RootElement.GetProperty("optimizedBytes").GetInt64() / 1024.0;
            double percent = doc.RootElement.GetProperty("reductionPercent").GetDouble();

            string? oxipngStatus = doc.RootElement.TryGetProperty("oxipng", out JsonElement ox) 
                ? ox.GetString() 
                : null;

            string note = oxipngStatus is null or "ok"
                ? string.Empty
                : $"Note: lossless pass skipped ({oxipngStatus}).";

            return new CompressionResult(
                Success: true, 
                kbBefore, 
                kbAfter, 
                percent, 
                note, 
                null
                );
        }
        catch (JsonException)
        {
            return new CompressionResult(
                Success: false, 
                KbBefore: 0, 
                KbAfter: 0, 
                ReductionPercent: 0, 
                null, 
                "Compressor returned unreadable output."
                );
        }
        catch (Exception ex)
        {
            return new CompressionResult(
                Success: false, 
                KbBefore: 0, 
                KbAfter: 0, 
                ReductionPercent: 0, 
                null, 
                ex.Message);
        }
    }
}
