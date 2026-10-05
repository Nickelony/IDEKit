using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
#if AVALONIAEDIT
using AvaloniaEdit.Editing;
using PopupContent = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit.Editing;
using PopupContent = System.Windows.UIElement;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

#endif

/// <summary>Shows the hover and the diagnostic under the pointer in a popup below their text, with content the host builds.</summary>
/// <remarks>
/// <para>
/// <see cref="Show"/> matches <see cref="HoverControllerHooks.ShowHover"/>, so a host passes it as the hook. The popup anchors
/// at the start of the hover's range, else of the diagnostic's range, else at the offset the controller targets. It closes on any change
/// of the document's text, typed, deleted, pasted or applied from another view, when the text area replaces its document, on Esc, when the text area
/// loses keyboard focus and when its window deactivates, and a show while the window is inactive hides it instead.
/// </para>
/// <para>
/// The popup takes the text area as its logical parent where the toolkit needs one to style the content, so a code block of the content renders.
/// The presenter must be created and used on the text area's thread, and the host disposes it with the editor.
/// </para>
/// </remarks>
public sealed class HoverPresenter : IDisposable
{
	private readonly Func<HoverInformation?, Diagnostic?, PopupContent?> _buildContent;
	private HoverPopupTheme _theme;

	/// <param name="textArea">The text area whose hovers the popup shows.</param>
	/// <param name="buildContent">
	/// Builds the content for a hover, a diagnostic or both, such as rendered Markdown; it returns <see langword="null"/>
	/// to show nothing, which hides the popup. It is never called with both arguments <see langword="null"/>.
	/// </param>
	/// <param name="presentation">The popup theme, or <see langword="null"/> for <see cref="HoverPresentationOptions.Default"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> or <paramref name="buildContent"/> is <see langword="null"/>.</exception>
	public HoverPresenter(
		TextArea textArea,
		Func<HoverInformation?, Diagnostic?, PopupContent?> buildContent,
		HoverPresentationOptions? presentation = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(buildContent);

		_buildContent = buildContent;
		Popup = new FeaturePopupPresenter(textArea, closesOnTextChange: true);
		_theme = (presentation ?? HoverPresentationOptions.Default).Theme;
		ApplyTheme();
	}

	/// <summary>
	/// The frame of the popup; it starts as <see cref="HoverPresentationOptions.Theme"/>, and an assignment restyles an open popup at once.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public HoverPopupTheme Theme
	{
		get => _theme;
		set
		{
			_theme = value ?? throw new ArgumentNullException(nameof(value));
			ApplyTheme();
		}
	}

	public bool IsOpen => Popup.IsOpen;

	/// <summary>The shared popup, which a test reads.</summary>
	internal FeaturePopupPresenter Popup { get; }

	/// <summary>Shows the hover and the diagnostic, or hides the popup when both are <see langword="null"/>.</summary>
	/// <param name="information">The hover content, or <see langword="null"/> when there is none.</param>
	/// <param name="diagnostic">The diagnostic under the pointer, or <see langword="null"/> when there is none.</param>
	/// <param name="offset">
	/// The document offset to anchor at when neither argument carries a range, or <see langword="null"/> when the evaluation found no target.
	/// </param>
	/// <remarks>
	/// The content is built only for a show that can succeed, so a show while the window is inactive or after disposal calls no builder.
	/// </remarks>
	public void Show(HoverInformation? information, Diagnostic? diagnostic, int? offset)
	{
		int? anchorOffset = information?.Range?.Offset ?? diagnostic?.Range.Offset ?? offset;

		if ((information is null && diagnostic is null) || anchorOffset is not { } anchor || !Popup.CanShow())
		{
			Hide();
			return;
		}

		if (_buildContent(information, diagnostic) is not { } content)
		{
			Hide();
			return;
		}

		Popup.Show(content, anchor);
	}

	/// <summary>Closes the popup; a call while it is closed does nothing.</summary>
	public void Hide() => Popup.Hide();

	/// <inheritdoc/>
	public void Dispose() => Popup.Dispose();

	private void ApplyTheme()
	{
		Popup.ApplyTheme(
			background: _theme.Background,
			borderBrush: _theme.BorderBrush,
			borderThickness: _theme.BorderThickness,
			cornerRadius: _theme.CornerRadius,
			padding: _theme.Padding,
			maxWidth: _theme.MaxWidth
		);
	}
}
