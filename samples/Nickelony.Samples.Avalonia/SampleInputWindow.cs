using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Nickelony.Samples.Avalonia;

/// <summary>
/// A prompt for one name, such as the new name of a symbol or of a file, which closes with the name or with <see langword="null"/> when canceled.
/// </summary>
internal sealed class SampleInputWindow : Window
{
	private readonly TextBox _name = new() { MinWidth = 260 };

	/// <param name="title">The title of the window.</param>
	/// <param name="prompt">The label above the box.</param>
	/// <param name="accept">The caption of the button that accepts the name.</param>
	/// <param name="initialText">The name the box starts with, selected so typing replaces it.</param>
	internal SampleInputWindow(string title, string prompt, string accept, string initialText = "")
	{
		Title = title;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		SizeToContent = SizeToContent.WidthAndHeight;
		CanResize = false;
		ShowInTaskbar = false;

		_name.Text = initialText;

		var accepted = new Button { Content = accept, IsDefault = true };

		var cancel = new Button
		{
			Content = "Cancel",
			IsCancel = true,
			Margin = new Thickness(left: 6, top: 0, right: 0, bottom: 0),
		};

		accepted.Click += (_, _) => Close(_name.Text);
		cancel.Click += (_, _) => Close(null);

		var buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(left: 0, top: 12, right: 0, bottom: 0),
		};

		buttons.Children.Add(accepted);
		buttons.Children.Add(cancel);

		var layout = new StackPanel { Margin = new Thickness(14) };

		layout.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 4) });
		layout.Children.Add(_name);
		layout.Children.Add(buttons);

		Content = layout;

		Opened += (_, _) =>
		{
			_name.Focus(NavigationMethod.Tab);
			_name.SelectAll();
		};
	}
}
