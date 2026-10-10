using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PixShrink.ViewModels;

namespace PixShrink.Models;

public class PngCompressionService
{
    private static readonly string s_ExePath = Path.Combine(
        AppContext.BaseDirectory, 
        "Process", 
        OperatingSystem.IsWindows() ? "pngopt.exe" : "pngopt"
    );
    
    // Logs/ sits next to Process/ and Settings/
    private static readonly string s_LogDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
    
    /// <summary>
    /// Compresses every file in <paramref name="inputs"/> with a single compressor process.
    /// Output files are written to <paramref name="outputDir"/> as "name + suffix + .png".
    /// </summary>
    /// <remarks>A failure on one file never stops the others. Never throws.</remarks>
    public async Task<BatchResult> CompressBatchAsync(CompressArgs args)
    {
        StringBuilder log = new();
        List<FileResult> results = new();
        string? fatal;
 
        // Input paths go through a UTF-8 text file: no command-line length limit, no quoting problems
        string listPath = Path.Combine(Path.GetTempPath(), $"pixshrink_{Guid.NewGuid():N}.txt");
 
        // Create a log file with every operation and results, so the user can send the log file should a problem happen
        log.Append(CreateLogHeader(args));
 
        try
        {
            fatal = await RunCompressorAsync(args, listPath, results, log);
        }
        catch (Exception ex)
        {
            fatal = ex.Message;
            log.AppendLine($"EXCEPTION: {ex}");
        }
        finally
        {
            TryDeleteFile(listPath);
        }
 
        // Anything the compressor never reported (for example because it crashed) counts as failed
        if (fatal == null)
        {
            HashSet<string> reported = new(results.Select(r => r.Input), StringComparer.Ordinal);
            foreach (string input in args.Inputs)
            {
                if (reported.Contains(input)) continue;
 
                results.Add(new FileResult(input, false, 0, 0, 0, null,
                    "No result was received from the compressor (it may have crashed)."));
                log.AppendLine($"MISSING RESULT: {input}");
            }
        }
 
        string? logPath = TryWriteLog(log.ToString());
        return new BatchResult(results, fatal, logPath);
    }
    
    /// <summary>
    /// Runs the process and fills <paramref name="results"/>.
    /// </summary>
    /// <returns>A batch-level error if any operation failed, or null if success.</returns>
    private static async Task<string?> RunCompressorAsync(CompressArgs args, string listPath, List<FileResult> results, StringBuilder log)
    {
        if (!File.Exists(s_ExePath)) return $"Compressor executable not found at:\n{s_ExePath}";
 
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(s_ExePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* Fall through; Process.Start will report a real problem */ }
        }
 
        await File.WriteAllLinesAsync(listPath, args.Inputs, new UTF8Encoding(false));

        ProcessStartInfo startInfo = CreatePythonProcess(listPath, args);
 
        log.AppendLine();
        log.AppendLine($"Command: {s_ExePath} {string.Join(' ', startInfo.ArgumentList)}");
 
        using Process? proc = Process.Start(startInfo);
        if (proc == null) return "Failed to start compressor process.";
 
        // Drain stderr on its own task so a full pipe can never block the compressor
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
 
        // Read from stdout, fill results with parsed JSON from the compressor, and logs every step into log file
        log.AppendLine("--- stdout ---");
        string? fatal = await ParseProcessOutput(proc, log, results, args);
        await proc.WaitForExitAsync();
        log.AppendLine($"--- exit code: {proc.ExitCode} ---");
 
        string stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            log.AppendLine("--- stderr ---");
            log.AppendLine(stderr.TrimEnd());
        }
 
        return fatal;
    }

    private static async Task<string?> ParseProcessOutput(Process proc, StringBuilder log, List<FileResult> results, CompressArgs args)
    {
        string? fatal = null;
        string? line;
        
        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
        {
            log.AppendLine(line); // raw output, exactly as received
            if (string.IsNullOrWhiteSpace(line)) continue;
 
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
 
                // Final summary line; the per-file lines already carry everything we need
                if (root.TryGetProperty("done", out _)) continue;
 
                bool ok = root.GetProperty("success").GetBoolean();
 
                // A failure line without "index" is a batch-level failure (unreadable list, bad arguments...)
                if (!root.TryGetProperty("index", out _))
                {
                    if (!ok) fatal = root.GetProperty("error").GetString() ?? "Unknown compression error.";
                    continue;
                }
 
                string input = root.GetProperty("input").GetString() ?? string.Empty;
 
                if (ok)
                {
                    string? oxipng = root.TryGetProperty("oxipng", out JsonElement ox) ? ox.GetString() : null;
 
                    results.Add(new FileResult(
                        input,
                        Success: true,
                        KbBefore: root.GetProperty("originalBytes").GetInt64() / 1024.0,
                        KbAfter: root.GetProperty("optimizedBytes").GetInt64() / 1024.0,
                        ReductionPercent: root.GetProperty("reductionPercent").GetDouble(),
                        OxipngNote: oxipng is null or "ok" ? null : oxipng,
                        Error: null));
                }
                else
                {
                    results.Add(new FileResult(input, false, 0, 0, 0, null,
                        root.GetProperty("error").GetString() ?? "Unknown error."));
                }
 
                args.Progress?.Report(new BatchProgress(results.Count, args.Inputs.Count));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // Not a line we understand; it is already in the log
                log.AppendLine($"(unparseable line ignored: {ex.GetType().Name})");
            }
        }
        
        return fatal;
    }

    private static ProcessStartInfo CreatePythonProcess(string listPath, CompressArgs args)
    {
        ProcessStartInfo startInfo = new(s_ExePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
 
        startInfo.ArgumentList.Add("--input-list");
        startInfo.ArgumentList.Add(listPath);
        startInfo.ArgumentList.Add("--output-dir");
        startInfo.ArgumentList.Add(args.OutputDir);
        // "--suffix=value" form: a suffix like "-small" would otherwise be mistaken for an option
        startInfo.ArgumentList.Add($"--suffix={args.Suffix}");
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(ColorToArg(args.ColorMode));
        startInfo.ArgumentList.Add("--colors");
        startInfo.ArgumentList.Add(args.Colors.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--deflate");
        startInfo.ArgumentList.Add(args.Deflate.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--posterize");
        startInfo.ArgumentList.Add(args.Posterize.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--dither");
        startInfo.ArgumentList.Add(args.Dither.ToString("0.0", CultureInfo.InvariantCulture));
        
        return startInfo;
    }
    
    /// <summary>
    /// Converts <see cref="eColorMode"/> values into valid argument strings expected by the Python compressor.
    /// </summary>
    /// <param name="colorMode">The <see cref="eColorMode"/> value to turn to a string.</param>
    /// <returns>A string formatted as the argument for the 'colorMode' parameter expected by the Python compressor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="colorMode"/> has no associated value.</exception>
    private static string ColorToArg(eColorMode colorMode)
    {
        return colorMode switch
        {
            eColorMode.Rgb => "rgb",
            eColorMode.Grayscale => "grayscale",
            eColorMode.Indexed => "indexed",
            _ => throw new ArgumentOutOfRangeException
                (nameof(colorMode), colorMode, $"ColorMode value passed in ColorToArg has no implementation.")
        };
    }
    
    /// <summary>
    /// Writes Logs/log_MM-dd-yyyy_HH_mm_ss.log. A logging problem must never fail a compression.
    /// </summary>
    private static string? TryWriteLog(string content)
    {
        try
        {
            Directory.CreateDirectory(s_LogDirectory);
 
            string stamp = DateTime.Now.ToString("MM-dd-yyyy_HH_mm_ss", CultureInfo.InvariantCulture);
            string path = Path.Combine(s_LogDirectory, $"log_{stamp}.log");
 
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not write log: {ex.Message}");
            return null;
        }
    }
 
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* a leftover temp file is harmless */ }
    }

    /// <summary>
    /// Creates a text summary of the current batch run without revealing private folder paths.
    /// </summary>
    /// <param name="args">The settings and input files for this run.</param>
    /// <param name="timestamp">An optional custom date and time for testing.</param>
    /// <param name="osDescription">An optional operating system name for testing.</param>
    /// <returns>A formatted block of text containing the safe run details.</returns>
    internal static string CreateLogHeader(CompressArgs args, DateTime? timestamp = null, string? osDescription = null)
    {
        DateTime time = timestamp ?? DateTime.Now;
        string os = osDescription ?? RuntimeInformation.OSDescription;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"PixShrink batch - {time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"OS: {os}");
        sb.AppendLine($"Files: {args.Inputs.Count}, mode: {args.ColorMode}, colors: {args.Colors}, deflate: {args.Deflate}, " +
                      $"dither: {args.Dither.ToString("0.0", CultureInfo.InvariantCulture)}, posterize: {args.Posterize}");
        sb.AppendLine($"Output folder: {SanitizePath(args.OutputDir)}");
        sb.AppendLine($"Suffix: \"{args.Suffix}\"");

        foreach (string input in args.Inputs)
        {
            sb.AppendLine($"  input: {SanitizePath(input)}");
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// Replaces the user's home directory in a file path with '~' to protect their privacy.
    /// </summary>
    /// <param name="path">The file or folder path to clean up.</param>
    /// <returns>The path with the home folder hidden, or the original path if it is outside the home folder.</returns>
    private static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[userProfile.Length..];
        }

        return path;
    }
}
