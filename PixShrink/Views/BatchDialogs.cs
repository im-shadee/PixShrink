using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PixShrink.Models;

namespace PixShrink.Views;

/// <summary>
/// Modal Yes/No question. Usage:
///     if (await ConfirmDialog.ShowAsync(this, "Replace files", "Replace 3 files?")) { ... }
/// Closing the window with the X counts as "No".
/// </summary>
public class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button yes = new()
        {
            Content = "Yes",
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true // Enter
        };
        Button no = new()
        {
            Content = "No",
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true // Escape
        };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no }
                }
            }
        };
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message)
        => await new ConfirmDialog(title, message).ShowDialog<bool>(owner);
}

/// <summary>
/// Per-file results of a batch in a scrollable, selectable text area (so errors can be copied).
/// </summary>
public class ResultsDialog : Window
{
    private ResultsDialog(BatchResult result)
    {
        Title = "Compression finished";
        Width = 560;
        Height = 480;
        MinWidth = 400;
        MinHeight = 260;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBox box = new()
        {
            Text = BuildText(result),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        Button ok = new()
        {
            Content = "OK",
            MinWidth = 80,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true,
            IsCancel = true
        };
        ok.Click += (_, _) => Close();

        Grid.SetRow(box, 0);
        Grid.SetRow(ok, 1);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { box, ok }
        };
    }

    public static Task ShowAsync(Window owner, BatchResult result)
        => new ResultsDialog(result).ShowDialog(owner);

    private static string BuildText(BatchResult r)
    {
        StringBuilder sb = new();

        sb.AppendLine($"{r.Succeeded} succeeded, {r.Failed} failed");

        List<FileResult> ok = r.Files.Where(f => f.Success).ToList();
        double before = ok.Sum(f => f.KbBefore);
        double after = ok.Sum(f => f.KbAfter);
        if (ok.Count > 0 && before > 0)
        {
            sb.AppendLine($"Total: {before:F2} KB -> {after:F2} KB ({(before - after) / before * 100:F1}% smaller)");
        }
        sb.AppendLine();

        foreach (FileResult f in r.Files)
        {
            if (f.Success)
            {
                sb.AppendLine($"{f.Name}: Succeeded!");
                sb.AppendLine($"Old size: {f.KbBefore:F2} KB, new size: {f.KbAfter:F2} KB, Reduction: {f.ReductionPercent:F1}%");
            }
            else
            {
                sb.AppendLine($"{f.Name}: Failed - {f.Error}");
            }
            sb.AppendLine();
        }

        // One note for the whole batch instead of repeating it on every file
        List<string> notes = ok.Select(f => f.OxipngNote).OfType<string>().Distinct().ToList();
        if (notes.Count > 0)
        {
            sb.AppendLine($"Note: the lossless pass was skipped for some files ({string.Join("; ", notes)}).");
            sb.AppendLine();
        }

        if (r.Failed > 0 && r.LogPath != null)
        {
            sb.AppendLine("Details were saved to:");
            sb.AppendLine(r.LogPath);
        }

        return sb.ToString().TrimEnd();
    }
}
