using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    #region Window Overrides

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

    #endregion

    #region InputSelection
    
    // Input events when dragging a file over the input file field
    /// <summary>
    /// When dragging a file over the input file field, set the cursor icon to 'copy' instead of the
    /// regular pointer.
    /// </summary>
    /// <remarks>The cursor icon does not change if the dropped file is not a PNG.</remarks>
    private void OnInputDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !ViewModel.IsCompressing && TryGetDroppedPngs(e, out _)
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

        if (!TryGetDroppedPngs(e, out List<string> paths))
        {
            await MessageDialog.ShowWarningAsync(this, "Only .png files can be dropped here.");
            return;
        }

        ViewModel.AddInputFiles(paths); // Dropping adds to the selection instead of replacing
    }
    
    /// <summary>
    /// Single entry point for the "..." button and drag-and-drop.
    /// If files are already selected, the user must before the selection is replaced.
    /// </summary>
    private async Task ApplySelectionAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
 
        int current = ViewModel.InputFiles.Count;
        if (current > 0)
        {
            bool replace = await ConfirmDialog.ShowAsync(this, "Replace selection",
                $"Replace the {current} currently selected file{(current == 1 ? "" : "s")} " +
                $"with {paths.Count} new file{(paths.Count == 1 ? "" : "s")}?");
 
            if (!replace) return;
        }
 
        ViewModel.SetInputFiles(paths);
    }

    /// <summary>Opens the multi-select PNG picker; returns the chosen paths (empty if cancelled).</summary>
    private async Task<List<string>> PickPngPathsAsync()
    {
        IStorageFolder? startDir = await TryGetFolderAsync(ViewModel.Settings.LastInputPath);

        FilePickerOpenOptions options = new()
        {
            Title = "Choose input PNGs",
            AllowMultiple = true,
            FileTypeFilter = new[] { FilePickerFileTypes.ImagePng },
            SuggestedStartLocation = startDir
        };

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async void OnChooseFiles(object? sender, RoutedEventArgs e)
    {
        await ApplySelectionAsync(await PickPngPathsAsync()); // Replace selection => asks first if the list isn't empty
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddInputFiles(await PickPngPathsAsync()); // Add to selection => no prompt
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        List<InputFileItem> selected = 
            InputFilesList.SelectedItems?.OfType<InputFileItem>().ToList() ?? new();
        
        ViewModel.RemoveInputFiles(selected);
    }

    private void OnInputSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedInputCount = InputFilesList.SelectedItems?.Count ?? 0;
    }
    
    #endregion

    #region Directory Selection

    private async void OnChooseDirectory(object? sender, RoutedEventArgs e)
    {
        // Safe if null: Start location is set to default location
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
        string dir;
        
        try
        {
            dir = Path.GetFullPath(GetStartDirectory(current));
        }
        catch (Exception e)
        {
            await MessageDialog.ShowErrorAsync(this, e.Message);
            return null;
        }

        return await StorageProvider.TryGetFolderFromPathAsync(new Uri(dir));
    }

    private string GetStartDirectory(string? current)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current)) return current;
            string? parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
        }

        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }
        catch (Exception e)
        {
            MessageDialog.ShowErrorAsync(this, e.Message);
            return string.Empty;
        }
    }

    #endregion

    #region Compression

    private async void OnCompressClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Run compression
        BatchResult result = await vm.CompressAsync();

        // The batch could not run at all (validation problem, compressor missing...)
        if (result.FatalError != null)
        {
            await MessageDialog.ShowErrorAsync(this, result.FatalError);
            return;
        }
 
        await ResultsDialog.ShowAsync(this, result);
    }

    #endregion
    
    #region Helpers

    /// <summary>
    /// Collects every dropped file that is an existing .png. Anything else is ignored.
    /// </summary>
    private static bool TryGetDroppedPngs(DragEventArgs e, out List<string> paths)
    {
        paths = new List<string>();
 
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        if (items == null) return false;
 
        foreach (IStorageItem item in items)
        {
            string? local = item.TryGetLocalPath();
 
            if (local != null
                && File.Exists(local)
                && local.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(local);
            }
        }
 
        return paths.Count > 0;
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

    #endregion
}
