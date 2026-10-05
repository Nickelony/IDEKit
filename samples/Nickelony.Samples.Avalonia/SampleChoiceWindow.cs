using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nickelony.Samples.Avalonia;

/// <summary>
/// A question with a row of answers, which closes with the index of the chosen answer, or with <see langword="null"/> when closed without one.
/// </summary>
/// <remarks>
/// Avalonia ships no message box, so this window asks what the WPF head asks through <c>MessageBox</c>.
/// </remarks>
internal sealed class SampleChoiceWindow : Window
{
	/// <param name="title">The title of the window.</param>
	/// <param name="question">The question the window asks.</param>
	/// <param name="answers">The captions of the answers; the first is the default and the last cancels.</param>
	internal SampleChoiceWindow(string title, string question, params string[] answers)
	{
		Title = title;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		MinWidth = 320;
		SizeToContent = SizeToContent.WidthAndHeight;
		CanResize = false;
		ShowInTaskbar = false;

		var buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Spacing = 6,
			Margin = new Thickness(left: 0, top: 14, right: 0, bottom: 0),
		};

		for (int index = 0; index < answers.Length; index++)
		{
			int answer = index;

			var button = new Button
			{
				Content = answers[index],
				IsDefault = index == 0,
				IsCancel = index == answers.Length - 1,
			};

			button.Click += (_, _) => Close(answer);
			buttons.Children.Add(button);
		}

		var layout = new StackPanel { Margin = new Thickness(16), MaxWidth = 460 };

		layout.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });
		layout.Children.Add(buttons);

		Content = layout;
	}
}
