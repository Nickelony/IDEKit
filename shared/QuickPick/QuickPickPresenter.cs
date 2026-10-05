#if AVALONIAQUICKPICK
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Brush = Avalonia.Media.IBrush;
using PlacementTarget = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;
using PlacementTarget = System.Windows.UIElement;
#endif

#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia;

#else

namespace Nickelony.QuickPick.Wpf;

#endif

/// <summary>
/// Shows the state of a <see cref="QuickPickController"/> as a window near the top of a host element: an input over a list of rows.
/// </summary>
/// <remarks>
/// <para>
/// The window opens when the controller's session opens and closes when it ends. Typing sets the text of the controller, and the keys
/// act on it: Up and Down move the active row, Page Up and Page Down move it by <see cref="QuickPickTheme.MaxVisibleRows"/> rows without wrapping,
/// Enter accepts the active item, and Esc ends the session. A click on a row accepts its item. The session also ends when the window loses
/// the keyboard focus, when the window of the host deactivates, and when the toolkit closes the popup, and the focus returns to the element that held
/// it before the window opened.
/// </para>
/// <para>
/// Disposing the presenter closes its window and detaches it from the controller; it does not end the controller's session.
/// The presenter must be created and used on the thread of its host element.
/// </para>
/// <para>
/// The accessible name of the input is the placeholder of the active mode, and the accessible name of a row is its label, description and trailing
/// text separated by commas.
/// </para>
/// </remarks>
public sealed class QuickPickPresenter : IDisposable
{
	private const double InputPlaceholderLeftMargin = 9.0;
	private const double HostMargin = 16.0;

	private readonly PlacementTarget _host;
	private readonly QuickPickController _controller;
	private readonly QuickPickPresenterHooks _hooks;

	private QuickPickTheme _theme;

	private IDisposable? _inputSubscription;
	private IDisposable? _dismissSubscription;

	private IReadOnlyList<QuickPickRow>? _shownRows;
	private object? _focusBeforeOpen;

	private bool _isSyncingText;
	private bool _isDisposed;

	/// <param name="host">The element the window is placed against, usually the content of the host window.</param>
	/// <param name="controller">The controller whose state the window shows.</param>
	/// <param name="theme">The look of the window, or <see langword="null"/> to use <see cref="QuickPickTheme.Default"/>.</param>
	/// <param name="hooks">The hooks that change what a row draws, or <see langword="null"/> for the default rows.</param>
	/// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="controller"/> is <see langword="null"/>.</exception>
	public QuickPickPresenter(
		PlacementTarget host,
		QuickPickController controller,
		QuickPickTheme? theme = null,
		QuickPickPresenterHooks? hooks = null
	)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentNullException.ThrowIfNull(controller);

		_host = host;
		_controller = controller;
		_theme = theme ?? QuickPickTheme.Default;
		_hooks = hooks ?? new QuickPickPresenterHooks();
		List = QuickPickSeam.CreateList(row => QuickPickRowEmitter.Emit(row, _theme, _hooks.ResolveIcon));

		Placeholder.IsHitTestVisible = false;
		Placeholder.Opacity = 0.55;
		Placeholder.VerticalAlignment = VerticalAlignment.Center;
		Placeholder.Margin = new Thickness(left: InputPlaceholderLeftMargin, top: 0.0, right: 0.0, bottom: 0.0);

		BusyBar.IsIndeterminate = true;
		BusyBar.Height = 3.0;
		BusyBar.BorderThickness = new Thickness(0.0);

		var inputLayer = new Grid();

		inputLayer.Children.Add(Input);
		inputLayer.Children.Add(Placeholder);

		var content = new StackPanel();

		content.Children.Add(inputLayer);
		content.Children.Add(BusyBar);
		content.Children.Add(List);

		Frame.Child = content;
		Popup = QuickPickSeam.CreatePopup(host, Frame);

		ApplyTheme();

		QuickPickSeam.SetVisible(BusyBar, false);
		QuickPickSeam.SetVisible(List, false);

		_controller.Changed += HandleControllerChanged;

		if (_controller.State.IsOpen)
			Synchronize();
	}

	/// <summary>Whether the window is open.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>The look of the window; an assigned theme applies to the open window at once.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public QuickPickTheme Theme
	{
		get => _theme;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			_theme = value;
			ApplyTheme();

			// The rows were drawn with the earlier theme, so they are drawn again.
			_shownRows = null;

			if (IsOpen)
				Synchronize();
		}
	}

	/// <summary>The popup that holds the window, which a test reads for its placement and its logical parent.</summary>
	internal Popup Popup { get; }

	/// <summary>The input the user types into.</summary>
	internal TextBox Input { get; } = new();

	/// <summary>The list of rows.</summary>
	internal ListBox List { get; }

	/// <summary>The bar that shows a slow query.</summary>
	internal ProgressBar BusyBar { get; } = new();

	/// <summary>The text that hints at the mode while the input is empty.</summary>
	internal TextBlock Placeholder { get; } = new();

	/// <summary>The frame that draws the border and the background of the window.</summary>
	internal Border Frame { get; } = new();

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_controller.Changed -= HandleControllerChanged;

		Close();
		QuickPickSeam.ReleasePopup(Popup);
	}

	private void HandleControllerChanged(object? sender, EventArgs e) => Synchronize();

	private void Synchronize()
	{
		if (_isDisposed)
			return;

		QuickPickPresentationState state = _controller.State;

		if (!state.IsOpen)
		{
			Close();
			return;
		}

		if (!IsOpen && !Open())
			return;

		SynchronizeText(state);

		if (!ReferenceEquals(_shownRows, state.Rows))
		{
			_shownRows = state.Rows;
			List.ItemsSource = state.Rows;
		}

		QuickPickSeam.SetVisible(List, state.Rows.Count > 0);
		QuickPickSeam.SetVisible(BusyBar, state.IsBusy);

		List.SelectedIndex = state.ActiveRowIndex;

		if (state.ActiveRowIndex >= 0)
			QuickPickSeam.ScrollIntoView(List, state.ActiveRowIndex);
	}

	private void SynchronizeText(QuickPickPresentationState state)
	{
		Placeholder.Text = state.Placeholder;
		QuickPickSeam.SetAccessibleName(Input, state.Placeholder);
		QuickPickSeam.SetVisible(Placeholder, state.Text.Length == 0);

		// Typing reaches the controller through the input, so only a text the controller set itself, such as the prefix of a mode that
		// the user picked from the list of modes, differs here.
		if ((Input.Text ?? string.Empty) == state.Text)
			return;

		_isSyncingText = true;

		try
		{
			Input.Text = state.Text;
			Input.CaretIndex = state.Text.Length;
		}
		finally
		{
			_isSyncingText = false;
		}
	}

	private bool Open()
	{
		_focusBeforeOpen = QuickPickSeam.GetFocusedElement(_host);

		// A host that is narrower than the window gets a window that fits, and a host that has not been measured gets the full width.
		double room = QuickPickSeam.GetWidth(_host) - (2.0 * HostMargin);

		Frame.Width = room > 0.0 ? Math.Min(_theme.Width, room) : _theme.Width;
		Popup.IsOpen = true;

		// A host that is not in a window refuses to open the popup, and the session then has nothing to show.
		if (!Popup.IsOpen)
		{
			_focusBeforeOpen = null;
			_controller.Dismiss();
			return false;
		}

		IsOpen = true;

		_inputSubscription = QuickPickSeam.SubscribeInput(
			Input,
			List,
			keyDown: HandleKey,
			textChanged: HandleTextChanged,
			rowClicked: HandleRowClicked
		);

		_dismissSubscription = QuickPickSeam.SubscribeDismissal(_host, Popup, _controller.Dismiss);

		QuickPickSeam.FocusInput(Input);

		return true;
	}

	private void Close()
	{
		if (!IsOpen)
			return;

		// The subscriptions end first, so closing the popup raises nothing that dismisses the session again.
		IsOpen = false;

		_inputSubscription?.Dispose();
		_dismissSubscription?.Dispose();

		_inputSubscription = null;
		_dismissSubscription = null;

		Popup.IsOpen = false;
		List.ItemsSource = null;
		_shownRows = null;

		_isSyncingText = true;

		try
		{
			Input.Text = string.Empty;
		}
		finally
		{
			_isSyncingText = false;
		}

		QuickPickSeam.RestoreFocus(_focusBeforeOpen, _host);
		_focusBeforeOpen = null;
	}

	private bool HandleKey(Key key)
	{
		int page = _theme.MaxVisibleRows;

		switch (key)
		{
			case Key.Down:
				_controller.MoveActive(1);
				return true;

			case Key.Up:
				_controller.MoveActive(-1);
				return true;

			case Key.PageDown:
				_controller.MoveActive(page, wrap: false);
				return true;

			case Key.PageUp:
				_controller.MoveActive(-page, wrap: false);
				return true;

			case Key.Enter:
				// The controller contains the failures of the provider, so the task needs no observer.
				_ = _controller.AcceptAsync();
				return true;

			case Key.Escape:
				_controller.Dismiss();
				return true;

			default:
				return false;
		}
	}

	private void HandleTextChanged()
	{
		if (!_isSyncingText)
			_controller.SetText(Input.Text ?? string.Empty);
	}

	private void HandleRowClicked(int rowIndex)
	{
		IReadOnlyList<QuickPickRow> rows = _controller.State.Rows;

		if (rowIndex < 0 || rowIndex >= rows.Count || !rows[rowIndex].IsSelectable)
			return;

		_controller.SetActiveRow(rowIndex);
		_ = _controller.AcceptAsync();
	}

	private void ApplyTheme()
	{
		Frame.Background = _theme.Background;
		Frame.BorderBrush = _theme.BorderBrush;
		Frame.BorderThickness = new Thickness(_theme.BorderThickness);
		Frame.CornerRadius = new CornerRadius(_theme.CornerRadius);
		Frame.Padding = _theme.Padding;

		Input.Background = _theme.Background;
		Input.Foreground = _theme.Foreground;
		Input.CaretBrush = _theme.Foreground;
		Input.BorderBrush = _theme.BorderBrush;
		Input.Padding = new Thickness(left: 6.0, top: 4.0, right: 6.0, bottom: 4.0);
		Placeholder.Foreground = _theme.Foreground;
		BusyBar.Foreground = _theme.ActiveBackground;
		BusyBar.Background = _theme.Background;

		List.Foreground = _theme.Foreground;
		List.MaxHeight = _theme.MaxVisibleRows * _theme.RowHeight;

		QuickPickSeam.SetItemTemplate(List, _theme.ItemTemplate);
		QuickPickSeam.StyleActiveRow(List, _theme.ActiveBackground, _theme.ActiveForeground);
		QuickPickSeam.SetTopOffset(Popup, _theme.TopOffset);
	}
}
