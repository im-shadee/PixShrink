using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixShrink.Models;
using PixShrink.Views;

namespace PixShrink.ViewModels;

public enum eColorMode { Rgb = 0, Grayscale = 1, Indexed = 2 }

public partial class MainViewModel : ViewModelBase
{
    private static readonly string[] s_WindowsReservedNames =
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" }))
            .ToArray();

    private static readonly string s_InvalidCharsDisplay = string.Join(" ",
        Path.GetInvalidFileNameChars().Where(c => !char.IsControl(c)));
    
    // These file systems are case-insensitive by default
    private static readonly bool s_CaseInsensitivePaths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private static readonly StringComparison s_PathComparison =
        s_CaseInsensitivePaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer s_PathComparer =
        s_CaseInsensitivePaths ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    
    private readonly PngCompressionService _compressionService = new();
    
    [ObservableProperty]
    private eColorMode _selectedColorMode = eColorMode.Rgb;

    [ObservableProperty]
    private string _outputFolderPath = string.Empty;
    
    [ObservableProperty, Description("Appended to every input file name, before '.png' (e.g. '_compressed')")]
    private string _outputSuffix = "_compressed";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDitherWarningVisible))]
    private bool _usePosterization = false;
    
    [ObservableProperty]
    private int _posterizeLevel = 256;
    
    [ObservableProperty]
    private int _colors = 256;

    [ObservableProperty]
    private int _deflateLevel = 9;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDitherWarningVisible))]
    private double _dither = 1.0;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ClearInputFilesCommand))]
    [NotifyPropertyChangedFor(nameof(CanStartCompression)), NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    private bool _isCompressing = false;
    
    /// <summary> The Compress button is enabled only when there is something to compress. </summary>
    public bool CanStartCompression => !IsCompressing && HasInputFiles;
    
    [ObservableProperty, Description("How many rows are selected in the input list (kept in sync by the view).")]
    [NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    private int _selectedInputCount = 0;
    
    public bool CanRemoveSelected => !IsCompressing && SelectedInputCount > 0;

    [ObservableProperty, Description("Progress shown while a batch runs.")]
    private string _statusText = string.Empty;
 
    [ObservableProperty]
    private double _progressValue;
 
    [ObservableProperty]
    private double _progressMax = 1;
    
    /// <summary> The files that will be compressed. Replaced as a whole via <see cref="SetInputFiles"/> .</summary>
    public ObservableCollection<InputFileItem> InputFiles { get; } = new();
    public bool HasInputFiles => InputFiles.Count > 0;
    public string InputFilesLabel => HasInputFiles ? $"Input files ({InputFiles.Count})" : "Input files";
    
    /// <summary>
    /// Returns true only when posterization is active and dithering exceeds the ideal threshold (0.5).
    /// </summary>
    public bool IsDitherWarningVisible => UsePosterization && Dither > 0.5;
    
    public readonly AppSettings Settings = new();
    
    /// <summary>
    /// Initializes the ViewModel component associated with <see cref="MainWindow"/>.
    /// </summary>
    public MainViewModel()
    {
        Settings.ReadFromJson();
        string folderPath = string.Empty;

        try
        {
            folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
        finally
        {
            OutputFolderPath = !string.IsNullOrWhiteSpace(Settings.LastOutputPath)
                ? Settings.LastOutputPath
                : Path.Combine(folderPath, "out");
        
            InputFiles.CollectionChanged += OnInputFilesChanged;
        }
    }
    
    private bool CanClearInputFiles() => !IsCompressing && HasInputFiles;
    [RelayCommand(CanExecute = nameof(CanClearInputFiles))]
    private void ClearInputFiles() => InputFiles.Clear();

    private void OnInputFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasInputFiles));
        OnPropertyChanged(nameof(CanStartCompression));
        OnPropertyChanged(nameof(InputFilesLabel));
        ClearInputFilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Replaces the current selection. Duplicate paths (case-insensitive on Windows/macOS) are skipped.
    /// Asking the user to confirm a replacement is the view's job; this just applies it.
    /// </summary>
    public void SetInputFiles(IEnumerable<string> paths)
    {
        List<InputFileItem> items = ToUniqueItems(paths, new HashSet<string>(s_PathComparer));
        if (items.Count == 0) return;

        InputFiles.Clear();
        foreach (InputFileItem item in items) InputFiles.Add(item);

        RememberInputPath(items[0]);
    }
    
    /// <summary>
    /// Adds to the current selection, skipping files already in the list.
    /// </summary>
    /// <returns>The number of files added, skipping duplicates.</returns>
    public int AddInputFiles(IEnumerable<string> paths)
    {
        HashSet<string> seen = new(InputFiles.Select(f => f.FullPath), s_PathComparer);
        List<InputFileItem> items = ToUniqueItems(paths, seen);

        foreach (InputFileItem item in items) InputFiles.Add(item);
        if (items.Count > 0) RememberInputPath(items[0]);

        return items.Count;
    }
    
    /// <summary>
    /// Remove the input files selected by the user.
    /// </summary>
    /// <param name="items">The selection of input file entries to remove.</param>
    public void RemoveInputFiles(IEnumerable<InputFileItem> items)
    {
        foreach (InputFileItem item in items.ToList()) InputFiles.Remove(item);
    }
    
    /// <summary>
    /// When the output folder is selected, inject the path into the view's <see cref="OutputFolderPath"/> field.
    /// </summary>
    /// <param name="path">The path of the selected folder to inject.</param>
    public void SetOutputFolder(string path)
    {
        OutputFolderPath = path;
        Settings.LastOutputPath = path;
        Settings.WriteToJson();
    }
    
    /// <summary>
    /// Validates the settings, then compresses every input file. Validation problems come back as
    /// <see cref="BatchResult.FatalError"/>; per-file problems are reported per file.
    /// </summary>
    public async Task<BatchResult> CompressAsync()
    {
        if (IsCompressing) return BatchResult.Fatal("A compression is already running.");
 
        if (InputFiles.Count == 0)
        {
            return BatchResult.Fatal("No input files selected.");
        }
 
        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            return BatchResult.Fatal("The output folder must be specified.");
        }
 
        string suffix = OutputSuffix.Trim();
 
        // The suffix ends up inside every file name, so validate it as part of a name
        if (suffix.Length > 0)
        {
            string? nameError = ValidateFileName($"a{suffix}.png");
            if (nameError != null) return BatchResult.Fatal($"Invalid suffix.\n{nameError}");
        }
 
        // If no suffix was chosen, and the user set the output folder as the input folder, it would overwrite the originals.
        // Log an explicit error to the user as overwriting is forbidden by design.
        if (suffix.Length == 0)
        {
            string outFolder = NormalizeFolder(OutputFolderPath);
 
            if (InputFiles.Any(f => string.Equals(
                    NormalizeFolder(Path.GetDirectoryName(f.FullPath)), outFolder, s_PathComparison)))
            {
                return BatchResult.Fatal(
                    "The output folder is the same as the folder of an input file and no suffix is set, " +
                    "so the original files would be overwritten.\n\n" +
                    "Enter a suffix or choose a different output folder.");
            }
        }
 
        if (SelectedColorMode == eColorMode.Indexed && Colors is < 1 or > 256)
        {
            return BatchResult.Fatal("Colors must be a number between 1 and 256.");
        }
 
        if (DeflateLevel is < 1 or > 9)
        {
            return BatchResult.Fatal("DEFLATE level must be a number between 1 and 9.");
        }
 
        List<string> inputs = InputFiles.Select(f => f.FullPath).ToList();
 
        IsCompressing = true;
        ProgressMax = inputs.Count;
        ProgressValue = 0;
        StatusText = $"0/{inputs.Count}";
 
        try
        {
            Progress<BatchProgress> progress = new(p =>
            {
                ProgressMax = p.Total;
                ProgressValue = p.Done;
                StatusText = $"{p.Done}/{p.Total}";
            });
 
            // 0 means "off" to the compressor script
            int posterizeValue = UsePosterization ? PosterizeLevel : 0;

            CompressArgs compressArgs = new(
                inputs,
                OutputFolderPath,
                suffix,
                SelectedColorMode,
                Colors,
                DeflateLevel,
                Dither,
                posterizeValue,
                progress
            );
                
            return await _compressionService.CompressBatchAsync(compressArgs);
        }
        finally
        {
            IsCompressing = false;
            StatusText = string.Empty;
        }
    }

    private static List<InputFileItem> ToUniqueItems(IEnumerable<string> paths, HashSet<string> seen)
    {
        List<InputFileItem> items = new List<InputFileItem>();
        foreach (string path in paths)
        {
            string full = Path.GetFullPath(path);
            if (seen.Add(full)) items.Add(new InputFileItem(full));
        }
        return items;
    }
    
    private void RememberInputPath(InputFileItem first)
    {
        Settings.LastInputPath = first.FullPath;
        Settings.WriteToJson();
    }
    
    private static string NormalizeFolder(string? path) =>
        string.IsNullOrEmpty(path) ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    
    /// <summary>
    /// Validates whether the file name chosen by the user is valid, e.g. if it contains valid characters, if it's
    /// non-empty, or if it's short enough. 
    /// </summary>
    /// <param name="fileName">The name of the file to analyze and validate.</param>
    /// <returns> An error message if the name isn't a valid file name on this OS, otherwise null. </returns>
    /// <remarks> Expects the name with the .png extension already added. </remarks>
    private static string? ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "The output file name cannot be empty.";

        // Per-OS invalid characters (includes path separators, so "a/b" is rejected)
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"The output file name contains invalid characters.\n" +
                   $"Not allowed: {s_InvalidCharsDisplay} (and control characters)";
        }

        // Rejects ".png", "..png" and similar names with nothing before the extension
        if (Path.GetFileNameWithoutExtension(fileName).Trim('.', ' ').Length == 0)
        {
            return "The output file name needs a name before the extension.";
        }

        if (OperatingSystem.IsWindows())
        {
            string reserved = fileName.Split('.')[0].Trim();
            if (s_WindowsReservedNames.Contains(reserved, StringComparer.OrdinalIgnoreCase))
            {
                return $"\"{reserved}\" is a reserved name on Windows. Please choose another one.";
            }
        }

        // Windows counts UTF-16 characters, Linux counts UTF-8 bytes
        int length = OperatingSystem.IsWindows()
            ? fileName.Length
            : Encoding.UTF8.GetByteCount(fileName);
        
        if (length > 255) return "The output file name is too long (255 characters maximum).";

        return null;
    }
}
