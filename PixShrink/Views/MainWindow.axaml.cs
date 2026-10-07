using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using PixShrink.Models;
using PixShrink.ViewModels;
using IStorageFolder = Avalonia.Platform.Storage.IStorageFolder;

namespace PixShrink.Views;

public partial class MainWindow : Window
{
    private static readonly string s_AppName = "PixShrink";
    private static readonly string s_VersionNumber = "beta-1.1.0";
    
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{s_AppName}_{s_VersionNumber}";
        
        DataContext = new MainViewModel();
        
        // Add events to handle file-drop into the input file field
        InputDropArea.AddHandler(DragDrop.DragOverEvent, OnInputDragOver,
            RoutingStrategies.Bubble, handledEventsToo: true);
        InputDropArea.AddHandler(DragDrop.DropEvent, OnInputDrop,
            RoutingStrategies.Bubble, handledEventsToo: true);

        ApplySavedWindowSettings();
    }
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToWorkingArea();
    }
    
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (ViewModel.IsCompressing && e.CloseReason != WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
            _ = MessageDialog.ShowWarningAsync(this,
                "A compression is still running.\nPlease wait for it to finish before closing.");
            return;
        }

        if (e.Cancel) return;

        if (WindowState == WindowState.Normal)
        {
            ViewModel.Settings.WindowSettings.Width = ClientSize.Width;
            ViewModel.Settings.WindowSettings.Height = ClientSize.Height;
            ViewModel.Settings.WindowSettings.IsMaximized = false;
        }
        else if (WindowState == WindowState.Maximized)
        {
            ViewModel.Settings.WindowSettings.IsMaximized = true;
        }

        ViewModel.Settings.WriteToJson();
    }
    
    // Input events when dragging a file over the input file field
    /// <summary>
    /// When dragging a file over the input file field, set the cursor icon to 'copy' instead of the
    /// regular pointer.
    /// </summary>
    /// <remarks>The cursor icon does not change if the dropped file is not a PNG.</remarks>
    private void OnInputDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !ViewModel.IsCompressing && TryGetDroppedPng(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }
    
    /// <summary>
    /// Communicates with the ViewModel to inject the selected path as well as the selected file's name
    /// into the input fields.
    /// </summary>
    /// <remarks>Displays an error window if the dropped file is not a PNG.</remarks>
    private async void OnInputDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        
        // Do not allow drag-and-drop while a png is already compressing
        if (ViewModel.IsCompressing) return;

        if (!TryGetDroppedPng(e, out string path))
        {
            await MessageDialog.ShowWarningAsync(this, "Only .png files can be dropped here.");
            return;
        }

        ViewModel.SetInputFile(path);
    }
    
    private static bool TryGetDroppedPng(DragEventArgs e, out string path)
    {
        path = string.Empty;
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        string? local = items?.FirstOrDefault()?.TryGetLocalPath();

        if (local == null 
            || !File.Exists(local) 
            || !local.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = local;
        return true;
    }
    
    /// <summary>
    /// Clamp the window's size to the user's current screen size.
    /// </summary>
    private void ClampToWorkingArea()
    {
        if (WindowState != WindowState.Normal) return;

        Screen? screen = Screens.Primary;
        if (screen == null) return;

        double maxWidth = screen.WorkingArea.Width / screen.Scaling;
        double maxHeight = screen.WorkingArea.Height / screen.Scaling;

        Size frame = FrameSize ?? ClientSize;
        double chromeWidth = frame.Width - ClientSize.Width;
        double chromeHeight = frame.Height - ClientSize.Height;

        double width = Math.Min(ClientSize.Width, maxWidth - chromeWidth);
        double height = Math.Min(ClientSize.Height, maxHeight - chromeHeight);

        Width = Math.Max(width, MinWidth);
        Height = Math.Max(height, MinHeight);
    }
    
    /// <summary>
    /// Method called whenever changes happen to the UI (input/output fields set, etc.).
    /// Interacts with the ViewModel script that owns the settings' saving/loading pipeline.
    /// </summary>
    private void ApplySavedWindowSettings()
    {
        WindowSettings saved = ViewModel.Settings.WindowSettings;
        if (saved.Width > 0 && saved.Height > 0)
        {
            Width = Math.Max(saved.Width, MinWidth);
            Height = Math.Max(saved.Height, MinHeight);
        }

        if (saved.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }
    
    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        string startFrom = string.IsNullOrEmpty(ViewModel.InputFilePath)
            ? ViewModel.Settings.LastInputPath
            : ViewModel.InputFilePath;

        IStorageFolder? startDir = await TryGetFolderAsync(startFrom);

        FilePickerOpenOptions options = new()
        {
            Title = "Choose input PNG",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImagePng },
            SuggestedStartLocation = startDir
        };

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            string? path = files[0].TryGetLocalPath();
            if (path != null) ViewModel.SetInputFile(path);
        }
    }

    private async void OnChooseDirectory(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? startDir = await TryGetFolderAsync(ViewModel.OutputFolderPath);

        FolderPickerOpenOptions options = new()
        {
            Title = "Choose destination folder",
            AllowMultiple = false,
            SuggestedStartLocation = startDir
        };

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            string? path = folders[0].TryGetLocalPath();
            if (path != null) ViewModel.SetOutputFolder(path);
        }
    }

    private async Task<IStorageFolder?> TryGetFolderAsync(string? current)
    {
        string dir = Path.GetFullPath(GetStartDirectory(current));
        return await StorageProvider.TryGetFolderFromPathAsync(new Uri(dir));
    }

    private static string GetStartDirectory(string? current)
    {
        if (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current)) return current;
            string? parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }
    
    private async void OnCompressClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Run compression
        CompressionResult result = await vm.CompressAsync();

        // Show appropriate dialog based on result
        if (!result.Success)
        {
            await MessageDialog.ShowErrorAsync(this, result.ErrorMessage ?? "An unknown error occurred.");
        }
        else
        {
            string message = $"Original size: {result.KbBefore:F2} KB\n" +
                             $"Compressed size: {result.KbAfter:F2} KB\n" +
                             $"Reduced by: {result.ReductionPercent:F1}%\n" +
                             (string.IsNullOrEmpty(result.OxipngNote) ? "" : $"\n{result.OxipngNote}");

            await MessageDialog.ShowInfoAsync(this, message);
        }
    }
}
