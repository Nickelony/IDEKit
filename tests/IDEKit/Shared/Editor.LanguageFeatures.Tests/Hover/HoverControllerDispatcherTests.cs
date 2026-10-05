#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvalonEdit.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that a hover controller runs correctly on the engine's own dispatcher: a provider that completes on a thread-pool thread still reads
/// the pointer source and shows the tooltip on the dispatcher thread. The neutral controller tests cover the same window against a manual dispatcher.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg. The pointer source is the engine one, with only the mapping
/// from a position to an offset replaced, because an editor that was never shown cannot map a position.
/// </remarks>
[STATestClass]
public sealed class HoverControllerDispatcherTests
{
	[TestMethod]
	public void RequestAsync_PoolThreadProviderContinuation_ReadsThePointerAndShowsOnTheDispatcherThread()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var pointerThreadIds = new List<int>();
		var displayThreadIds = new List<int>();
		var displays = new List<HoverInformation?>();

		int dispatcherThreadId = Environment.CurrentManagedThreadId;

		var source = new HoverPointerSource(
			editor.TextArea,

			offsetFromPoint: _ =>
			{
				pointerThreadIds.Add(Environment.CurrentManagedThreadId);
				return 3;
			}
		);

		using var controller = new HoverController(
			pointerSource: source,
			provider: new PoolThreadProvider(providerStarted, providerGate),

			hooks: new HoverControllerHooks
			{
				BuildDecision = static offset => new HoverDecision
				{
					ShouldRequestHover = true,
					RequestOffset = offset,
					CanShowHoverContent = true,
					CanShowDiagnosticFallback = false,
					Diagnostic = null,
				},

				BuildRequest = static offset => new HoverRequest("hover-dispatcher-test.lua", "sample", offset),

				ShowHover = (hoverInfo, _, _) =>
				{
					displays.Add(hoverInfo);
					displayThreadIds.Add(Environment.CurrentManagedThreadId);
				},
			},

			dispatcher: UiDispatcher.From(editor.TextArea.Dispatcher)
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		Task request = controller.RequestAsync();

		// The provider resumes on a thread-pool thread after its gate opens, so the second pointer read and the display must still run here.
		DispatcherTestHelpers.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		Assert.HasCount(1, displays);
		Assert.IsNotNull(displays[0]);
		Assert.AreSequenceEqual([dispatcherThreadId], displayThreadIds);
		Assert.AreSequenceEqual([dispatcherThreadId, dispatcherThreadId], pointerThreadIds);
	}

	private sealed class PoolThreadProvider(TaskCompletionSource started, TaskCompletionSource gate) : IHoverProvider
	{
		public async Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
		{
			started.SetResult();

			await gate.Task.ConfigureAwait(false);

			return new HoverInformation("hover") { SymbolName = "symbol" };
		}
	}
}
