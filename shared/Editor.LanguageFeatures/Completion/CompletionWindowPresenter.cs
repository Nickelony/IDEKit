using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using ClosedHandler = System.EventHandler<System.EventArgs>;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using ClosedHandler = System.EventHandler;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>
/// Creates, shows, and closes the completion windows of a single text area with the default theme, so at most one window is tracked at a time.
/// </summary>
/// <remarks>
/// Creating a new window closes the window created or tracked by an earlier call, and a window is tracked only while it is shown.
/// The presenter is owned by the <see cref="CompletionPresenter"/> of its text area, which creates and shows windows through it, so every shown
/// window went through the presenter's configuration pipeline (sizing, tooltip theme, window hook, and non-activation policy)
/// and the reported presentation state stays truthful. A host observes the tracked window through <see cref="IsWindowOpen"/>,
/// learns about closures through <see cref="WindowClosed"/>, and can close the window with <see cref="Close"/>; window creation and showing
/// are not part of the public surface. The presenter must be created and used on the thread that owns <see cref="TextArea"/>,
/// because it creates and shows the editor's completion windows and subscribes to their events directly. It holds only the window
/// it created or tracks plus the closed-handler subscription it installs; <see cref="Close"/> is safe to call, and the presenter remains usable
/// before and after the window closes.
/// </remarks>
public sealed class CompletionWindowPresenter
{
	private readonly TextArea _textArea;
	private readonly Action<CompletionWindow> _showWindow;

	private ClosedHandler? _closedHandler;
	private CompletionWindow? _createdWindow;

	/// <param name="textArea">The text area completion windows are created for.</param>
	/// <param name="defaultTheme">The default theme applied to created windows.</param>
	/// <param name="showWindow">
	/// Shows a created window, or <see langword="null"/> to show it with the editor's own <c>Show</c>. A test replaces
	/// it to force the show-failure rollback, which the engine cannot reproduce through its public surface: a shown popup reopens instead of throwing
	/// when it is shown again.
	/// </param>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	internal CompletionWindowPresenter(TextArea textArea, CompletionWindowTheme defaultTheme, Action<CompletionWindow>? showWindow = null)
	{
		// The presenter creates and shows the text area's completion windows and subscribes to their events
		// directly, so creating it off the text area's thread is a caller error.
		textArea.Dispatcher.VerifyAccess();

		_textArea = textArea;
		Theme = defaultTheme;
		_showWindow = showWindow ?? (static window => window.Show());
	}

	/// <summary>
	/// Raised when the shown window closes, whatever the cause - the window itself, a replacement, or <see cref="Close"/> -
	/// after it stops being tracked, exactly once per shown window.
	/// </summary>
	/// <remarks>
	/// A window that was created but never shown does not raise the event. A throwing subscriber propagates to whoever closed the window,
	/// exactly like a throwing host callback on the close path.
	/// </remarks>
	public event EventHandler? WindowClosed;

	/// <summary>The currently tracked completion window, or <see langword="null"/> when none is tracked.</summary>
	/// <remarks>
	/// A window is tracked only while it is shown; a created-but-never-shown window, a closed window, and the window <see cref="Close"/>
	/// closes are not reported. The owning <see cref="CompletionPresenter"/>'s disposal closes the tracked window, so this is <see langword="null"/>
	/// afterwards as well. This is the engine-typed accessor used internally; a host observes the neutral <see cref="IsWindowOpen"/>.
	/// </remarks>
	internal CompletionWindow? ActiveWindow { get; private set; }

	/// <summary>Indicates whether a completion window is currently tracked; a window is tracked only after a successful show.</summary>
	public bool IsWindowOpen => ActiveWindow is not null;

	/// <summary>The default chrome of the next window that <see cref="Create"/> makes; a window that is open keeps its chrome.</summary>
	internal CompletionWindowTheme Theme { get; set; }

	/// <summary>Creates a new completion window with the given maximum height, closing the window created or tracked by an earlier call.</summary>
	/// <param name="maxHeight">
	/// The maximum window height; the window sizes to its content up to this value. The completion presenter
	/// passes <see cref="CompletionWindowOptions.MaxHeight"/>.
	/// </param>
	/// <returns>The created window, which becomes the tracked <see cref="ActiveWindow"/> once it is shown.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxHeight"/> is negative or not finite.</exception>
	/// <remarks>
	/// Any tracked window is closed by the replacement and <see cref="WindowClosed"/> is raised for it. The created window is not tracked
	/// until it is shown through <see cref="Show"/>. The default chrome goes on the element that draws the frame of the engine's completion window
	/// (<see cref="CompletionSeam.CreateWindow"/> names it). The caller sizes that element before showing the window.
	/// </remarks>
	internal CompletionWindow Create(double maxHeight)
	{
		if (!NumericValidation.IsFiniteNonNegative(maxHeight))
			throw new ArgumentOutOfRangeException(nameof(maxHeight), maxHeight, "The maximum window height must be finite and non-negative.");

		// A window that was created but never shown is not tracked, so it must be closed here; otherwise, a
		// repeated Create without Show would leave the first window untracked and never closed.
		Close();

		return _createdWindow = CompletionSeam.CreateWindow(_textArea, Theme, maxHeight);
	}

	/// <summary>Shows the completion window created by <see cref="Create"/>, if one exists, and tracks it until it closes.</summary>
	/// <remarks>
	/// A show failure leaves the window untracked; <see cref="Close"/> can still close it. Only the window created by the most recent
	/// <see cref="Create"/> call is shown, and no window can be tracked when this method runs: <see cref="Create"/> closes any window created
	/// or tracked by an earlier call before it creates its own.
	/// </remarks>
	internal void Show()
	{
		if (_createdWindow is not CompletionWindow window)
			return;

		TrackWindow(window);

		try
		{
			_showWindow(window);
			_createdWindow = null;
		}
		catch
		{
			// A failed show must not leave the window tracked: the event would never be raised for a window
			// that never became visible.
			UntrackWindow(window);
			throw;
		}
	}

	/// <summary>Closes the currently tracked completion window, or the window created by <see cref="Create"/> when it has not been shown.</summary>
	public void Close()
	{
		CompletionWindow? window = ActiveWindow ?? _createdWindow;

		if (window is null)
			return;

		bool wasTracked = ReferenceEquals(window, ActiveWindow);

		UntrackWindow(window);
		_createdWindow = null;
		window.Close();

		if (wasTracked)
			WindowClosed?.Invoke(this, EventArgs.Empty);
	}

	private void TrackWindow(CompletionWindow completionWindow)
	{
		ActiveWindow = completionWindow;

		_closedHandler = (sender, e) =>
		{
			UntrackWindow(completionWindow);
			WindowClosed?.Invoke(this, EventArgs.Empty);
		};

		completionWindow.Closed += _closedHandler;
	}

	private void UntrackWindow(CompletionWindow? completionWindow)
	{
		if (completionWindow is null || _closedHandler is null || !ReferenceEquals(completionWindow, ActiveWindow))
			return;

		completionWindow.Closed -= _closedHandler;
		_closedHandler = null;
		ActiveWindow = null;
	}
}
