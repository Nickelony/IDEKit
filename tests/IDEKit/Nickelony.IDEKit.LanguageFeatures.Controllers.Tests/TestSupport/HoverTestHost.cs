using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Builds a hover controller over a scripted pointer source and records every display decision and provider call, so hover tests share one host
/// instead of repeating the full hook set. The host is the provider the controller asks, and it owns the dispatcher the controller runs on.
/// </summary>
/// <remarks>
/// The properties are mutable on purpose: a test starts from the defaults and swaps in the behavior it needs, and the recorder lists observe
/// what the controller reported. <see cref="DisplayCalls"/> records every display decision including the hide requests, so neither a stale tooltip
/// nor an unexpected hide can pass unnoticed. The host disposes its dispatcher, which restores the thread's synchronization context.
/// </remarks>
internal sealed class HoverTestHost : IDisposable, IHoverProvider
{
	/// <summary>The document identifier the requests the host builds carry.</summary>
	public const string DocumentId = "hover-test.lua";

	/// <summary>The document text of the request; its length bounds the offsets a request can address.</summary>
	public const string DocumentText = "0123456789abcdefghij";

	/// <param name="installSynchronizationContext">
	/// Whether the dispatcher is the synchronization context of the test thread, as the dispatcher of a running editor is. A test that needs
	/// a continuation to resume on the thread pool passes <see langword="false"/>.
	/// </param>
	public HoverTestHost(bool installSynchronizationContext = true)
	{
		Dispatcher = new FakePumpedDispatcher(installSynchronizationContext);
		PointerSource = new ScriptedHoverPointerSource(Dispatcher);
	}

	/// <summary>The dispatcher the controller runs on; the test pumps it.</summary>
	public FakePumpedDispatcher Dispatcher { get; }

	/// <summary>The pointer source the controller reads; a test scripts the offset through it.</summary>
	public ScriptedHoverPointerSource PointerSource { get; }

	/// <summary>
	/// Holds the request-state hook, which defaults to a requesting state at offset <c>5</c> that allows a tooltip and has no diagnostic
	/// and no fallback.
	/// </summary>
	public Func<int, HoverDecision> BuildDecision { get; set; } =
		static _ => new HoverDecision
		{
			ShouldRequestHover = true,
			RequestOffset = 5,
			CanShowHoverContent = true,
			CanShowDiagnosticFallback = false,
			Diagnostic = null,
		};

	/// <summary>Holds the request builder, which defaults to a request for the host document at the offset.</summary>
	public Func<int, HoverRequest?> BuildRequest { get; set; } = static offset => new HoverRequest(DocumentId, DocumentText, offset);

	/// <summary>Holds the provider behavior, which defaults to an immediately completing hover result without a diagnostic.</summary>
	public Func<int, CancellationToken, Task<HoverInformation?>> RequestHoverAsync { get; set; } =
		static (_, _) => Task.FromResult<HoverInformation?>(new HoverInformation("hover") { SymbolName = "symbol" });

	/// <summary>Holds the optional request-offset hook, or <see langword="null"/> when the host does not remap.</summary>
	public Func<int, int?>? ResolveRequestOffset { get; set; }

	/// <summary>Holds the optional context-version provider, or <see langword="null"/> when the host version never changes.</summary>
	public Func<int>? ContextVersionProvider { get; set; }

	/// <summary>
	/// The display decisions the controller reported, in order, including hide requests (<see langword="null"/> content and no diagnostic).
	/// </summary>
	public List<(HoverInformation? HoverInfo, Diagnostic? Diagnostic)> DisplayCalls { get; } = [];

	/// <summary>The offsets the controller requested hover for, in order.</summary>
	public List<int> RequestOffsets { get; } = [];

	/// <summary>The cancellation tokens the controller passed to the provider, in order.</summary>
	public List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>The target offsets the display callback received, in order, one per entry of <see cref="DisplayCalls"/>.</summary>
	public List<int?> DisplayOffsets { get; } = [];

	/// <summary>The threads the display callback ran on, in order.</summary>
	public List<int> DisplayThreadIds { get; } = [];

	/// <summary>
	/// Holds an optional callback invoked after a display decision has been recorded, for example to throw and exercise the failure containment.
	/// </summary>
	public Action<HoverInformation?, Diagnostic?>? OnDisplay { get; set; }

	/// <inheritdoc/>
	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
	{
		RequestOffsets.Add(request.HoveredOffset);
		RequestTokens.Add(cancellationToken);

		return RequestHoverAsync(request.HoveredOffset, cancellationToken);
	}

	/// <summary>Creates a hover controller wired to this host's pointer source, hooks, provider and dispatcher.</summary>
	/// <param name="logger">The optional logger for controller failures.</param>
	/// <returns>The created controller.</returns>
	public HoverController CreateController(ILogger? logger = null) => new(PointerSource, provider: this, hooks: CreateHooks(), Dispatcher, logger);

	/// <summary>Creates the hook set that records into this host.</summary>
	/// <returns>The hooks.</returns>
	public HoverControllerHooks CreateHooks()
	{
		return new HoverControllerHooks
		{
			BuildDecision = offset => BuildDecision(offset),
			BuildRequest = offset => BuildRequest(offset),
			ResolveRequestOffset = ResolveRequestOffset is null ? null : offset => ResolveRequestOffset(offset),
			ContextVersionProvider = ContextVersionProvider,

			ShowHover = (hoverInfo, diagnosticInfo, offset) =>
			{
				DisplayCalls.Add((hoverInfo, diagnosticInfo));
				DisplayOffsets.Add(offset);
				DisplayThreadIds.Add(Environment.CurrentManagedThreadId);

				OnDisplay?.Invoke(hoverInfo, diagnosticInfo);
			},
		};
	}

	/// <summary>Disposes the dispatcher, which restores the synchronization context it installed.</summary>
	public void Dispose() => Dispatcher.Dispose();
}
