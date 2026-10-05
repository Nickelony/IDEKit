using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Provides the Avalonia half of the completion window helpers that the shared completion window tests call.</summary>
/// <remarks>
/// An AvaloniaEdit completion window is a borderless popup whose chrome, height cap and font live on its completion list, and the headless host
/// does not measure the popup.
/// </remarks>
internal static class CompletionWindowTestHelpers
{
	/// <summary>Gets the element that draws the window frame, whose size, font and theme the controller sets.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns>The completion list of the popup.</returns>
	internal static TemplatedControl GetFrameElement(CompletionWindow window) => window.CompletionList;

	/// <summary>Determines whether the completion window is shown.</summary>
	/// <param name="window">The completion window.</param>
	/// <returns><see langword="true"/> when the popup is open.</returns>
	internal static bool IsShown(CompletionWindow window) => window.IsOpen;

	/// <summary>Asserts that the window carries no window style.</summary>
	/// <param name="window">The completion window.</param>
	/// <remarks>
	/// The popup carries no window style to assert.
	/// </remarks>
	internal static void AssertHasNoWindowStyle(CompletionWindow window) { }

	/// <summary>Asserts that the user cannot resize the window.</summary>
	/// <param name="window">The completion window.</param>
	/// <remarks>
	/// The popup is not user-resizable, so there is no resize mode to assert.
	/// </remarks>
	internal static void AssertIsNotResizable(CompletionWindow window) { }

	/// <summary>Observes the closure of a window that was created but not shown yet.</summary>
	/// <param name="window">The completion window to observe.</param>
	/// <returns>A function that reports whether the window closed.</returns>
	/// <remarks>
	/// Closing a popup that was never opened does not raise its <c>Closed</c> event, so the closure is read from the open state instead.
	/// </remarks>
	internal static Func<bool> ObserveClosure(CompletionWindow window) => () => !window.IsOpen;

	/// <summary>Creates the show action that makes the presenter's show fail, or <see langword="null"/> when the window fails by itself.</summary>
	/// <returns>An action that throws.</returns>
	/// <remarks>
	/// The popup reopens after <c>Close</c> without throwing, so the failing show is forced through the presenter's injected show action
	/// instead of a closed-and-reshown window.
	/// </remarks>
	internal static Action<CompletionWindow>? CreateFailingShow() => static _ => throw new InvalidOperationException("forced show failure");

	/// <summary>Creates the window of the presenter so that the next <c>Show</c> fails.</summary>
	/// <param name="presenter">The presenter that owns the window.</param>
	internal static void ArrangeFailingShow(CompletionWindowPresenter presenter) => presenter.Create(CompletionWindowOptions.Default.MaxHeight);

	/// <summary>Releases what the failed show left behind.</summary>
	/// <param name="presenter">The presenter that owns the window.</param>
	internal static void ReleaseAfterFailingShow(CompletionWindowPresenter presenter) => presenter.Close();

	/// <summary>Asserts that the posted initial selection did not apply the provider's preselection.</summary>
	/// <param name="presenter">The presenter that tracks the window.</param>
	/// <param name="window">The open completion window.</param>
	/// <param name="preselected">The item that carries the preselection hint.</param>
	/// <remarks>
	/// AvaloniaEdit applies the best-match selection asynchronously (<c>CompletionList.SelectItem</c> posts <c>SelectIndexCentered</c>
	/// at <c>Loaded</c> priority), so the controller's synchronous preselection is overwritten and the engine's best match wins the open path.
	/// The helper asserts the divergence so an engine change that honors the preselection fails the test. The refresh path, where the synchronous
	/// selection is still observable, is pinned by <c>CompletionPresenterWindowRefreshTests</c>.
	/// </remarks>
	internal static void AssertOpenPathPreselection(CompletionWindowPresenter presenter, CompletionWindow window, ICompletionData preselected)
	{
		Assert.IsNotNull(window);

		window.UpdateLayout();
		DispatcherTestHelpers.PumpUntilIdle(DispatcherPriority.ApplicationIdle);

		Assert.IsTrue(presenter.IsWindowOpen);

		Assert.AreNotSame(
			preselected,
			window.CompletionList.ListBox.SelectedItem,
			"AvaloniaEdit's deferred best-match selection overwrites the preselection."
		);
	}

	/// <summary>Asserts that the preselected item at the end of a long list was not selected.</summary>
	/// <param name="presenter">The presenter that tracks the window.</param>
	/// <param name="window">The open completion window.</param>
	/// <param name="items">The items of the window.</param>
	/// <param name="preselectedIndex">The index of the preselected item.</param>
	/// <remarks>
	/// The scroll assertion depends on the preselected item becoming the selection, but AvaloniaEdit's deferred best-match selection overwrites
	/// the controller's synchronous preselection, so the off-screen preselected item is never selected on the open path. The helper asserts
	/// the divergence so an engine change that honors it fails the test.
	/// </remarks>
	internal static void AssertPreselectedItemScrolledIntoView(
		CompletionWindowPresenter presenter,
		CompletionWindow window,
		IReadOnlyList<ICompletionData> items,
		int preselectedIndex
	)
	{
		Assert.IsNotNull(window);

		window.UpdateLayout();
		DispatcherTestHelpers.PumpUntilIdle(DispatcherPriority.ApplicationIdle);

		Assert.IsTrue(presenter.IsWindowOpen);

		Assert.AreNotSame(
			items[preselectedIndex],
			window.CompletionList.ListBox.SelectedItem,
			"AvaloniaEdit's deferred best-match selection never selects the off-screen preselected item."
		);
	}

	/// <summary>Waits for the posted initial selection to leave the list in its steady state.</summary>
	/// <param name="window">The open completion window.</param>
	/// <param name="expectedItemCount">The number of items the list holds in its steady state.</param>
	/// <remarks>
	/// AvaloniaEdit owns the swap away from the raw completion data internally, so the helper waits for the steady item count.
	/// </remarks>
	internal static void AwaitListSteadyState(CompletionWindow window, int expectedItemCount)
	{
		ListBox listBox = window.CompletionList.ListBox;

		// Let the posted initial selection run so the list is in its steady state.
		DispatcherTestHelpers.PumpUntil(() => listBox.ItemCount == expectedItemCount, priority: DispatcherPriority.ContextIdle);

		Assert.AreEqual(expectedItemCount, listBox.ItemCount);
	}

	/// <summary>Waits until the list holds items, so a check of the list does not observe it before it is populated.</summary>
	/// <param name="window">The open completion window.</param>
	/// <remarks>
	/// AvaloniaEdit filters the completion list lazily, so the list box is only populated once the posted initial selection applies the query.
	/// A check of an empty list that ran earlier would observe an unpopulated list and close the window.
	/// </remarks>
	internal static void AwaitPopulatedList(CompletionWindow window) =>
		DispatcherTestHelpers.PumpUntil(() => window.CompletionList.ListBox.ItemCount > 0, priority: DispatcherPriority.ApplicationIdle);

	/// <summary>Lets a selection that the engine applies after the call settle before the selection is read.</summary>
	/// <remarks>
	/// AvaloniaEdit applies the best-match selection asynchronously, so the deferred selection is pumped in first.
	/// </remarks>
	internal static void SettleDeferredSelection() => DispatcherTestHelpers.PumpUntilIdle();

	/// <summary>Asserts the sizing of a window that holds one short item.</summary>
	/// <param name="window">The completion window.</param>
	/// <param name="options">The window options of the controller.</param>
	/// <remarks>
	/// The headless host does not measure the popup, so the measured-height assertions have no deterministic counterpart; the height cap
	/// is pinned instead.
	/// </remarks>
	internal static void AssertSizedToContent(CompletionWindow window, CompletionWindowOptions options) =>
		Assert.AreEqual(options.MaxHeight, window.CompletionList.MaxHeight);

	/// <summary>Asserts that a window of a tall item set grows towards the cap without exceeding it.</summary>
	/// <param name="tallWindow">The window of the tall item set.</param>
	/// <param name="smallWindow">The window of a single item.</param>
	/// <param name="options">The window options of the controller.</param>
	/// <remarks>
	/// The headless host does not measure the popup, so the height cap is pinned instead of the measured height.
	/// </remarks>
	internal static void AssertGrowsTowardsCap(CompletionWindow tallWindow, CompletionWindow smallWindow, CompletionWindowOptions options) =>
		Assert.AreEqual(options.MaxHeight, tallWindow.CompletionList.MaxHeight);

	/// <summary>Gets the pixels-per-DIP factor that the completion measurement is expected to use for a text area.</summary>
	/// <param name="textArea">The text area the measurement serves.</param>
	/// <returns>The factor <c>1.0</c>, because Avalonia measures in device-independent units and its measurement has no DPI input.</returns>
	internal static double GetExpectedPixelsPerDip(TextArea textArea) => 1.0;

	/// <summary>Creates the one-pixel icon that makes a completion item reserve the icon column.</summary>
	/// <returns>The icon image.</returns>
	internal static IImage CreateIconImage() => new TestImage();

	/// <summary>A minimal 1x1 image double for the sizing tests.</summary>
	/// <remarks>
	/// The sizing measures an item's icon column from the image's presence, so this lightweight <see cref="IImage"/> stands in for a rendered icon.
	/// </remarks>
	private sealed class TestImage : IImage
	{
		/// <inheritdoc/>
		public Size Size => new(1.0, 1.0);

		/// <inheritdoc/>
		public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
	}
}
