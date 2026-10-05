using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvalonEdit.Threading;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the tooltip presenter's lifecycle branches: the degraded access path (an AvalonEdit upgrade without the reflected field,
/// which skips styling and logs the unsupported access once per presenter) and the stale-window guards that keep an update or a late resolve
/// from writing into a tooltip whose window is not the tracked one.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionTooltipPresenterTests
{
	[TestMethod]
	public void ApplyTheme_WhenTooltipAccessFails_SkipsStylingAndLogsTheWarningOnce()
	{
		var logger = new RecordingLogger();

		TextEditor editor = WpfTestHost.CreateEditor("sample");

		var completionWindow = new CompletionWindow(editor.TextArea);

		var context = new CompletionTooltipPresenterContext(
			Dispatcher: UiDispatcher.FromCurrent(),
			GetActiveWindow: static () => null,
			SetTooltipState: static (_, _) => { },
			TryGetTooltip: static _ => null
		);

		using var presenter = new CompletionTooltipPresenter(
			context,
			theme: CompletionTooltipTheme.Default,
			hooks: null,
			resolveDelay: TimeSpan.FromMilliseconds(120.0),
			logger
		);

		presenter.ApplyTheme(completionWindow);
		presenter.ApplyTheme(completionWindow);

		// The degraded path must not throw, and the access failure is reported exactly once so an
		// AvalonEdit upgrade becomes visible without spamming every opened window.
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1002, logger.Entries[0].EventId.Id);
		Assert.IsFalse(presenter.IsUpdatePending);
	}

	[TestMethod]
	public void RunUpdate_WhenTheActiveWindowIsNoLongerTracked_DoesNotWriteTheTooltip()
	{
		var logger = new RecordingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WpfTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.CompletionList.CompletionData.Add(new StubCompletionData("sample", "description"));
			completionWindow.Show();

			CompletionWindow? activeWindow = completionWindow;
			var tooltipStates = new List<(object? Content, bool Visible)>();
			CompletionTooltipPresenterContext context = CreateContext(editor, () => activeWindow, tooltipStates);

			using var presenter = new CompletionTooltipPresenter(
				context,
				theme: CompletionTooltipTheme.Default,
				hooks: null,
				resolveDelay: CreateFastDelay(),
				logger
			);

			presenter.ApplyTheme(completionWindow);

			completionWindow.CompletionList.SelectItem("sample");
			Assert.IsTrue(presenter.IsUpdatePending, "Selecting an item must schedule the debounced tooltip update.");

			// The tracked window vanishes before the debounce runs; the update must not write into its tooltip.
			activeWindow = null;

			DispatcherTestHelpers.PumpUntil(() => !presenter.IsUpdatePending);

			Assert.AreSequenceEqual(
				Array.Empty<(object?, bool)>(),
				tooltipStates,
				"An update whose window is no longer tracked must not report tooltip state."
			);
		}
	}

	[TestMethod]
	public void SelectionChange_WhenTheWindowIsNotTheTrackedOne_BuildsNoMarkdown()
	{
		var logger = new RecordingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WpfTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);

			completionWindow.CompletionList.CompletionData.Add(
				new CompletionItemAdapter(new CompletionItem("sample") { Documentation = new MarkupContent("**docs**", MarkupKind.Markdown) })
			);

			completionWindow.Show();

			var markdownInputs = new List<string>();

			CompletionWindow? activeWindow = null;
			CompletionTooltipPresenterContext context = CreateContext(editor, () => activeWindow, []);

			using var presenter = new CompletionTooltipPresenter(
				context,
				theme: CompletionTooltipTheme.Default,

				hooks: new CompletionPresenterHooks
				{
					BuildMarkdownContent = markdown =>
					{
						markdownInputs.Add(markdown);
						return new Border();
					},
				},

				resolveDelay: CreateFastDelay(),
				logger
			);

			presenter.ApplyTheme(completionWindow);

			// The window opens its list before it is tracked, so a selection change then belongs to no tracked window's tooltip.
			completionWindow.CompletionList.SelectItem("sample");

			Assert.IsEmpty(markdownInputs);

			activeWindow = completionWindow;
			completionWindow.CompletionList.ListBox.SelectedItem = null;
			completionWindow.CompletionList.ListBox.SelectedItem = completionWindow.CompletionList.CompletionData[0];

			Assert.HasCount(1, markdownInputs);
		}
	}

	[TestMethod]
	public void RunUpdate_WhenTheActiveWindowWasReplaced_DropsTheLateResolvedContent()
	{
		var logger = new RecordingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WpfTestHost.ShowHostedEditor("sample");
		(ICSharpCode.AvalonEdit.TextEditor replacementEditor, HostWindow replacementHostWindow) = WpfTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			using (replacementHostWindow)
			{
				var completionWindow = new CompletionWindow(editor.TextArea);
				completionWindow.CompletionList.CompletionData.Add(new StubCompletionData("sample", description: null));
				completionWindow.Show();

				// The replacement window only needs to be a distinct tracked window; the guard short-circuits on
				// the identity comparison before it reads anything from it.
				var replacementWindow = new CompletionWindow(replacementEditor.TextArea);

				var resolution = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
				CompletionWindow? activeWindow = completionWindow;
				var tooltipStates = new List<(object? Content, bool Visible)>();
				CompletionTooltipPresenterContext context = CreateContext(editor, () => activeWindow, tooltipStates);

				using var presenter = new CompletionTooltipPresenter(
					context,
					theme: CompletionTooltipTheme.Default,
					hooks: new CompletionPresenterHooks { ResolveDescriptionAsync = (_, _) => resolution.Task },
					resolveDelay: CreateFastDelay(),
					logger
				);

				presenter.ApplyTheme(completionWindow);

				completionWindow.CompletionList.SelectItem("sample");
				DispatcherTestHelpers.PumpUntil(() => !presenter.IsUpdatePending);

				// The null description already reported a hide decision, so the resolve is now in flight.
				Assert.HasCount(1, tooltipStates);
				Assert.IsNull(tooltipStates[0].Content);
				Assert.IsFalse(tooltipStates[0].Visible);

				// The tracked window is replaced while the resolve is in flight; the late content must not be
				// written into the replaced window's tooltip.
				activeWindow = replacementWindow;
				resolution.TrySetResult("late resolved description");
				DispatcherTestHelpers.PumpUntilIdle();

				Assert.HasCount(1, tooltipStates, "A late resolve for a replaced window must not write tooltip state.");
			}
		}
	}

	[TestMethod]
	public void RunUpdate_ItemWithoutAResolveCallback_ResolvesNothingAndWritesTheTooltipOnce()
	{
		var logger = new RecordingLogger();
		(ICSharpCode.AvalonEdit.TextEditor editor, HostWindow hostWindow) = WpfTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			var plain = new CompletionItemAdapter(new CompletionItem("plain") { Documentation = "Plain text." });

			var resolvable = new CompletionItemAdapter(
				new CompletionItem("resolvable")
				{
					ResolveCallback = _ => Task.FromResult(new CompletionItem("resolvable") { Documentation = "Resolved text." }),
				}
			);

			completionWindow.CompletionList.CompletionData.Add(plain);
			completionWindow.CompletionList.CompletionData.Add(resolvable);

			completionWindow.Show();

			var tooltipStates = new List<(object? Content, bool Visible)>();
			CompletionTooltipPresenterContext context = CreateContext(editor, () => completionWindow, tooltipStates);

			using var presenter = new CompletionTooltipPresenter(
				context,
				theme: CompletionTooltipTheme.Default,
				hooks: null,
				resolveDelay: CreateFastDelay(),
				logger
			);

			presenter.ApplyTheme(completionWindow);

			completionWindow.CompletionList.ListBox.SelectedItem = plain;
			DispatcherTestHelpers.PumpUntil(() => !presenter.IsUpdatePending);
			DispatcherTestHelpers.PumpUntilIdle();

			// An item without a callback would only resolve to itself, so its update writes the tooltip once.
			Assert.HasCount(1, tooltipStates);
			Assert.AreEqual("Plain text.", tooltipStates[0].Content);

			tooltipStates.Clear();
			completionWindow.CompletionList.ListBox.SelectedItem = resolvable;

			DispatcherTestHelpers.PumpUntil(() => !presenter.IsUpdatePending);
			DispatcherTestHelpers.PumpUntil(() => tooltipStates.Count > 0 && tooltipStates[^1].Visible);

			DispatcherTestHelpers.PumpUntilIdle();

			// An item with one resolves, and the tooltip ends on the resolved text.
			Assert.AreEqual(("Resolved text.", true), tooltipStates[^1]);
		}
	}

	private static TimeSpan CreateFastDelay() => TimeSpan.FromMilliseconds(1.0);

	private static CompletionTooltipPresenterContext CreateContext(
		ICSharpCode.AvalonEdit.TextEditor editor,
		Func<CompletionWindow?> getActiveWindow,
		List<(object? Content, bool Visible)> tooltipStates
	)
	{
		return new(
			Dispatcher: UiDispatcher.From(editor.TextArea.Dispatcher),
			getActiveWindow,
			SetTooltipState: (content, visible) => tooltipStates.Add((content, visible)),
			TryGetTooltip: static window => CompletionTooltipResolver.TryGetTooltip(window, out ToolTip? tooltip) ? tooltip : null
		);
	}
}
