using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Covers the completion window presenter's lifecycle, closure reporting, and theme validation.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionWindowPresenterTests
{
	[TestMethod]
	public void Create_WithHeightCap_CreatesUntrackedWindowWithHeightCap()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			// A created but not yet shown window is not tracked.
			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);

			// The window is created with the shared completion window height cap; the controller sizes the
			// width from the measured items before showing the window, so the initial width is the engine's
			// metadata (deliberately not pinned here, because this package does not own it).
			Assert.AreEqual(300.0, GetFrameElement(window).MaxHeight);
			AssertHasNoWindowStyle(window);

			presenter.Show();

			Assert.IsTrue(presenter.IsWindowOpen);
			Assert.AreSame(window, presenter.ActiveWindow);

			presenter.Close();

			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);
		}
	}

	[TestMethod]
	public void Create_AppliesTheDocumentedWindowProperties()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor();

		var theme = new CompletionWindowTheme
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = Brushes.White,
			BorderThickness = 2.0,
		};

		var presenter = new CompletionWindowPresenter(editor.TextArea, theme);

		using (hostWindow)
		{
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			// The theme is the only source of these values; the border thickness comes from the theme.
			var frame = GetFrameElement(window);

			AssertIsNotResizable(window);

			Assert.AreEqual(new Thickness(2.0), frame.BorderThickness);
			Assert.AreSame(theme.Background, frame.Background);
			Assert.AreSame(theme.Foreground, frame.Foreground);
			Assert.AreSame(theme.BorderBrush, frame.BorderBrush);
		}
	}

	[TestMethod]
	public void Theme_PartialInitializer_KeepsTheDefaultBrushes()
	{
		// A partial initializer is valid: every brush already carries a non-null default, so the record does
		// not require the caller to repeat the binding's baseline chrome.
		var theme = new CompletionWindowTheme { BorderThickness = 2.0 };

		Assert.AreSame(CompletionWindowTheme.Default.BorderBrush, theme.BorderBrush);
		Assert.AreSame(CompletionWindowTheme.Default.Background, theme.Background);
		Assert.AreSame(CompletionWindowTheme.Default.Foreground, theme.Foreground);
		Assert.AreEqual(2.0, theme.BorderThickness);
	}

	[TestMethod]
	public void Theme_NegativeBorderThickness_ThrowsArgumentOutOfRangeException()
	{
		// The theme validates itself, so the failure site is the object initializer instead of the presenter
		// constructor.
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new CompletionWindowTheme
			{
				BorderBrush = Brushes.Gray,
				Background = Brushes.Black,
				Foreground = Brushes.White,
				BorderThickness = -1.0,
			}
		);

		Assert.AreEqual("BorderThickness", exception.ParamName);
	}

	[TestMethod]
	public void IsWindowOpen_TracksWindowUntilItClosesItself()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			Assert.IsFalse(presenter.IsWindowOpen);

			presenter.Show();

			Assert.IsTrue(presenter.IsWindowOpen);
			Assert.AreSame(window, presenter.ActiveWindow);
			Assert.IsTrue(IsShown(window));

			window.Close();

			// A window that closed itself is dropped from tracking.
			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);
		}
	}

	[TestMethod]
	public void Close_WithTrackedWindow_ClosesItAndClearsTracking()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			presenter.Close();

			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);
			Assert.IsFalse(IsShown(window));
		}
	}

	[TestMethod]
	public void Create_TwiceWithoutShow_ClosesTheUnshownWindowAndCreatesANewOne()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			CompletionWindow first = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			Func<bool> firstClosed = ObserveClosure(first);

			CompletionWindow second = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			// The created-but-unshown window is closed by the replacement instead of being stranded
			// untracked, and the replacement stays usable.
			Assert.AreNotSame(first, second);
			Assert.IsTrue(firstClosed(), "The created-but-unshown window must be closed by the replacement.");
			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);

			presenter.Show();

			Assert.IsTrue(presenter.IsWindowOpen);
			Assert.AreSame(second, presenter.ActiveWindow);

			presenter.Close();
		}
	}

	[TestMethod]
	public void Create_WhileWindowTracked_ClosesAndReportsItExactlyOnce()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int firstClosedCalls = 0;
			presenter.WindowClosed += (_, _) => firstClosedCalls++;
			CompletionWindow first = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			// Replacing the tracked window closes it and raises the event for it exactly once.
			CompletionWindow second = presenter.Create(250.0);

			Assert.IsFalse(IsShown(first));
			Assert.AreEqual(1, firstClosedCalls, "Replacing a window raises the window-closed event exactly once.");
			Assert.AreEqual(250.0, GetFrameElement(second).MaxHeight);
			Assert.IsFalse(presenter.IsWindowOpen);

			presenter.Show();
			presenter.Close();
		}
	}

	[TestMethod]
	public void Show_WindowClosesItself_ReportsOnce()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int closedCalls = 0;
			presenter.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			window.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(presenter.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_TrackedWindow_ReportsThroughTheEventExactlyOnce()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int closedCalls = 0;
			presenter.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			presenter.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(IsShown(window));

			// Closing again with nothing tracked is a no-op that reports nothing more.
			presenter.Close();

			Assert.AreEqual(1, closedCalls);
		}
	}

	[TestMethod]
	public void Close_ThrowingSubscriber_PropagatesToTheCloser()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			presenter.WindowClosed += (_, _) => throw new InvalidOperationException("Subscriber failed.");
			presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			// The documented contract: a throwing subscriber propagates to whoever closed the window,
			// exactly like a throwing host callback on the close path, while the window stays untracked.
			InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(presenter.Close);

			Assert.AreEqual("Subscriber failed.", exception.Message);
			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);
		}
	}

	[TestMethod]
	public void Show_WithoutCreatedWindow_IsANoOp()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int closedCalls = 0;
			presenter.WindowClosed += (_, _) => closedCalls++;

			presenter.Show();

			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);

			// A second show with the same window still tracked keeps it tracked and reports nothing.
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			presenter.Show();
			presenter.Show();

			Assert.IsTrue(presenter.IsWindowOpen);
			Assert.AreSame(window, presenter.ActiveWindow);
			Assert.AreEqual(0, closedCalls);

			// Close the created window so the test does not leave a live popup behind.
			presenter.Close();
			Assert.IsFalse(presenter.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_AfterWindowClosedItself_ReportsNothingMore()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int closedCalls = 0;
			presenter.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
			presenter.Show();

			window.Close();
			presenter.Close();

			Assert.AreEqual(1, closedCalls, "A self-closed window must not be reported a second time by Close.");
			Assert.IsFalse(presenter.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Show_FailingShow_DoesNotTrackWindowOrRaiseTheEvent()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter(showWindow: CreateFailingShow());

		using (hostWindow)
		{
			int closedCalls = 0;
			presenter.WindowClosed += (_, _) => closedCalls++;

			ArrangeFailingShow(presenter);

			Assert.ThrowsExactly<InvalidOperationException>(presenter.Show);

			// A window that never became visible must not stay tracked and must not report a closure.
			Assert.IsFalse(presenter.IsWindowOpen);
			Assert.IsNull(presenter.ActiveWindow);
			Assert.AreEqual(0, closedCalls);

			ReleaseAfterFailingShow(presenter);
		}
	}

	[TestMethod]
	public void Create_WithNegativeOrNonFiniteMaxHeight_ThrowsArgumentOutOfRangeException()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => presenter.Create(-1.0));
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => presenter.Create(double.NaN));
		}
	}

	[TestMethod]
	public void Theme_NullBrush_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionWindowTheme
			{
				BorderBrush = null!,
				Background = Brushes.Black,
				Foreground = Brushes.White,
			}
		);

		Assert.AreEqual("BorderBrush", exception.ParamName);
	}

	[TestMethod]
	public void Theme_NullBackgroundBrush_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionWindowTheme
			{
				BorderBrush = Brushes.Gray,
				Background = null!,
				Foreground = Brushes.White,
			}
		);

		Assert.AreEqual("Background", exception.ParamName);
	}

	[TestMethod]
	public void Theme_NullForegroundBrush_ThrowsArgumentNullException()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionWindowTheme
			{
				BorderBrush = Brushes.Gray,
				Background = Brushes.Black,
				Foreground = null!,
			}
		);

		Assert.AreEqual("Foreground", exception.ParamName);
	}

	[TestMethod]
	public void Close_OnCreatedNotShownWindow_ClosesWithoutRaisingTheEvent()
	{
		(CompletionWindowPresenter presenter, HostWindow hostWindow) = CreateHostedPresenter();

		using (hostWindow)
		{
			int closedEvents = 0;
			presenter.WindowClosed += (_, _) => closedEvents++;

			CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);

			// A window that was created but never shown does not raise the closure event when it is closed.
			presenter.Close();

			Assert.IsNull(presenter.ActiveWindow);
			Assert.AreEqual(0, closedEvents);
			Assert.IsFalse(IsShown(window));
		}
	}

	private static (CompletionWindowPresenter Presenter, HostWindow HostWindow) CreateHostedPresenter(Action<CompletionWindow>? showWindow = null)
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor();
		return (CompletionTestHelpers.CreateWindowPresenter(editor, showWindow), hostWindow);
	}
}
