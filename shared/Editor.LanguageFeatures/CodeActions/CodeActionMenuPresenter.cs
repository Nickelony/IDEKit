using Nickelony.IDEKit.LanguageFeatures.CodeActions;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.Editing;
using FontWeights = Avalonia.Media.FontWeight;
using PlacementTarget = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FontWeights = System.Windows.FontWeights;
using PlacementTarget = System.Windows.UIElement;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>
/// Creates and manages the single code-action menu of a text area: standard menu items over the themed chrome, anchored at the requested point,
/// with focus restored to the editor after closing.
/// </summary>
/// <remarks>
/// <para>
/// The presenter must be created and used on the thread that owns the text area. Opening a menu replaces an open menu. The menu takes keyboard focus
/// while it is open - standard menu behavior - and the element that had focus before the menu opened is restored when it closes, so a margin click
/// that opened the menu never moves the caret and the editor keeps working after the menu closes.
/// </para>
/// <para>
/// The item containers are the standard <see cref="MenuItem"/> ones, so the selection highlight follows the platform menu highlight
/// like the package's other windows; the theme supplies the menu chrome colors only. The optional item hook receives every created container
/// before the menu opens, so a host can restyle an item without replacing the menu pipeline.
/// </para>
/// </remarks>
internal sealed class CodeActionMenuPresenter : IDisposable
{
	private readonly TextArea _textArea;
	private readonly double _maxHeight;
	private readonly Action<ContextMenu>? _configureMenu;
	private readonly Action<MenuItem, CodeAction>? _configureMenuItem;

	private ContextMenu? _menu;
	private IInputElement? _focusedBeforeOpen;
	private bool _isDisposed;

	/// <param name="textArea">The text area the menu belongs to and focus returns to.</param>
	/// <param name="presentation">The menu theme and menu presentation options.</param>
	/// <param name="configureMenu">The optional host hook invoked before a menu opens.</param>
	/// <param name="configureMenuItem">The optional host hook invoked for every created item.</param>
	internal CodeActionMenuPresenter(
		TextArea textArea,
		CodeActionPresentationOptions presentation,
		Action<ContextMenu>? configureMenu,
		Action<MenuItem, CodeAction>? configureMenuItem
	)
	{
		_textArea = textArea;
		Theme = presentation.MenuTheme;
		_maxHeight = presentation.MenuOptions.MaxHeight;
		_configureMenu = configureMenu;
		_configureMenuItem = configureMenuItem;
	}

	/// <summary>Indicates whether a menu created by this presenter is open.</summary>
	internal bool IsOpen => _menu is not null;

	/// <summary>The colors of the next menu; a menu that is open keeps the colors it opened with.</summary>
	internal CodeActionMenuTheme Theme { get; set; }

	/// <summary>Tries to open a menu for the supplied actions, replacing an open menu.</summary>
	/// <param name="items">The actions to present, in order.</param>
	/// <param name="placementTarget">The element the anchor is relative to.</param>
	/// <param name="anchor">The menu's top-left position, relative to <paramref name="placementTarget"/>.</param>
	/// <param name="invoked">The callback invoked with the action whose menu item was clicked.</param>
	/// <returns>
	/// <see langword="true"/> when the menu was created, shown, and is still open; otherwise, <see langword="false"/> when the presenter
	/// has been disposed or the host closed the menu while opening it.
	/// </returns>
	internal bool TryOpenMenu(IReadOnlyList<CodeAction> items, PlacementTarget placementTarget, Point anchor, Action<CodeAction> invoked)
	{
		if (_isDisposed)
			return false;

		Close();

		var menu = new ContextMenu
		{
			MaxHeight = _maxHeight,
			Background = Theme.Background,
			Foreground = Theme.Foreground,
			BorderBrush = Theme.BorderBrush,
		};

		CodeActionMenuSeam.ApplyPlacement(menu, placementTarget, anchor);

		for (int index = 0; index < items.Count; index++)
		{
			CodeAction item = items[index];

			// The header is a text element (not a string) so action titles are never parsed for
			// access keys, and a preferred action stands out without changing what the item does.
			var header = new TextBlock { Text = item.Title };

			if (item.IsPreferred)
				header.FontWeight = FontWeights.Bold;

			var menuItem = new MenuItem { Header = header };
			menuItem.Click += (_, _) => invoked(item);
			_configureMenuItem?.Invoke(menuItem, item);
			menu.Items.Add(menuItem);
		}

		_configureMenu?.Invoke(menu);

		menu.Closed += HandleMenuClosed;
		_focusedBeforeOpen = CodeActionMenuSeam.GetFocusedElement(_textArea);
		_menu = menu;

		try
		{
			CodeActionMenuSeam.Open(menu, placementTarget);
		}
		catch
		{
			// A failed show must not leave a menu tracked that never became visible.
			HandleMenuClosed(menu, EventArgs.Empty);
			throw;
		}

		// The host open callback can close the menu synchronously (for example on a placement failure), which
		// untracks it; reporting success only when the menu is still tracked keeps the caller's state truthful.
		if (!ReferenceEquals(_menu, menu))
			return false;

		CodeActionMenuSeam.FocusFirstItem(menu);

		return true;
	}

	/// <summary>Closes the open menu, if any.</summary>
	internal void Close()
	{
		ContextMenu? menu = _menu;

		if (menu is null)
			return;

		CodeActionMenuSeam.Close(menu);

		// Closing normally raises Closed, which untracks the menu; a menu that never became visible
		// (or was closed externally) is untracked defensively so it cannot leak into the next open.
		if (ReferenceEquals(_menu, menu))
			HandleMenuClosed(menu, EventArgs.Empty);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		Close();
	}

	private void HandleMenuClosed(object? sender, EventArgs e)
	{
		ContextMenu? menu = _menu;

		if (menu is null || (sender is ContextMenu closedMenu && !ReferenceEquals(closedMenu, menu)))
			return;

		menu.Closed -= HandleMenuClosed;
		_menu = null;
		RestoreFocus();
	}

	private void RestoreFocus()
	{
		IInputElement? previousFocus = _focusedBeforeOpen;
		_focusedBeforeOpen = null;

		if (!_textArea.IsVisible)
			return;

		if (!CodeActionMenuSeam.TryFocus(previousFocus))
			_textArea.Focus();
	}
}
