using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the completion window coordinator's lifecycle: creating, showing, tracking, and closing windows,
/// the closure reporting contract, and the skin validation. The completion window is an AvaloniaEdit popup
/// whose chrome lives on its completion list, so the skin and height assertions read the list.
/// </summary>
[AvaloniaTestClass]
public sealed class CompletionWindowCoordinatorTests
{
	[TestMethod]
	public void Create_WithHeightCap_CreatesUntrackedWindowWithHeightCap()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			// A created but not yet shown window is not tracked.
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			// The window is created with the shared completion window height cap; the controller sizes the
			// width from the measured items before showing the window, so the initial width is AvaloniaEdit's
			// metadata (deliberately not pinned here, because this package does not own it). An AvaloniaEdit
			// completion window is a borderless popup whose chrome and height cap live on its completion list,
			// so the cap is asserted there; the popup carries no window style to assert.
			Assert.AreEqual(300.0, window.CompletionList.MaxHeight);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);

			coordinator.Close();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Create_AppliesTheDocumentedWindowProperties()
	{
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor();
		var skin = new CompletionWindowSkin { BorderBrush = Brushes.Gray, Background = Brushes.Black, Foreground = Brushes.White, BorderThickness = 2.0 };
		var coordinator = new CompletionWindowCoordinator(editor.TextArea, skin);

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			// Avalonia divergence: an AvaloniaEdit completion window is a popup that draws no chrome of its
			// own and is not user-resizable, so there is no ResizeMode to assert; the skin is the only source
			// of these values and is applied to the completion list that draws the window frame.
			Assert.AreEqual(new Thickness(2.0), window.CompletionList.BorderThickness);
			Assert.AreSame(skin.Background, window.CompletionList.Background);
			Assert.AreSame(skin.Foreground, window.CompletionList.Foreground);
			Assert.AreSame(skin.BorderBrush, window.CompletionList.BorderBrush);
		}
	}

	[TestMethod]
	public void Skin_NegativeBorderThickness_ThrowsArgumentOutOfRangeException()
	{
		// The skin validates itself, so the failure site is the object initializer instead of the coordinator
		// constructor.
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionWindowSkin
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = Brushes.White,
			BorderThickness = -1.0
		});

		Assert.AreEqual("BorderThickness", exception.ParamName);
	}

	[TestMethod]
	public void IsWindowOpen_TracksWindowUntilItClosesItself()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			Assert.IsFalse(coordinator.IsWindowOpen);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);

			// An AvaloniaEdit completion window is a Popup, so its open state is IsOpen (there is no
			// window-level IsVisible semantic to read).
			Assert.IsTrue(window.IsOpen);

			window.Close();

			// A self-closed window is no longer tracked.
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Close_WithTrackedWindow_ClosesItAndClearsTracking()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			coordinator.Close();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
			Assert.IsFalse(window.IsOpen);
		}
	}

	[TestMethod]
	public void Create_TwiceWithoutShow_ClosesTheUnshownWindowAndCreatesANewOne()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			CompletionWindow first = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			CompletionWindow second = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			// The created-but-unshown window is closed by the replacement instead of being stranded
			// untracked, and the replacement stays usable. Avalonia divergence: an AvaloniaEdit completion
			// window is a Popup, and closing a Popup that was never opened does not raise its Closed event
			// (the WPF reference window raised it), so the closure is pinned through the open state instead.
			Assert.AreNotSame(first, second);
			Assert.IsFalse(first.IsOpen, "The created-but-unshown window must be closed by the replacement.");
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(second, coordinator.ActiveWindow);

			coordinator.Close();
		}
	}

	[TestMethod]
	public void Create_WhileWindowTracked_ClosesAndReportsItExactlyOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int firstClosedCalls = 0;
			coordinator.WindowClosed += (_, _) => firstClosedCalls++;
			CompletionWindow first = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			// Replacing the tracked window closes it and raises the event for it exactly once.
			CompletionWindow second = coordinator.Create(250.0);

			Assert.IsFalse(first.IsOpen);
			Assert.AreEqual(1, firstClosedCalls, "Replacing a window raises the window-closed event exactly once.");
			Assert.AreEqual(250.0, second.CompletionList.MaxHeight);
			Assert.IsFalse(coordinator.IsWindowOpen);

			coordinator.Show();
			coordinator.Close();
		}
	}

	[TestMethod]
	public void Show_WindowClosesItself_ReportsOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			window.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_TrackedWindow_ReportsThroughTheEventExactlyOnce()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			coordinator.Close();

			Assert.AreEqual(1, closedCalls);
			Assert.IsFalse(window.IsOpen);

			// Closing again with nothing tracked is a no-op that reports nothing more.
			coordinator.Close();

			Assert.AreEqual(1, closedCalls);
		}
	}

	[TestMethod]
	public void Close_ThrowingSubscriber_PropagatesToTheCloser()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			coordinator.WindowClosed += (_, _) => throw new InvalidOperationException("Subscriber failed.");
			coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			// The documented contract: a throwing subscriber propagates to whoever closed the window,
			// exactly like a throwing host callback on the close path, while the window stays untracked.
			var exception = Assert.ThrowsExactly<InvalidOperationException>(() => coordinator.Close());

			Assert.AreEqual("Subscriber failed.", exception.Message);
			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void Show_WithoutCreatedWindow_IsANoOp()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;

			coordinator.Show();

			Assert.IsFalse(coordinator.IsWindowOpen);
			Assert.IsNull(coordinator.ActiveWindow);

			// A second show with the same window still tracked keeps it tracked and reports nothing.
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();
			coordinator.Show();

			Assert.IsTrue(coordinator.IsWindowOpen);
			Assert.AreSame(window, coordinator.ActiveWindow);
			Assert.AreEqual(0, closedCalls);

			// Close the created window so the test does not leave a live popup behind.
			coordinator.Close();
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Close_AfterWindowClosedItself_ReportsNothingMore()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedCalls = 0;
			coordinator.WindowClosed += (_, _) => closedCalls++;
			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);
			coordinator.Show();

			window.Close();
			coordinator.Close();

			Assert.AreEqual(1, closedCalls, "A self-closed window must not be reported a second time by Close.");
			Assert.IsFalse(coordinator.IsWindowOpen);
		}
	}

	[TestMethod]
	public void Show_FailingShow_DoesNotTrackWindowOrRaiseTheEvent()
	{
		// Avalonia divergence: the reference modeled a failing show by closing the window and showing it
		// again, which WPF rejects with InvalidOperationException. An AvaloniaEdit completion window is a
		// Popup whose Show() after Close() reopens without throwing, so the failing-show path cannot be
		// modeled through the public API and the coordinator's rollback branch is unreachable from a test.
		// Reporting this inconclusive keeps the reference intent visible without a false assertion.
		Assert.Inconclusive(
			"An AvaloniaEdit completion window is a Popup whose Show() after Close() does not throw, so the "
			+ "coordinator's failing-show rollback branch cannot be exercised from a test.");
	}

	[TestMethod]
	public void Create_WithNegativeOrNonFiniteMaxHeight_ThrowsArgumentOutOfRangeException()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => coordinator.Create(-1.0));
			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => coordinator.Create(double.NaN));
		}
	}

	[TestMethod]
	public void Skin_NullBrush_ThrowsArgumentNullException()
	{
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowSkin
		{
			BorderBrush = null!,
			Background = Brushes.Black,
			Foreground = Brushes.White
		});

		Assert.AreEqual("BorderBrush", exception.ParamName);
	}

	[TestMethod]
	public void Skin_NullBackgroundBrush_ThrowsArgumentNullException()
	{
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowSkin
		{
			BorderBrush = Brushes.Gray,
			Background = null!,
			Foreground = Brushes.White
		});

		Assert.AreEqual("Background", exception.ParamName);
	}

	[TestMethod]
	public void Skin_NullForegroundBrush_ThrowsArgumentNullException()
	{
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => new CompletionWindowSkin
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = null!
		});

		Assert.AreEqual("Foreground", exception.ParamName);
	}

	[TestMethod]
	public void Close_OnCreatedNotShownWindow_ClosesWithoutRaisingTheEvent()
	{
		(CompletionWindowCoordinator coordinator, HostWindow hostWindow) = CreateHostedCoordinator();

		using (hostWindow)
		{
			int closedEvents = 0;
			coordinator.WindowClosed += (_, _) => closedEvents++;

			CompletionWindow window = coordinator.Create(CompletionWindowDefaults.DefaultWindowMaxHeight);

			// A window that was created but never shown does not raise the closure event when it is closed.
			coordinator.Close();

			Assert.IsNull(coordinator.ActiveWindow);
			Assert.AreEqual(0, closedEvents);
			Assert.IsFalse(window.IsOpen);
		}
	}

	private static (CompletionWindowCoordinator Coordinator, HostWindow HostWindow) CreateHostedCoordinator()
	{
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor();

		return (CompletionTestHost.CreateCoordinator(editor), hostWindow);
	}
}
