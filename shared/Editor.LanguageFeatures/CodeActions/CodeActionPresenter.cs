using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using System.Windows;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>
/// Presents the actions of a <see cref="CodeActionController"/> in a text area: an indicator margin and a themed drop-down menu anchored
/// at the indicator or at a host position.
/// </summary>
/// <remarks>
/// <para>
/// The presenter must be created and used on the thread that owns the text area. It owns the <see cref="Margin"/> and the menu,
/// and it does not own the controller: disposing the presenter closes the menu and detaches from the controller, and the host disposes the controller
/// and removes the margin from the text area when the editor is torn down.
/// </para>
/// <para>
/// A menu presents the action set that was published when it opened. When the controller replaces or clears that set, the presenter closes the menu,
/// so its items always belong to the current set; a menu that a <see cref="CodeActionController.Changed"/> handler reopened with the new set stays.
/// The menu takes keyboard focus while it is open and gives it back to the element that had it, so opening it from the margin never moves the caret.
/// </para>
/// </remarks>
public sealed class CodeActionPresenter : IDisposable
{
	private readonly TextArea _textArea;

	[SuppressMessage(
		"Performance",
		"CA1859:Use concrete types when possible for improved performance",
		Justification = "Shared presenter code holds the neutral dispatcher interface, never the engine adapter."
	)]
	private readonly IUiDispatcher _dispatcher;

	private readonly CodeActionController _controller;
	private readonly CodeActionMenuOptions _menuOptions;
	private readonly CodeActionMenuPresenter _menu;

	// The action set the open menu was created for; compared by reference, because every publish creates a new snapshot.
	private IReadOnlyList<CodeAction>? _shownActions;
	private bool _isDisposed;

	/// <param name="textArea">The text area the margin and the menu belong to.</param>
	/// <param name="controller">The controller whose actions the presenter shows.</param>
	/// <param name="presentation">
	/// The menu theme and menu presentation options, or <see langword="null"/> for <see cref="CodeActionPresentationOptions.Default"/>.
	/// </param>
	/// <param name="hooks">The optional hooks that restyle the menu and its items, or <see langword="null"/> for the plain themed menu.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="controller"/> is <see langword="null"/>, or the theme or the menu options
	/// of <paramref name="presentation"/> are <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	public CodeActionPresenter(
		TextArea textArea,
		CodeActionController controller,
		CodeActionPresentationOptions? presentation = null,
		CodeActionMenuHooks? hooks = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(controller);

		presentation ??= CodeActionPresentationOptions.Default;

		// The presenter touches the text area's visual tree and hosts the menu directly, so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		_menu = new CodeActionMenuPresenter(textArea, presentation, configureMenu: hooks?.ConfigureMenu, configureMenuItem: hooks?.ConfigureMenuItem);

		_textArea = textArea;
		_dispatcher = UiDispatcher.From(textArea.Dispatcher);
		_controller = controller;
		_menuOptions = presentation.MenuOptions;

		Margin = new CodeActionMargin(controller, presenter: this);

		controller.Changed += HandleControllerChanged;
	}

	/// <summary>The margin that renders the indicator for the controller's current context.</summary>
	/// <remarks>
	/// The margin shows the indicator when it is added to the text area's left margins; it repaints itself when the controller reports a change.
	/// Disposing the presenter does not remove the margin from the text area, so the host removes it when the editor is torn down.
	/// </remarks>
	public CodeActionMargin Margin { get; }

	/// <summary>Indicates whether a menu created by this presenter is open.</summary>
	public bool IsMenuOpen => _menu.IsOpen;

	/// <summary>The colors of the actions menu; they start as <see cref="CodeActionPresentationOptions.MenuTheme"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns the text area.</exception>
	/// <remarks>
	/// A host that switches themes assigns the new one: the next menu takes it, and a menu that is open keeps its colors.
	/// </remarks>
	public CodeActionMenuTheme MenuTheme
	{
		get => _menu.Theme;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			_dispatcher.VerifyAccess();

			_menu.Theme = value;
		}
	}

	/// <summary>Opens the actions menu for the current context, anchored at the indicator line, when actions are available.</summary>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no actions are available or the editor
	/// is not connected to a presentation source.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the text area's thread.</exception>
	public bool TryOpenMenu()
	{
		if (_isDisposed)
			return false;

		_textArea.Dispatcher.VerifyAccess();

		// The anchor is computed from the text-area visual tree, so the connectivity refusal must run
		// before it: a disconnected editor has no presentation source to translate against.
		if (!CodeActionMenuSeam.CanOpenMenu(_textArea))
			return false;

		// Without actions there is nothing to open, and GetIndicatorAnchor walks the visual tree, so the
		// no-actions refusal runs before the anchor is computed.
		if (!_controller.HasActions)
			return false;

		int indicatorLine = GetIndicatorLineNumber();

		return TryOpenMenu(indicatorLine, GetIndicatorAnchor(indicatorLine));
	}

	/// <summary>
	/// Opens the actions menu for the specified document line, anchored at the specified position, when the current context's actions are available
	/// for that line.
	/// </summary>
	/// <param name="lineNumber">The one-based document line the action context belongs to.</param>
	/// <param name="anchor">The menu's top-left position in the text area's coordinate space.</param>
	/// <returns>
	/// <see langword="true"/> when the menu opened; otherwise, <see langword="false"/> because no actions are available for the line or the editor
	/// is not connected to a presentation source.
	/// </returns>
	/// <exception cref="InvalidOperationException">The caller is not on the text area's thread.</exception>
	/// <remarks>
	/// This is the anchoring entry point for a host-owned indicator. A host that draws its own code-action indicator, or handles its own trigger
	/// gesture with a known anchor, opens the library menu at its own click position instead of the caret. It does so by passing
	/// the line the indicator belongs to and a position in the text area's coordinate space. The menu itself stays library-owned. Its chrome and item
	/// styling are customized through <see cref="CodeActionMenuHooks"/>, while its anchoring, focus handling, and lifetime stay with the presenter
	/// so the presentation state remains consistent.
	/// </remarks>
	public bool TryOpenMenu(int lineNumber, Point anchor)
	{
		if (_isDisposed)
			return false;

		_textArea.Dispatcher.VerifyAccess();

		if (!_controller.HasActions || lineNumber != GetIndicatorLineNumber() || !CodeActionMenuSeam.CanOpenMenu(_textArea))
			return false;

		IReadOnlyList<CodeAction> actions = _controller.Actions;

		bool opened = _menu.TryOpenMenu(items: actions, placementTarget: _textArea, anchor, invoked: InvokeAction);

		_shownActions = opened ? actions : null;

		// A host hook that runs while the menu opens can replace or clear the controller's set before the menu is tracked, which the change
		// handler cannot see, so the opened menu is checked against the current set once more.
		if (opened && !ReferenceEquals(actions, _controller.Actions))
		{
			CloseMenuWhenStale();
			return false;
		}

		return opened;
	}

	/// <summary>Closes the actions menu when it is open.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the text area's thread.</exception>
	public void CloseMenu()
	{
		if (_isDisposed)
			return;

		_textArea.Dispatcher.VerifyAccess();

		_menu.Close();
	}

	/// <summary>Closes the menu and detaches from the controller.</summary>
	/// <remarks>
	/// The controller, the margin, and the text area stay as they are. A call after disposal does nothing, and every open member
	/// then returns <see langword="false"/>.
	/// </remarks>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_shownActions = null;

		_controller.Changed -= HandleControllerChanged;
		_menu.Dispose();
	}

	/// <summary>Opens the actions menu for a margin click on the indicator's line.</summary>
	/// <param name="lineNumber">The one-based document line the margin click resolved to.</param>
	/// <param name="marginPoint">The click position in the margin's coordinate space.</param>
	/// <returns><see langword="true"/> when the menu opened; otherwise, <see langword="false"/>.</returns>
	internal bool TryOpenMenuFromMargin(int lineNumber, Point marginPoint)
	{
		// The margin point is translated against the visual tree, so the connectivity refusal must run
		// before the translation.
		if (!CodeActionMenuSeam.CanOpenMenu(_textArea))
			return false;

		// A failed translation (the margin is not connected to the text area) means there is no anchor to
		// open the menu at, so the click is refused instead of anchoring at the text-view top.
		if (!CodeActionMenuSeam.TryTranslatePoint(source: Margin, point: marginPoint, target: _textArea, translated: out Point areaPoint))
			return false;

		return TryOpenMenu(
			lineNumber,
			new Point(CodeActionMenuSeam.GetTextViewOrigin(_textArea).X + _menuOptions.AnchorXOffset, areaPoint.Y + _menuOptions.MarginAnchorYOffset)
		);
	}

	private void InvokeAction(CodeAction item) => _ = _controller.ExecuteActionAsync(item);

	// The controller raises its change notification on the text area's thread, except for a disposal that another thread runs, which posts the check.
	private void HandleControllerChanged(object? sender, EventArgs e)
	{
		if (_isDisposed)
			return;

		if (_dispatcher.CheckAccess())
			CloseMenuWhenStale();
		else
			_dispatcher.Post(CloseMenuWhenStale);
	}

	// The menu presents the set it opened with; any other published set, including the empty one, makes its items stale.
	private void CloseMenuWhenStale()
	{
		if (_isDisposed)
			return;

		if (_menu.IsOpen && !ReferenceEquals(_shownActions, _controller.Actions))
			_menu.Close();

		// A closed menu presents no set, whether it closed by itself or this call closed it, so the presenter releases the set and its payloads.
		if (!_menu.IsOpen)
			_shownActions = null;
	}

	private int GetIndicatorLineNumber()
	{
		IReadOnlyList<int> lines = _controller.GetMarkedLineNumbers();
		return lines.Count > 0 ? lines[0] : 0;
	}

	private Point GetIndicatorAnchor(int indicatorLine)
	{
		Point origin = CodeActionMenuSeam.GetTextViewOrigin(_textArea);
		double y = origin.Y;
		TextView textView = _textArea.TextView;

		if (indicatorLine > 0 && textView.VisualLinesValid && textView.GetVisualLine(indicatorLine) is VisualLine line)
			y += line.VisualTop - textView.VerticalOffset;

		return new Point(origin.X + _menuOptions.AnchorXOffset, y + _menuOptions.CaretAnchorYOffset);
	}
}
