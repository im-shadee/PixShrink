using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PixShrink.Views;

/// <summary>
/// Minimal modal message Window.
/// </summary>
/// <example>
/// Global logs:
///     await MessageDialog.ShowAsync(this, "MyTitle", "This is a template dialog")
/// Error logs:
///     await MessageDialog.ShowErrorAsync(this, "Something went wrong")
/// </example>
public class MessageDialog : Window
{
    private MessageDialog(string title, string message)
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button okButton = new Button()
        {
            Content = "OK",
            MinWidth = 80,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true, // Enter key triggers it
        };
        okButton.Click += (_, _) => Close(); // Close the window once the ok button is clicked

        // Dynamically create window content
        Content = new StackPanel()
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                okButton,
            }
        };
    }

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/> and completes when it closes.
    /// </summary>
    public static Task ShowAsync(Window owner, string title, string message)
        => new MessageDialog(title, message).ShowDialog(owner);
    
    public static Task ShowErrorAsync(Window owner, string message)
        => ShowAsync(owner, "Error", message);
    
    public static Task ShowWarningAsync(Window owner, string message)
        => ShowAsync(owner, "Warning", message);
 
    public static Task ShowInfoAsync(Window owner, string message)
        => ShowAsync(owner, "Info", message);
}
