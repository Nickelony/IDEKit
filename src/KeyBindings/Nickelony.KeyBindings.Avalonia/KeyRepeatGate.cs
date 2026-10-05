using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Nickelony.KeyBindings.Avalonia;

/// <summary>
/// Admits the press of a key once while the key is held, because Avalonia reports no auto-repeat flag on <see cref="KeyEventArgs"/>.
/// </summary>
/// <remarks>
/// <para>
/// The gate records each admitted key and forgets it when the key is released. It observes <see cref="InputElement.KeyUpEvent"/> on the tunnel stage
/// of the scope, handled events included, so a release that another handler consumed still clears the key. It forgets every key when focus leaves
/// the scope and when the window that contains the scope is deactivated, because a release that goes to another element or window never reaches
/// the scope.
/// </para>
/// <para>
/// The gate is not thread-safe: it observes input events and must be created, used and disposed on the UI thread.
/// </para>
/// </remarks>
public sealed class KeyRepeatGate : IDisposable
{
	private readonly HashSet<Key> _heldKeys = [];
	private readonly InputElement _scope;

	private Window? _window;
	private bool _isDisposed;

	/// <summary>Creates a gate that observes the key releases and the focus loss of <paramref name="scope"/>.</summary>
	/// <param name="scope">The element whose key releases the gate observes, such as the window that routes every key press to the adapter.</param>
	/// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
	public KeyRepeatGate(InputElement scope)
	{
		ArgumentNullException.ThrowIfNull(scope);

		_scope = scope;

		_scope.AddHandler(routedEvent: InputElement.KeyUpEvent, handler: HandleKeyUp, routes: RoutingStrategies.Tunnel, handledEventsToo: true);

		_scope.AddHandler(
			routedEvent: InputElement.LostFocusEvent,
			handler: HandleLostFocus,
			routes: RoutingStrategies.Bubble,
			handledEventsToo: true
		);

		_scope.AttachedToVisualTree += HandleScopeAttachedToVisualTree;
		_scope.DetachedFromVisualTree += HandleScopeDetachedFromVisualTree;
		ObserveWindow(TopLevel.GetTopLevel(_scope) as Window);
	}

	/// <summary>Records a key press and tells whether the key was up before it.</summary>
	/// <param name="e">The key-down event to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when the key was not held, so the press is new; <see langword="false"/> when the key is still held
	/// from an earlier press, so the event is a repeat.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">
	/// The gate is disposed, because it observes no releases then and would hold every key forever.
	/// </exception>
	public bool Admit(KeyEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(e);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _heldKeys.Add(e.Key);
	}

	/// <summary>Forgets every held key, so the next press of each key is admitted.</summary>
	public void Reset() => _heldKeys.Clear();

	/// <summary>Detaches every handler from the scope and its window, and forgets the held keys.</summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		_scope.RemoveHandler(InputElement.KeyUpEvent, HandleKeyUp);
		_scope.RemoveHandler(InputElement.LostFocusEvent, HandleLostFocus);

		_scope.AttachedToVisualTree -= HandleScopeAttachedToVisualTree;
		_scope.DetachedFromVisualTree -= HandleScopeDetachedFromVisualTree;
		ObserveWindow(null);
		Reset();
	}

	private void HandleKeyUp(object? sender, KeyEventArgs e) => _heldKeys.Remove(e.Key);

	private void HandleLostFocus(object? sender, RoutedEventArgs e)
	{
		// Focus that moves between two elements of the scope keeps the key held, so only focus that leaves the scope resets the gate.
		IInputElement? focused = TopLevel.GetTopLevel(_scope)?.FocusManager?.GetFocusedElement();

		if (focused is Visual visual && (ReferenceEquals(visual, _scope) || _scope.IsVisualAncestorOf(visual)))
			return;

		Reset();
	}

	private void HandleScopeAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
		ObserveWindow(TopLevel.GetTopLevel(_scope) as Window);

	private void HandleScopeDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
	{
		ObserveWindow(null);
		Reset();
	}

	private void HandleWindowDeactivated(object? sender, EventArgs e) => Reset();

	private void ObserveWindow(Window? window)
	{
		if (ReferenceEquals(_window, window))
			return;

		if (_window is not null)
			_window.Deactivated -= HandleWindowDeactivated;

		_window = window;

		if (_window is not null)
			_window.Deactivated += HandleWindowDeactivated;
	}
}
