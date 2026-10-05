using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Provides the WPF half of the completion window helpers that the shared completion window tests call.</summary>
internal static class CompletionWindowTestHelpers
{
	/// <summary>Gets the element that draws the window frame, whose size, font and theme the controller sets.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns>The window itself.</returns>
	internal static Control GetFrameElement(CompletionWindow window) => window;

	/// <summary>Determines whether the completion window is shown.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns><see langword="true"/> when the window is visible.</returns>
	internal static bool IsShown(CompletionWindow window) => window.IsVisible;

	/// <summary>Asserts that the window carries no window style.</summary>
	/// <param name="window">The completion window.</param>
	internal static void AssertHasNoWindowStyle(CompletionWindow window)
	{
		// CompletionWindowBase overrides the window style metadata to None, so no assignment is needed.
		Assert.AreEqual(WindowStyle.None, window.WindowStyle);
	}

	/// <summary>Asserts that the user cannot resize the window.</summary>
	/// <param name="window">The completion window.</param>
	internal static void AssertIsNotResizable(CompletionWindow window)
	{
		// AvalonEdit only sets the window style metadata, so the theme and the presenter's resize
		// setting are the only source of this value.
		Assert.AreEqual(ResizeMode.NoResize, window.ResizeMode);
	}

	/// <summary>Observes the closure of a window that was created but not shown yet.</summary>
	/// <param name="window">The completion window to observe.</param>
	/// <returns>A function that reports whether the window closed.</returns>
	internal static Func<bool> ObserveClosure(CompletionWindow window)
	{
		bool closed = false;

		window.Closed += (_, _) => closed = true;

		return () => closed;
	}

	/// <summary>Creates the show action that makes the presenter's show fail, or <see langword="null"/> when the window fails by itself.</summary>
	/// <returns><see langword="null"/>, because a window that has already closed cannot be shown again.</returns>
	internal static Action<CompletionWindow>? CreateFailingShow() => null;

	/// <summary>Creates the window of the presenter so that the next <c>Show</c> fails.</summary>
	/// <param name="presenter">The presenter that owns the window.</param>
	internal static void ArrangeFailingShow(CompletionWindowPresenter presenter)
	{
		// A window that has already been closed cannot be shown again, which models a failing show operation.
		CompletionWindow window = presenter.Create(CompletionWindowOptions.Default.MaxHeight);
		window.Show();
		window.Close();
	}

	/// <summary>Releases what the failed show left behind.</summary>
	/// <param name="presenter">The presenter that owns the window.</param>
	/// <remarks>
	/// The failed WPF show closed its window already, so there is nothing to release.
	/// </remarks>
	internal static void ReleaseAfterFailingShow(CompletionWindowPresenter presenter) { }

	/// <summary>Waits for the posted initial selection to apply the provider's preselection.</summary>
	/// <param name="presenter">The presenter that tracks the window.</param>
	/// <param name="window">The open completion window.</param>
	/// <param name="preselected">The item that carries the preselection hint.</param>
	internal static void AssertOpenPathPreselection(CompletionWindowPresenter presenter, CompletionWindow window, ICompletionData preselected)
	{
		// The posted context-idle selection applies the provider's preselection once the window's
		// layout ran; both items match the one-character query, so the preselected one is available.
		DispatcherTestHelpers.PumpUntil(
			() => ReferenceEquals(window.CompletionList.ListBox.SelectedItem, preselected),
			priority: DispatcherPriority.ApplicationIdle
		);

		Assert.IsTrue(presenter.IsWindowOpen);
	}

	/// <summary>Waits until the preselected item at the end of a long list is selected and realized in the list.</summary>
	/// <param name="presenter">The presenter that tracks the window.</param>
	/// <param name="window">The open completion window.</param>
	/// <param name="items">The items of the window.</param>
	/// <param name="preselectedIndex">The index of the preselected item.</param>
	internal static void AssertPreselectedItemScrolledIntoView(
		CompletionWindowPresenter presenter,
		CompletionWindow window,
		IReadOnlyList<ICompletionData> items,
		int preselectedIndex
	)
	{
		ListBox listBox = window.CompletionList.ListBox;

		DispatcherTestHelpers.PumpUntil(
			() => ReferenceEquals(listBox.SelectedItem, items[preselectedIndex]),
			priority: DispatcherPriority.ApplicationIdle
		);

		window.UpdateLayout();

		// A realized container for the selected item proves the list scrolled to it: a virtualizing
		// list realizes only the visible items.
		DispatcherTestHelpers.PumpUntil(() => listBox.ItemContainerGenerator.ContainerFromIndex(preselectedIndex) is not null);
	}

	/// <summary>Waits for the posted initial selection to leave the list in its steady state.</summary>
	/// <param name="window">The open completion window.</param>
	/// <param name="expectedItemCount">The number of items the list holds in its steady state.</param>
	internal static void AwaitListSteadyState(CompletionWindow window, int expectedItemCount)
	{
		ListBox listBox = window.CompletionList.ListBox;

		// Let the posted initial selection run so the list is in its steady state.
		DispatcherTestHelpers.PumpUntil(
			() => !ReferenceEquals(listBox.ItemsSource, window.CompletionList.CompletionData),
			priority: DispatcherPriority.ContextIdle
		);

		Assert.AreEqual(expectedItemCount, listBox.Items.Count);
	}

	/// <summary>Waits until the list holds items, so a check of the list does not observe it before it is populated.</summary>
	/// <param name="window">The open completion window.</param>
	/// <remarks>
	/// The WPF list holds its items as soon as the window is open, so there is nothing to wait for.
	/// </remarks>
	internal static void AwaitPopulatedList(CompletionWindow window) { }

	/// <summary>Lets a selection that the engine applies after the call settle before the selection is read.</summary>
	/// <remarks>
	/// The WPF list applies its selection synchronously, so there is nothing to settle.
	/// </remarks>
	internal static void SettleDeferredSelection() { }

	/// <summary>Asserts the sizing of a window that holds one short item.</summary>
	/// <param name="window">The completion window.</param>
	/// <param name="options">The window options of the controller.</param>
	internal static void AssertSizedToContent(CompletionWindow window, CompletionWindowOptions options)
	{
		window.UpdateLayout();

		// The window is content-sized: a single item stays below the cap...
		Assert.IsGreaterThan(0.0, window.ActualHeight, "The completion window must be measured for this test.");
		Assert.IsLessThan(window.MaxHeight, window.ActualHeight, "A single item must stay below the cap.");
	}

	/// <summary>Asserts that a window of a tall item set grows towards the cap without exceeding it.</summary>
	/// <param name="tallWindow">The window of the tall item set.</param>
	/// <param name="smallWindow">The window of a single item.</param>
	/// <param name="options">The window options of the controller.</param>
	internal static void AssertGrowsTowardsCap(CompletionWindow tallWindow, CompletionWindow smallWindow, CompletionWindowOptions options)
	{
		tallWindow.UpdateLayout();

		Assert.AreEqual((double)options.MaxHeight, tallWindow.MaxHeight);
		Assert.IsLessThanOrEqualTo(tallWindow.MaxHeight, tallWindow.ActualHeight, "A tall window must not exceed the cap.");

		Assert.IsGreaterThan(
			smallWindow.ActualHeight,
			tallWindow.ActualHeight,
			"The premise: both windows were measured, so the taller item set grows the window."
		);
	}

	/// <summary>Gets the pixels-per-DIP factor that the completion measurement is expected to use for a text area.</summary>
	/// <param name="textArea">The text area the measurement serves.</param>
	/// <returns>The DPI factor of the text area's visual.</returns>
	internal static double GetExpectedPixelsPerDip(TextArea textArea) => VisualTreeHelper.GetDpi(textArea).PixelsPerDip;

	/// <summary>Creates the one-pixel icon that makes a completion item reserve the icon column.</summary>
	/// <returns>The icon image.</returns>
	internal static ImageSource CreateIconImage() =>
		new WriteableBitmap(pixelWidth: 1, pixelHeight: 1, dpiX: 96.0, dpiY: 96.0, pixelFormat: PixelFormats.Pbgra32, palette: null);
}
