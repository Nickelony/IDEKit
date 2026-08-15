using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>
/// The inline element a rendered hyperlink is shown as.
/// </summary>
/// <remarks>
/// The toolkit has no inline hyperlink, so a link is an <c>InlineUIContainer</c> hosting this text block. It
/// exposes the target it can open so a host or test can inspect it, and it opens through the renderer's
/// opener chain on a pointer press or the Enter or Space key; the renderer never opens a link on its own.
/// </remarks>
internal sealed class MarkdownLinkElement : TextBlock
{
	/// <summary>
	/// Gets the absolute target this link can open, or <see langword="null"/> when the renderer cannot open it.
	/// The link keeps its link affordances either way and is only activatable when the target is present.
	/// </summary>
	public Uri? OpenableUri { get; private init; }

	/// <summary>
	/// Gets the callback that opens this link's target through the renderer's opener chain.
	/// </summary>
	public Action<Uri>? Open { get; private init; }

	/// <summary>
	/// Creates a rendered link element.
	/// </summary>
	/// <param name="theme">The theme that supplies the link foreground.</param>
	/// <param name="link">The link node being rendered.</param>
	/// <param name="options">The rendering options that decide keyboard reachability.</param>
	/// <param name="open">The callback that opens an openable target through the opener chain.</param>
	/// <returns>The link element.</returns>
	internal static MarkdownLinkElement Create(MarkdownRenderTheme theme, MarkdownLink link, MarkdownRenderOptions options, Action<Uri> open)
	{
		bool canOpen = link.OpenableUri is not null;

		return new MarkdownLinkElement
		{
			OpenableUri = link.OpenableUri,
			Open = canOpen ? open : null,
			Foreground = theme.LinkForeground,
			TextDecorations = Avalonia.Media.TextDecorations.Underline,
			Cursor = canOpen ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
			// Only an openable link becomes a keyboard stop when interaction is enabled; a link the
			// renderer cannot open must not add a dead stop.
			Focusable = canOpen && options.AllowContentInteraction,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Center
		};
	}

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		if (OpenableUri is null)
			return;

		TryOpen();
		e.Handled = true;
	}

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		if (OpenableUri is null || e.Key is not (Key.Enter or Key.Space))
			return;

		TryOpen();
		e.Handled = true;
	}

	private void TryOpen()
	{
		if (Open is not null && OpenableUri is not null)
			Open(OpenableUri);
	}
}
