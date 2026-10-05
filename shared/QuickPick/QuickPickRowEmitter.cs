using Nickelony.Matching;
#if AVALONIAQUICKPICK
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using IconImage = Avalonia.Media.IImage;
using RowElement = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Controls;
using IconImage = System.Windows.Media.ImageSource;
using RowElement = System.Windows.UIElement;
#endif

#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia;

#else

namespace Nickelony.QuickPick.Wpf;

#endif

/// <summary>
/// Writes the default visual tree of one quick-pick row: a separator, or an item with its icon, label, description, detail and trailing text.
/// </summary>
/// <remarks>
/// The label and the description draw the characters of their highlight ranges bold. The description, the detail, the trailing text and a separator
/// are drawn at reduced opacity, and a disabled item is dimmed as a whole, so the colors of the row follow its foreground.
/// </remarks>
internal static class QuickPickRowEmitter
{
	private const double IconSize = 16.0;
	private const double DimmedOpacity = 0.65;
	private const double DisabledOpacity = 0.45;

	/// <summary>Writes the visual tree of a row.</summary>
	/// <param name="row">The row to draw.</param>
	/// <param name="theme">The theme, which sets the least height of a row.</param>
	/// <param name="resolveIcon">The hook that maps an icon key to an image, or <see langword="null"/> when the host draws no icons.</param>
	internal static RowElement Emit(QuickPickRow row, QuickPickTheme theme, Func<string, IconImage?>? resolveIcon)
	{
		if (row.Item is { } item)
			return EmitItem(item, theme, resolveIcon);

		return row.Message is { } message ? EmitMessage(message, theme) : EmitSeparator(row.SeparatorLabel ?? string.Empty, theme);
	}

	/// <summary>Gives the text a screen reader announces for a row.</summary>
	/// <param name="row">The row to describe.</param>
	/// <returns>The label, the description and the trailing text of an item, separated by commas, or the text of a separator or a message.</returns>
	internal static string AccessibleNameOf(QuickPickRow row)
	{
		if (row.Item is not { } item)
			return row.Message ?? row.SeparatorLabel ?? string.Empty;

		return string.Join(", ", new[] { item.Label, item.Description, item.TrailingText }.Where(part => !string.IsNullOrEmpty(part)));
	}

	private static TextBlock EmitMessage(string text, QuickPickTheme theme)
	{
		return new TextBlock
		{
			Text = text,
			Opacity = DimmedOpacity,
			Margin = new Thickness(left: 8.0, top: 6.0, right: 8.0, bottom: 6.0),
			MinHeight = theme.RowHeight * 0.6,
		};
	}

	private static TextBlock EmitSeparator(string label, QuickPickTheme theme)
	{
		return new TextBlock
		{
			Text = label,
			Opacity = DimmedOpacity,
			Margin = new Thickness(left: 8.0, top: 6.0, right: 8.0, bottom: 2.0),
			MinHeight = theme.RowHeight * 0.6,
		};
	}

	private static DockPanel EmitItem(QuickPickItem item, QuickPickTheme theme, Func<string, IconImage?>? resolveIcon)
	{
		var panel = new DockPanel { MinHeight = theme.RowHeight, Margin = new Thickness(left: 6.0, top: 2.0, right: 6.0, bottom: 2.0) };

		if (item.IconKey is { } iconKey && resolveIcon?.Invoke(iconKey) is { } icon)
		{
			var image = new Image
			{
				Source = icon,
				Width = IconSize,
				Height = IconSize,
				Margin = new Thickness(left: 0.0, top: 0.0, right: 8.0, bottom: 0.0),
				VerticalAlignment = VerticalAlignment.Center,
			};

			DockPanel.SetDock(image, Dock.Left);
			panel.Children.Add(image);
		}

		if (!string.IsNullOrEmpty(item.TrailingText))
		{
			var trailing = new TextBlock
			{
				Text = item.TrailingText,
				Opacity = DimmedOpacity,
				Margin = new Thickness(left: 16.0, top: 0.0, right: 0.0, bottom: 0.0),
				VerticalAlignment = VerticalAlignment.Center,
			};

			DockPanel.SetDock(trailing, Dock.Right);
			panel.Children.Add(trailing);
		}

		panel.Children.Add(EmitText(item));

		if (!item.IsEnabled)
			panel.Opacity = DisabledOpacity;

		return panel;
	}

	// The label, the description and the detail sit in one clipped column, so a long text never pushes the trailing text out of the row.
	private static StackPanel EmitText(QuickPickItem item)
	{
		var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
		var line = new StackPanel { Orientation = Orientation.Horizontal };

		line.Children.Add(EmitHighlighted(item.Label, item.LabelHighlights, opacity: 1.0));

		if (!string.IsNullOrEmpty(item.Description))
		{
			TextBlock description = EmitHighlighted(item.Description, item.DescriptionHighlights, DimmedOpacity);

			description.Margin = new Thickness(left: 8.0, top: 0.0, right: 0.0, bottom: 0.0);
			line.Children.Add(description);
		}

		column.Children.Add(line);

		if (!string.IsNullOrEmpty(item.Detail))
			column.Children.Add(new TextBlock { Text = item.Detail, Opacity = DimmedOpacity });

		return column;
	}

	private static TextBlock EmitHighlighted(string text, IReadOnlyList<MatchRange> highlights, double opacity)
	{
		var textBlock = new TextBlock { Opacity = opacity };

		if (highlights.Count == 0)
			textBlock.Text = text;
		else
			QuickPickSeam.SetRuns(textBlock, SplitRuns(text, highlights));

		return textBlock;
	}

	/// <summary>Cuts a text into runs at the edges of the highlight ranges, each run marked bold or not.</summary>
	/// <param name="text">The text to cut.</param>
	/// <param name="highlights">The ranges to draw bold, ascending; a range past the end of the text is clamped to it.</param>
	/// <returns>The runs, in order, that together hold the whole text.</returns>
	internal static IReadOnlyList<(string Text, bool IsBold)> SplitRuns(string text, IReadOnlyList<MatchRange> highlights)
	{
		List<(string Text, bool IsBold)> runs = [];
		int position = 0;

		foreach (MatchRange range in highlights)
		{
			int start = Math.Clamp(range.Offset, position, text.Length);
			int end = Math.Clamp(range.EndOffset, start, text.Length);

			if (start > position)
				runs.Add((text[position..start], false));

			if (end > start)
				runs.Add((text[start..end], true));

			position = end;
		}

		if (position < text.Length)
			runs.Add((text[position..], false));

		return runs;
	}
}
