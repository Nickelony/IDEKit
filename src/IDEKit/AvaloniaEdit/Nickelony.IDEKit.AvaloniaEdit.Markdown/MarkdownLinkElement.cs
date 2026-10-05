using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.Markdown.Planning;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>The inline element a rendered hyperlink is shown as.</summary>
/// <remarks>
/// The toolkit has no inline hyperlink, so a link is an <c>InlineUIContainer</c> hosting this text block. It opens through the renderer's opener
/// chain on a pointer press or the Enter or Space key; the renderer never opens a link on its own.
/// </remarks>
internal sealed class MarkdownLinkElement : TextBlock
{
	// The hand cursor is shared by every openable link instead of being recreated for each rendered link.
	private static readonly Cursor s_handCursor = new(StandardCursorType.Hand);

	/// <summary>The absolute target this link can open, or <see langword="null"/> when the renderer cannot open it.</summary>
	/// <remarks>
	/// The link keeps its link affordances either way and is only activatable when the target is present.
	/// </remarks>
	internal Uri? OpenableUri { get; private init; }

	/// <summary>The callback that opens this link's target through the renderer's opener chain.</summary>
	internal Action<Uri>? OpenLink { get; private init; }

	/// <summary>Creates the element for a link plan, which is activatable only when the link has an openable target.</summary>
	/// <param name="resources">The resolved theme resources that supply the link foreground.</param>
	/// <param name="plan">The link plan being rendered.</param>
	/// <param name="open">The callback that opens an openable target through the opener chain.</param>
	internal static MarkdownLinkElement Create(MarkdownThemeResources resources, MarkdownLinkPlan plan, Action<Uri> open)
	{
		bool canOpen = plan.OpenableUri is not null;

		return new MarkdownLinkElement
		{
			OpenableUri = plan.OpenableUri,
			OpenLink = canOpen ? open : null,
			Foreground = resources.LinkForeground,
			TextDecorations = Avalonia.Media.TextDecorations.Underline,
			Cursor = canOpen ? s_handCursor : Cursor.Default,
			Focusable = plan.IsFocusable,
			TextWrapping = TextWrapping.Wrap,
			VerticalAlignment = VerticalAlignment.Center,
		};
	}

	/// <inheritdoc/>
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		// A press the host already handled is left alone; otherwise only the left button activates the link,
		// matching MarkdownCodeBlockFactory.HandlePointerPressed, so a right or middle press reaches the host's context gesture.
		if (e.Handled || OpenableUri is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		Open();
		e.Handled = true;
	}

	/// <inheritdoc/>
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		// A key the host already handled is left alone, so the activation is skipped.
		if (e.Handled || OpenableUri is null || e.Key is not (Key.Enter or Key.Space))
			return;

		Open();
		e.Handled = true;
	}

	private void Open()
	{
		if (OpenLink is not null && OpenableUri is not null)
			OpenLink(OpenableUri);
	}
}
