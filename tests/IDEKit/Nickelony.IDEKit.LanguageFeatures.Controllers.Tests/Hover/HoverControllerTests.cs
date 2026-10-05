using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the hover controller's display and hide decisions, constructor validation, ownership of the pointer source, and shared helpers;
/// the diagnostic-fallback and request-lifecycle concerns live in the sibling partials.
/// </summary>
/// <remarks>
/// The tests share <see cref="HoverTestHost"/>, which scripts the pointer source and records what the controller reported to the host.
/// A test that awaits a pending provider pumps the dispatcher instead, because the host installs its synchronization context.
/// </remarks>
[TestClass]
public sealed partial class HoverControllerTests
{
	[TestMethod]
	public async Task RequestAsync_WithoutCurrentRequestOffsetHook_UsesTheHoveredOffset()
	{
		using var host = new HoverTestHost();
		using HoverController controller = host.CreateController();

		await controller.RequestAsync();

		// Without the optional hook the hovered offset doubles as the request offset.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.AreSequenceEqual([5], host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_AfterDisposal_ReportsNoDisplayDecision()
	{
		using var host = new HoverTestHost();
		using HoverController controller = host.CreateController();

		controller.Dispose();
		await controller.RequestAsync();

		// A disposed controller must not report hover, diagnostic, or hide decisions.
		Assert.AreSequenceEqual(Array.Empty<(HoverInformation?, Diagnostic?)>(), host.DisplayCalls);
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.AreEqual(0, host.PointerSource.ReadCount);
	}

	[TestMethod]
	public async Task RequestAsync_SuccessfulRequest_DisplaysTheHoverContent()
	{
		using var host = new HoverTestHost();
		using HoverController controller = host.CreateController();

		await controller.RequestAsync();

		// The resolved hover content is reported through the display callback with no diagnostic.
		Assert.HasCount(1, host.DisplayCalls);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_NoHoveredOffset_RequestsNothingAndReportsHide()
	{
		using var host = new HoverTestHost();
		host.PointerSource.Offset = static () => null;

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// A hover that maps to no document offset supersedes pending work, requests nothing, and tells the
		// host to hide its hover.
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_BuildRequestReturnsNull_RequestsNothingAndShowsTheDiagnosticFallback()
	{
		Diagnostic diagnostic = CreateDiagnostic();

		using var host = new HoverTestHost
		{
			BuildRequest = static _ => null,
			BuildDecision = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: diagnostic),
		};

		using HoverController controller = host.CreateController();

		await controller.RequestAsync();

		// A vetoed request concludes like an evaluation that declined it: nothing reaches the provider, and the diagnostic fallback shows.
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreSame(diagnostic, host.DisplayCalls[0].Diagnostic);
	}

	[TestMethod]
	public async Task RequestAsync_WithoutABuildDecision_RequestsAtTheHoveredOffsetAndShowsTheContent()
	{
		using var host = new HoverTestHost();
		host.PointerSource.Offset = static () => 3;

		HoverControllerHooks hooks = host.CreateHooks();

		using var controller = new HoverController(
			pointerSource: host.PointerSource,
			provider: host,
			hooks: new HoverControllerHooks { BuildRequest = hooks.BuildRequest, ShowHover = hooks.ShowHover },
			dispatcher: host.Dispatcher
		);

		await controller.RequestAsync();

		Assert.AreSequenceEqual([3], host.RequestOffsets);
		Assert.HasCount(1, host.DisplayCalls);
		Assert.IsNotNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.AreSequenceEqual([3], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_CompletedRequest_ShowsTheHoverAtTheRequestOffset()
	{
		using var host = new HoverTestHost { BuildDecision = static _ => CreateState(requestOffset: 7), ResolveRequestOffset = static _ => 7 };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// The pointer rests over offset 5, and the host remapped the request to offset 7, which the hover anchors at.
		Assert.AreSequenceEqual([7], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_DeclinedRequest_ShowsTheFallbackAtTheHoveredOffset()
	{
		using var host = new HoverTestHost
		{
			BuildDecision = static _ =>
				CreateState(shouldRequestHover: false, requestOffset: 0, canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
		};

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// A decision that asks for no request carries no meaningful request offset, so the hover targets the hovered offset.
		Assert.AreSequenceEqual([5], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_NoHoveredOffset_ReportsNoTarget()
	{
		using var host = new HoverTestHost();
		host.PointerSource.Offset = static () => null;

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		Assert.AreSequenceEqual([null], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_RequestBuilderThrows_ShowsTheFallbackAtTheRequestOffset()
	{
		Diagnostic diagnostic = CreateDiagnostic();

		using var host = new HoverTestHost
		{
			BuildDecision = _ => CreateState(requestOffset: 7, canShowDiagnosticFallback: true, diagnosticInfo: diagnostic),
			BuildRequest = static _ => throw new InvalidOperationException("request builder failed"),
			ResolveRequestOffset = static _ => 7,
		};

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// The decision asked for a request at offset 7 before the builder failed, so the fallback anchors where the request would have.
		Assert.AreSame(diagnostic, host.DisplayCalls[0].Diagnostic);
		Assert.AreSequenceEqual([7], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_DecisionBuilderThrows_HidesAtTheHoveredOffset()
	{
		using var host = new HoverTestHost { BuildDecision = static _ => throw new InvalidOperationException("decision builder failed") };

		using HoverController controller = host.CreateController();
		await controller.RequestAsync();

		// No decision exists, so the hide request targets the offset under the pointer.
		Assert.IsNull(host.DisplayCalls[0].Diagnostic);
		Assert.AreSequenceEqual([5], host.DisplayOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_RequestOffset_ReachesBuildRequestAndTheProviderRequest()
	{
		var builtOffsets = new List<int>();

		using var host = new HoverTestHost
		{
			BuildDecision = static _ => CreateState(requestOffset: 7),

			BuildRequest = offset =>
			{
				builtOffsets.Add(offset);
				return new HoverRequest(HoverTestHost.DocumentId, HoverTestHost.DocumentText, offset);
			},

			ResolveRequestOffset = static _ => 7,
		};

		using HoverController controller = host.CreateController();

		await controller.RequestAsync();

		// The request offset of the evaluation state, not the hovered offset, is what the host builds the request from.
		Assert.AreSequenceEqual([7], builtOffsets);
		Assert.AreSequenceEqual([7], host.RequestOffsets);
	}

	[TestMethod]
	public void Constructor_NullSourceHooksOrDispatcher_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();

		IHoverPointerSource pointerSource = host.PointerSource;
		IHoverProvider provider = host;
		HoverControllerHooks hooks = CreateHooks();
		IUiDispatcher dispatcher = host.Dispatcher;

		Assert.ThrowsExactly<ArgumentNullException>(() => new HoverController(pointerSource: null!, provider, hooks, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new HoverController(pointerSource, provider, hooks: null!, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new HoverController(pointerSource, provider, hooks, dispatcher: null!));
	}

	[TestMethod]
	public void Constructor_EachNullRequiredHook_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();

		IHoverPointerSource pointerSource = host.PointerSource;
		IHoverProvider provider = host;
		IUiDispatcher dispatcher = host.Dispatcher;

		ArgumentNullException Reject(HoverControllerHooks hooks) =>
			Assert.ThrowsExactly<ArgumentNullException>(() => new HoverController(pointerSource, provider, hooks, dispatcher));

		string? requestParameter = Reject(
			new HoverControllerHooks
			{
				BuildDecision = static _ => CreateState(),
				BuildRequest = null!,
				ShowHover = static (_, _, _) => { },
			}
		).ParamName;

		Assert.IsNotNull(requestParameter);
		Assert.Contains("BuildRequest", requestParameter);

		string? showParameter = Reject(
			new HoverControllerHooks
			{
				BuildDecision = static _ => CreateState(),
				BuildRequest = static _ => null,
				ShowHover = null!,
			}
		).ParamName;

		Assert.IsNotNull(showParameter);
		Assert.Contains("ShowHover", showParameter);
	}

	[TestMethod]
	public void Constructor_RejectedArguments_LeaveTheSourceToTheCaller()
	{
		using var host = new HoverTestHost();

		IHoverPointerSource pointerSource = host.PointerSource;
		IHoverProvider provider = host;
		HoverControllerHooks hooks = CreateHooks();
		IUiDispatcher dispatcher = host.Dispatcher;

		Assert.ThrowsExactly<ArgumentNullException>(() => new HoverController(pointerSource, provider, hooks: null!, dispatcher));

		// The controller owns the source only once its constructor returns, so a rejected call leaves the disposal to the caller.
		Assert.AreEqual(0, host.PointerSource.DisposeCount);
	}

	[TestMethod]
	public void Constructor_OffTheDispatcherThread_ThrowsInvalidOperationException()
	{
		using var host = new HoverTestHost();

		IHoverPointerSource pointerSource = host.PointerSource;
		IHoverProvider provider = host;
		HoverControllerHooks hooks = CreateHooks();
		IUiDispatcher dispatcher = host.Dispatcher;

		Exception? failure = CaptureOffThreadFailure(() => new HoverController(pointerSource, provider, hooks, dispatcher));

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
		Assert.AreEqual(0, host.PointerSource.DisposeCount);
	}

	[TestMethod]
	public void Dispose_DisposesThePointerSourceOnce()
	{
		using var host = new HoverTestHost();

		HoverController controller = host.CreateController();

		controller.Dispose();
		controller.Dispose();

		// The controller owns the source: it disposes it with itself, and a second disposal does not dispose it again.
		Assert.AreEqual(1, host.PointerSource.DisposeCount);
	}

	[TestMethod]
	public void RequestAsync_OffTheDispatcherThread_FaultsTheReturnedTask()
	{
		using var host = new HoverTestHost();
		using HoverController controller = host.CreateController();

		Task? request = null;

		Exception? failure = CaptureOffThreadFailure(() => request = controller.RequestAsync());

		// The pointer source and the display callback are thread-affine, so the request fails fast off the dispatcher's thread: the
		// asynchronous member reports the failure through the task it returns.
		Assert.IsNull(failure);
		Assert.IsNotNull(request);
		Assert.IsTrue(request.IsFaulted);
		Assert.IsInstanceOfType<InvalidOperationException>(request.Exception?.InnerException);
		Assert.AreEqual(0, host.PointerSource.ReadCount);
	}

	[TestMethod]
	public void PublicOperations_FromAnotherThreadAfterDisposal_AreQuietNoOps()
	{
		using var host = new HoverTestHost();
		HoverController controller = host.CreateController();

		controller.Dispose();

		Task? request = null;

		// After disposal every member returns before the dispatcher-affinity check, so the calls are safe from any thread.
		Exception? failure = CaptureOffThreadFailure(() =>
		{
			request = controller.RequestAsync();
			controller.CancelInFlightRequest();
			controller.InvalidateRequests();
			controller.Dispose();
		});

		Assert.IsNull(failure);
		Assert.IsNotNull(request);
		Assert.IsTrue(request.IsCompletedSuccessfully);
		Assert.AreEqual(1, host.PointerSource.DisposeCount);
	}

	// A dedicated thread runs genuinely off the controller's dispatcher thread, and it carries no synchronization context.
	private static Exception? CaptureOffThreadFailure(Action action)
	{
		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		return failure;
	}

	private static HoverControllerHooks CreateHooks()
	{
		return new()
		{
			BuildDecision = static _ => CreateState(),
			BuildRequest = static _ => null,
			ShowHover = static (_, _, _) => { },
		};
	}

	private static HoverDecision CreateState(
		bool shouldRequestHover = true,
		int requestOffset = 5,
		bool canShowHoverContent = true,
		bool canShowDiagnosticFallback = false,
		Diagnostic? diagnosticInfo = null
	)
	{
		return new()
		{
			ShouldRequestHover = shouldRequestHover,
			RequestOffset = requestOffset,
			CanShowHoverContent = canShowHoverContent,
			CanShowDiagnosticFallback = canShowDiagnosticFallback,
			Diagnostic = diagnosticInfo,
		};
	}

	private static Diagnostic CreateDiagnostic(string message = "diagnostic", DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
		new(severity, message, new TextRange(0, 1));
}
