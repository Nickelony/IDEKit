using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
#if AVALONIAEDIT
using AvaloniaEdit.Editing;
using PopupContent = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit.Editing;
using PopupContent = System.Windows.UIElement;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;

#endif

/// <summary>Shows a signature help answer in a popup below its request offset, with content the host builds.</summary>
/// <remarks>
/// <para>
/// <see cref="Show"/> matches <see cref="SignatureHelpControllerHooks.ShowSignatureHelp"/>, and the presenter overload
/// of <see cref="SignatureHelpControllerFactory"/> wires it to a controller. The popup closes on Esc, when the text area loses keyboard focus,
/// when its window deactivates, when the text area replaces its document and when the toolkit closes it, and raises <see cref="Dismissed"/>
/// for each of those; typed text leaves it open, because the call it describes goes on. A show while the window is inactive hides the popup instead.
/// </para>
/// <para>
/// The popup takes the text area as its logical parent where the toolkit needs one to style the content, so a code block of the content renders.
/// The presenter must be created and used on the text area's thread, and the host disposes it with the editor.
/// </para>
/// </remarks>
public sealed class SignatureHelpPresenter : IDisposable
{
	private readonly TextArea _textArea;
	private readonly Func<SignatureHelpInformation, PopupContent?> _buildContent;

	private SignatureHelpPopupTheme _theme;

	// The controller that the factory wired to the presenter, which the latest wiring replaces, so a recreated controller does not leave the
	// disposed one reachable from the presenter.
	private SignatureHelpController? _controller;
	private Action? _caretMoved;
	private IDisposable? _typedText;

	/// <param name="textArea">The text area whose signature help the popup shows.</param>
	/// <param name="buildContent">
	/// Builds the content for an answer, such as the active signature rendered as Markdown; it returns <see langword="null"/>
	/// to show nothing, which hides the popup.
	/// </param>
	/// <param name="presentation">The popup theme, or <see langword="null"/> for <see cref="SignatureHelpPresentationOptions.Default"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textArea"/> or <paramref name="buildContent"/> is <see langword="null"/>.</exception>
	public SignatureHelpPresenter(
		TextArea textArea,
		Func<SignatureHelpInformation, PopupContent?> buildContent,
		SignatureHelpPresentationOptions? presentation = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(buildContent);

		_textArea = textArea;
		_buildContent = buildContent;
		Popup = new FeaturePopupPresenter(textArea, closesOnTextChange: false);
		Popup.Dismissed += HandlePopupDismissed;
		_theme = (presentation ?? SignatureHelpPresentationOptions.Default).Theme;
		ApplyTheme();

		textArea.Caret.PositionChanged += HandleCaretPositionChanged;
	}

	/// <summary>
	/// Raised when the popup closed on its own: on Esc, on the focus loss of the text area, on the deactivation of its window, on a document
	/// replacement or when the toolkit closed it.
	/// </summary>
	/// <remarks>
	/// <see cref="Hide"/> and a show that hides the popup raise nothing, so a host that hides the popup itself hears only the closes it did not make.
	/// A controller that the factory wired to the presenter is dismissed on each of these closes before the event is raised.
	/// </remarks>
	public event EventHandler? Dismissed;

	/// <summary>
	/// The frame of the popup; it starts as <see cref="SignatureHelpPresentationOptions.Theme"/>, and an assignment restyles an open popup at once.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public SignatureHelpPopupTheme Theme
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

	/// <summary>Shows an answer below a document offset, or hides the popup when the content builder returns <see langword="null"/>.</summary>
	/// <param name="information">The answer to show.</param>
	/// <param name="offset">The document offset to anchor at, which a controller passes as the offset of its request.</param>
	/// <exception cref="ArgumentNullException"><paramref name="information"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The content is built only for a show that can succeed, so a show while the window is inactive or after disposal calls no builder.
	/// </remarks>
	public void Show(SignatureHelpInformation information, int offset)
	{
		ArgumentNullException.ThrowIfNull(information);

		if (!Popup.CanShow() || _buildContent(information) is not { } content)
		{
			Popup.Hide();
			return;
		}

		Popup.Show(content, offset);
	}

	/// <summary>Closes the popup without raising <see cref="Dismissed"/>; a call while it is closed does nothing.</summary>
	/// <remarks>
	/// A controller that the factory wired to the presenter is dismissed first, so it never counts a popup visible that the host closed.
	/// </remarks>
	public void Hide()
	{
		_controller?.Dismiss();
		Popup.Hide();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_textArea.Caret.PositionChanged -= HandleCaretPositionChanged;
		Popup.Dismissed -= HandlePopupDismissed;
		_controller = null;
		_caretMoved = null;
		_typedText?.Dispose();
		_typedText = null;
		Popup.Dispose();
	}

	/// <summary>Wires the controller the factory created, replacing an earlier wiring, so the presenter's closes and caret moves reach it.</summary>
	/// <param name="controller">The controller, which a close of the popup or <see cref="Hide"/> dismisses.</param>
	/// <param name="caretMoved">Invoked when the caret moves while the popup is open.</param>
	/// <param name="typedText">
	/// The subscription that starts the controller's requests on typed characters, which the presenter disposes when a later wiring replaces
	/// it or the presenter is disposed, or <see langword="null"/> for none.
	/// </param>
	internal void Attach(SignatureHelpController controller, Action caretMoved, IDisposable? typedText)
	{
		_typedText?.Dispose();
		_controller = controller;
		_caretMoved = caretMoved;
		_typedText = typedText;
	}

	/// <summary>Closes the popup for the controller's own dismissal, which must not dismiss the controller again.</summary>
	internal void HideForController() => Popup.Hide();

	private void HandleCaretPositionChanged(object? sender, EventArgs e)
	{
		if (Popup.IsOpen)
			_caretMoved?.Invoke();
	}

	private void HandlePopupDismissed(object? sender, EventArgs e)
	{
		_controller?.Dismiss();
		Dismissed?.Invoke(this, EventArgs.Empty);
	}

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
