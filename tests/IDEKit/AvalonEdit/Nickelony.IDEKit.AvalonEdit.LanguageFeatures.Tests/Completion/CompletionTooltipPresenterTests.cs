using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the tooltip presenter's lifecycle branches: the degraded access path (an AvalonEdit upgrade
/// without the reflected field, which skips styling and logs the unsupported access once per presenter)
/// and the stale-window guards that keep an update or a late resolve from writing into a tooltip whose
/// window is no longer the tracked one.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionTooltipPresenterTests
{
	[TestMethod]
	public void Style_WhenTooltipAccessFails_SkipsStylingAndLogsTheWarningOnce()
	{
		var logger = new CapturingLogger();
		var editor = WPFTestHost.CreateEditor("sample");
		var completionWindow = new CompletionWindow(editor.TextArea);
		var context = new CompletionTooltipPresenterContext(
			Dispatcher.CurrentDispatcher,
			static () => null,
			static (_, _) => { },
			static _ => null);

		using var presenter = new CompletionTooltipPresenter(
			TextCompletionControllerOptions.Default,
			CompletionTooltipSkin.Default,
			context,
			resolveDescriptionAsync: null,
			configureTooltip: null,
			logger);

		presenter.ApplySkin(completionWindow);
		presenter.ApplySkin(completionWindow);

		// The degraded path must not throw, and the access failure is reported exactly once so an
		// AvalonEdit upgrade becomes visible without spamming every opened window.
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1002, logger.Entries[0].EventId.Id);
		Assert.IsFalse(presenter.IsUpdatePending);
	}

	[TestMethod]
	public void RunUpdate_WhenTheActiveWindowIsNoLongerTracked_DoesNotWriteTheTooltip()
	{
		var logger = new CapturingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WPFTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.CompletionList.CompletionData.Add(new TestCompletionData("sample", "description"));
			completionWindow.Show();

			CompletionWindow? activeWindow = completionWindow;
			var tooltipStates = new List<(object? Content, bool Visible)>();
			var context = CreateContext(editor, () => activeWindow, tooltipStates);

			using var presenter = new CompletionTooltipPresenter(
				CreateFastOptions(),
				CompletionTooltipSkin.Default,
				context,
				resolveDescriptionAsync: null,
				configureTooltip: null,
				logger);

			presenter.ApplySkin(completionWindow);

			completionWindow.CompletionList.SelectItem("sample");
			Assert.IsTrue(presenter.IsUpdatePending, "Selecting an item must schedule the debounced tooltip update.");

			// The tracked window vanishes before the debounce runs; the update must not write into its tooltip.
			activeWindow = null;

			DispatcherTestUtils.PumpUntil(() => !presenter.IsUpdatePending);

			CollectionAssert.AreEqual(
				Array.Empty<(object?, bool)>(),
				tooltipStates,
				"An update whose window is no longer tracked must not report tooltip state.");
		}
	}

	[TestMethod]
	public void RunUpdate_WhenTheActiveWindowWasReplaced_DropsTheLateResolvedContent()
	{
		var logger = new CapturingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WPFTestHost.ShowHostedEditor("sample");
		(ICSharpCode.AvalonEdit.TextEditor replacementEditor, HostWindow replacementHostWindow) =
			WPFTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		using (replacementHostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.CompletionList.CompletionData.Add(new TestCompletionData("sample", description: null));
			completionWindow.Show();

			// The replacement window only needs to be a distinct tracked window; the guard short-circuits on
			// the identity comparison before it reads anything from it.
			var replacementWindow = new CompletionWindow(replacementEditor.TextArea);

			var resolution = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			CompletionWindow? activeWindow = completionWindow;
			var tooltipStates = new List<(object? Content, bool Visible)>();
			var context = CreateContext(editor, () => activeWindow, tooltipStates);

			using var presenter = new CompletionTooltipPresenter(
				CreateFastOptions(),
				CompletionTooltipSkin.Default,
				context,
				resolveDescriptionAsync: (_, _) => resolution.Task,
				configureTooltip: null,
				logger);

			presenter.ApplySkin(completionWindow);

			completionWindow.CompletionList.SelectItem("sample");
			DispatcherTestUtils.PumpUntil(() => !presenter.IsUpdatePending);

			// The null description already reported a hide decision, so the resolve is now in flight.
			Assert.AreEqual(1, tooltipStates.Count);
			Assert.IsNull(tooltipStates[0].Content);
			Assert.IsFalse(tooltipStates[0].Visible);

			// The tracked window is replaced while the resolve is in flight; the late content must not be
			// written into the replaced window's tooltip.
			activeWindow = replacementWindow;
			resolution.TrySetResult("late resolved description");
			DispatcherTestUtils.PumpUntilIdle();

			Assert.AreEqual(1, tooltipStates.Count, "A late resolve for a replaced window must not write tooltip state.");
		}
	}

	private static TextCompletionControllerOptions CreateFastOptions() => TextCompletionControllerOptions.Default with
	{
		TooltipResolveDelay = TimeSpan.FromMilliseconds(1.0)
	};

	private static CompletionTooltipPresenterContext CreateContext(
		ICSharpCode.AvalonEdit.TextEditor editor,
		Func<CompletionWindow?> getActiveWindow,
		List<(object? Content, bool Visible)> tooltipStates)
		=> new(
			editor.TextArea.Dispatcher,
			getActiveWindow,
			(content, visible) => tooltipStates.Add((content, visible)),
			static window => CompletionWindowTooltipAccess.TryGetTooltip(window, out ToolTip? tooltip) ? tooltip : null);
}
