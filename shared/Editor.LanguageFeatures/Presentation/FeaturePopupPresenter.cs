using Nickelony.IDEKit.Core.Validation;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
using PopupContent = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Brush = System.Windows.Media.Brush;
using PopupContent = System.Windows.UIElement;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures;

#endif

/// <summary>
/// Shows the content of one feature in a framed popup below a document offset of a text area, and closes it by the rules every feature popup shares.
/// </summary>
/// <remarks>
/// <para>
/// The popup closes on its own on Esc, when the text area loses keyboard focus, when its window deactivates, when the text
/// area replaces its document, when the toolkit closes it, and, when the presenter is created with <c>closesOnTextChange</c>, on any change
/// of the document's text, typed or not. Each such close raises <see cref="Dismissed"/>, and <see cref="Hide"/> raises nothing. The close handlers
/// are subscribed only while the popup is open.
/// </para>
/// <para>
/// One popup serves every show, so a show while the popup is open replaces its content and its anchor at once. The anchor is a text anchor,
/// so it follows an edit before it, and the popup moves with the text when the view scrolls. A show while the text area's window is inactive,
/// or while the text area has no document, hides the popup instead, because a topmost popup of an inactive window would cover the application
/// in the foreground. The presenter must be used on the text area's thread.
/// </para>
/// </remarks>
internal sealed class FeaturePopupPresenter : IDisposable
{
	private readonly TextArea _textArea;
	private readonly bool _closesOnTextChange;
	private readonly Border _frame = new();

	private IDisposable? _closing;
	private TextDocument? _document;
	private TextAnchor? _anchor;
	private bool _isDisposed;

	/// <param name="textArea">The text area the popup is placed against and whose events close it.</param>
	/// <param name="closesOnTextChange">
	/// Whether a change of the document's text closes the popup, as it does for a hover, whose text moved, and not for a signature,
	/// whose call goes on.
	/// </param>
	internal FeaturePopupPresenter(TextArea textArea, bool closesOnTextChange)
	{
		_textArea = textArea;
		_closesOnTextChange = closesOnTextChange;
		Popup = PopupSeam.CreatePopup(textArea, _frame);

		// A popup the toolkit closes by itself counts as a close on its own.
		Popup.Closed += (_, _) => Close(raiseDismissed: true);
	}

	/// <summary>Raised when the popup closed on its own, and not through <see cref="Hide"/>.</summary>
	internal event EventHandler? Dismissed;

	/// <summary>Whether the popup is open, from the show that opened it until it closes.</summary>
	internal bool IsOpen { get; private set; }

	/// <summary>The toolkit popup, which a test reads for its placement and its logical parent.</summary>
	internal Popup Popup { get; }

	/// <summary>Returns the padding when every side is finite and non-negative; otherwise, throws.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A side of <paramref name="padding"/> is negative or not finite.</exception>
	internal static Thickness ValidatePadding(Thickness padding, string parameterName)
	{
		bool isValid =
			NumericValidation.IsFiniteNonNegative(padding.Left)
			&& NumericValidation.IsFiniteNonNegative(padding.Top)
			&& NumericValidation.IsFiniteNonNegative(padding.Right)
			&& NumericValidation.IsFiniteNonNegative(padding.Bottom);

		return isValid
			? padding
			: throw new ArgumentOutOfRangeException(parameterName, padding, "Every side of the padding must be finite and non-negative.");
	}

	/// <summary>Applies the chrome of a theme to the frame, including the frame of an open popup.</summary>
	internal void ApplyTheme(Brush background, Brush borderBrush, double borderThickness, double cornerRadius, Thickness padding, double maxWidth)
	{
		_frame.Background = background;
		_frame.BorderBrush = borderBrush;
		_frame.BorderThickness = new Thickness(borderThickness);
		_frame.CornerRadius = new CornerRadius(cornerRadius);
		_frame.Padding = padding;
		_frame.MaxWidth = maxWidth;
	}

	/// <summary>Reports whether a show would open or update the popup, so a caller builds content only for a show that can succeed.</summary>
	/// <returns>
	/// <see langword="false"/> when the presenter is disposed, the text area has no document,
	/// or its window is inactive; otherwise, <see langword="true"/>.
	/// </returns>
	internal bool CanShow() => !_isDisposed && _textArea.Document is not null && !PopupSeam.IsWindowInactive(_textArea);

	/// <summary>Shows the content below the line of a document offset, which is clamped to the document.</summary>
	/// <returns>
	/// <see langword="true"/> when the popup shows the content; <see langword="false"/> when <see cref="CanShow"/> refused, which hides the popup,
	/// or when the toolkit refused to open it.
	/// </returns>
	internal bool Show(PopupContent content, int anchorOffset)
	{
		if (!CanShow())
		{
			Hide();
			return false;
		}

		TextDocument document = _textArea.Document!;

		_frame.Child = content;
		_anchor = document.CreateAnchor(Math.Clamp(anchorOffset, 0, document.TextLength));
		PopupSeam.SetAnchor(Popup, AnchorBelow(document, _anchor.Offset));

		if (IsOpen)
			return true;

		// The popup opens before the presenter counts it open, so a toolkit that refuses to open it, and raises Closed while it does,
		// reports no dismissal of a popup that never showed.
		Popup.IsOpen = true;

		if (!Popup.IsOpen)
		{
			_frame.Child = null;
			_anchor = null;
			return false;
		}

		IsOpen = true;
		_closing = PopupSeam.SubscribeClosing(_textArea, CloseOnItsOwn);
		_document = document;
		_textArea.DocumentChanged += HandleDocumentReplaced;
		_textArea.TextView.ScrollOffsetChanged += HandleScrolled;

		if (_closesOnTextChange)
			document.TextChanged += HandleTextChanged;

		return true;
	}

	/// <summary>Closes the popup without raising <see cref="Dismissed"/>; a call while it is closed does nothing.</summary>
	internal void Hide() => Close(raiseDismissed: false);

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		Hide();
		_isDisposed = true;
		PopupSeam.ReleasePopup(Popup);
	}

	private void CloseOnItsOwn() => Close(raiseDismissed: true);

	private void Close(bool raiseDismissed)
	{
		if (!IsOpen)
			return;

		// The state is cleared before the toolkit closes the popup, because the close raises the popup's Closed event, which then finds it
		// closed and raises nothing a second time.
		IsOpen = false;
		_closing?.Dispose();
		_closing = null;
		_textArea.DocumentChanged -= HandleDocumentReplaced;
		_textArea.TextView.ScrollOffsetChanged -= HandleScrolled;

		if (_document is not null)
			_document.TextChanged -= HandleTextChanged;

		_document = null;
		_anchor = null;
		Popup.IsOpen = false;
		_frame.Child = null;

		if (raiseDismissed)
			Dismissed?.Invoke(this, EventArgs.Empty);
	}

	private void HandleDocumentReplaced(object? sender, EventArgs e) => CloseOnItsOwn();

	private void HandleTextChanged(object? sender, EventArgs e) => CloseOnItsOwn();

	private void HandleScrolled(object? sender, EventArgs e)
	{
		if (IsOpen && _anchor is not null && _textArea.Document is { } document)
			PopupSeam.SetAnchor(Popup, AnchorBelow(document, _anchor.Offset));
	}

	private Rect AnchorBelow(TextDocument document, int offset)
	{
		TextView textView = _textArea.TextView;
		var position = new TextViewPosition(document.GetLocation(Math.Clamp(offset, 0, document.TextLength)));
		Point visual = textView.GetVisualPosition(position, VisualYPosition.LineBottom) - textView.ScrollOffset;

#if AVALONIAEDIT
		Point anchor = textView.TranslatePoint(visual, _textArea) ?? visual;
#else
		Point anchor = textView.TranslatePoint(visual, _textArea);
#endif

		return new Rect(anchor, new Size(1, 1));
	}
}
