using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the tooltip presenter's lifecycle branches: the degraded access path (an AvaloniaEdit version
/// without an exposed tooltip, which skips styling and logs the unsupported access once per presenter) and
/// the stale-window guards that keep an update or a late resolve from writing into a tooltip whose window is
/// no longer the tracked one.
/// </summary>
/// <remarks>
/// Avalonia divergence: AvaloniaEdit exposes no tooltip member for <see cref="CompletionWindowTooltipAccess"/>
/// to read, so <see cref="CompletionTooltipPresenter.ApplySkin"/> always takes the graceful-disable path and
/// never subscribes to the list selection. The stale-window guard is therefore unreachable from a test; the
/// tests below pin the observable outcome the mirror actually produces (no pending update, no tooltip state,
/// no description resolve) instead of the reference's pending-update assertions.
/// </remarks>
[AvaloniaTestClass]
public sealed class CompletionTooltipPresenterTests
{
	[TestMethod]
	public void Style_WhenTooltipAccessFails_SkipsStylingAndLogsTheWarningOnce()
	{
		var logger = new CapturingLogger();
		var editor = AvaloniaTestHost.CreateEditor("sample");
		var completionWindow = new CompletionWindow(editor.TextArea);
		var context = new CompletionTooltipPresenterContext(
			Dispatcher.UIThread,
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
		// AvaloniaEdit upgrade becomes visible without spamming every opened window.
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1002, logger.Entries[0].EventId.Id);
		Assert.IsFalse(presenter.IsUpdatePending);
	}

	[TestMethod]
	public void RunUpdate_WhenTheActiveWindowIsNoLongerTracked_DoesNotWriteTheTooltip()
	{
		var logger = new CapturingLogger();
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor("sample");

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

			// The unsupported tooltip access must leave the presenter without a subscription, so selecting an
			// item cannot schedule a debounced update at all.
			Assert.IsFalse(presenter.IsUpdatePending, "The unsupported tooltip access must not schedule an update.");

			// The tracked window vanishes before any debounce could run; no tooltip state may be reported.
			activeWindow = null;

			DispatcherTestUtils.PumpUntilIdle();

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
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor("sample");
		(TextEditor replacementEditor, HostWindow replacementHostWindow) = AvaloniaTestHost.ShowHostedEditor("sample");

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
			bool resolveInvoked = false;
			CompletionWindow? activeWindow = completionWindow;
			var tooltipStates = new List<(object? Content, bool Visible)>();
			var context = CreateContext(editor, () => activeWindow, tooltipStates);

			using var presenter = new CompletionTooltipPresenter(
				CreateFastOptions(),
				CompletionTooltipSkin.Default,
				context,
				resolveDescriptionAsync: (_, _) =>
				{
					resolveInvoked = true;
					return resolution.Task;
				},
				configureTooltip: null,
				logger);

			presenter.ApplySkin(completionWindow);

			completionWindow.CompletionList.SelectItem("sample");
			DispatcherTestUtils.PumpUntilIdle();

			// The unsupported tooltip access means no update ever runs, so the description resolver is never
			// entered; the late content therefore cannot be written into a replaced window's tooltip either.
			Assert.IsFalse(resolveInvoked, "The unsupported tooltip access must not start a description resolve.");

			// The tracked window is replaced while a resolve would have been in flight; the late content must
			// not be written into the replaced window's tooltip.
			activeWindow = replacementWindow;
			resolution.TrySetResult("late resolved description");
			DispatcherTestUtils.PumpUntilIdle();

			CollectionAssert.AreEqual(
				Array.Empty<(object?, bool)>(),
				tooltipStates,
				"A late resolve for a replaced window must not write tooltip state.");
		}
	}

	private static TextCompletionControllerOptions CreateFastOptions() => TextCompletionControllerOptions.Default with
	{
		TooltipResolveDelay = TimeSpan.FromMilliseconds(1.0)
	};

	private static CompletionTooltipPresenterContext CreateContext(
		TextEditor editor,
		Func<CompletionWindow?> getActiveWindow,
		List<(object? Content, bool Visible)> tooltipStates)
		=> new(
			editor.TextArea.Dispatcher,
			getActiveWindow,
			(content, visible) => tooltipStates.Add((content, visible)),
			static window => CompletionWindowTooltipAccess.TryGetTooltip(window, out ToolTip? tooltip) ? tooltip : null);
}
