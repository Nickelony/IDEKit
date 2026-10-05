using System.Windows;
using System.Windows.Controls;

namespace Nickelony.Samples.Wpf;

/// <summary>
/// A prompt for one name, such as the new name of a symbol or of a file, which closes with a dialog result of <see langword="true"/> when accepted.
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
		SizeToContent = SizeToContent.WidthAndHeight;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;

		_name.Text = initialText;

		var accepted = new Button
		{
			Content = accept,
			IsDefault = true,
			Padding = new Thickness(left: 10, top: 2, right: 10, bottom: 2),
		};

		var cancel = new Button
		{
			Content = "Cancel",
			IsCancel = true,
			Padding = new Thickness(left: 10, top: 2, right: 10, bottom: 2),
			Margin = new Thickness(left: 6, top: 0, right: 0, bottom: 0),
		};

		accepted.Click += (_, _) => DialogResult = true;

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

		Loaded += (_, _) =>
		{
			_name.Focus();
			_name.SelectAll();
		};
	}

	/// <summary>The name the user typed.</summary>
	internal string Text => _name.Text;

	/// <summary>Shows the prompt and returns the name, or <see langword="null"/> when the user canceled.</summary>
	internal string? Ask(Window owner)
	{
		Owner = owner;
		return ShowDialog() == true ? Text : null;
	}
}
