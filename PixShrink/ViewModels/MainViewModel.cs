using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PixShrink.Models;

namespace PixShrink.ViewModels;

public enum eColorMode { rgb = 0, grayscale = 1, indexed = 2 }

public partial class MainViewModel : ViewModelBase
{
    private static readonly string[] s_WindowsReservedNames =
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).SelectMany(i => new[] { $"COM{i}", $"LPT{i}" }))
            .ToArray();

    private static readonly string s_InvalidCharsDisplay = string.Join(" ",
        Path.GetInvalidFileNameChars().Where(c => !char.IsControl(c)));
    
    private readonly PngCompressionService _compressionService = new();
    
    [ObservableProperty]
    private eColorMode _selectedColorMode = eColorMode.rgb;
    
    [ObservableProperty]
    private string _inputFilePath = string.Empty;

    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _outputFileName = string.Empty;

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

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    private bool _isCompressing = false;
    
    public string FullOutputPath => 
        string.IsNullOrWhiteSpace(OutputFolderPath) || string.IsNullOrWhiteSpace(OutputFileName)
            ? string.Empty
            : Path.Combine(OutputFolderPath, EnsurePngExtension(OutputFileName));
    
    /// <summary>
    /// Returns true only when posterization is active and dithering exceeds the ideal threshold (0.5).
    /// </summary>
    public bool IsDitherWarningVisible => UsePosterization && Dither > 0.5;
    
    public AppSettings Settings { get; } = new();
    
    public MainViewModel()
    {
        Settings.ReadFromJson();
        
        OutputFolderPath = !string.IsNullOrWhiteSpace(Settings.LastOutputPath)
            ? Settings.LastOutputPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "out");
    }
    
    [RelayCommand(CanExecute = nameof(CanCompress))]
    public async Task<CompressionResult> CompressAsync()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath) ||
            string.IsNullOrWhiteSpace(OutputFolderPath) ||
            string.IsNullOrWhiteSpace(OutputFileName))
        {
            return new CompressionResult(false, 0, 0, 0, null, "Input and output files must be specified.");
        }

        string finalName = EnsurePngExtension(OutputFileName);
        string? nameError = ValidateFileName(finalName);
        if (nameError != null)
        {
            return new CompressionResult(false, 0, 0, 0, null, nameError);
        }

        OutputFileName = finalName;

        string inputFull = Path.GetFullPath(InputFilePath);
        string outputFull = Path.GetFullPath(FullOutputPath);

        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(inputFull, outputFull, comparison))
        {
            return new CompressionResult(false, 0, 0, 0, null, 
                "The output file would overwrite the input file.\nChoose a different output name or directory.");
        }

        if (!File.Exists(InputFilePath))
        {
            return new CompressionResult(false, 0, 0, 0, null, $"Input file not found:\n{InputFilePath}");
        }

        if (Colors is < 1 or > 256)
        {
            return new CompressionResult(false, 0, 0, 0, null, "Colors must be a number between 1 and 256.");
        }

        if (DeflateLevel is < 1 or > 9)
        {
            return new CompressionResult(false, 0, 0, 0, null, "DEFLATE level must be a number between 1 and 9.");
        }

        IsCompressing = true;

        try
        {
            // If posterization is not used, set the level to 0 which will evaluate to
            // false in the compressor script
            int posterizeValue = UsePosterization ? PosterizeLevel : 0;
            
            return await _compressionService.CompressAsync(InputFilePath, FullOutputPath, 
                SelectedColorMode, Colors, DeflateLevel, Dither, posterizeValue);
        }
        finally
        {
            IsCompressing = false;
        }
    }
    
    private bool CanCompress() => !IsCompressing;
    
    /// <summary>
    /// When a file is selected, inject the path and filename into the view's <see cref="InputFilePath"/>
    /// and <see cref="OutputFileName"/> fields.
    /// </summary>
    /// <param name="path">The path selected file to inject.</param>
    public void SetInputFile(string path)
    {
        InputFilePath = path;
        if (string.IsNullOrEmpty(OutputFileName))
        {
            OutputFileName = $"{Path.GetFileNameWithoutExtension(path)}Compressed{Path.GetExtension(path)}";
        }
        
        Settings.LastInputPath = path;
        Settings.WriteToJson();
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
    /// Returns just the file name, with ".png" appended if it isn't already there.
    /// </summary>
    private static string EnsurePngExtension(string fileName)
    {
        fileName = fileName.Trim();

        return fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".png";
    }
    
    /// <summary>
    /// Returns an error message if the name isn't a valid file name on this OS, otherwise null.
    /// Expects the name with the .png extension already added.
    /// </summary>
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
