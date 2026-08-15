#if AVALONIAEDIT
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers the hover controller's diagnostic-fallback and failure-containment behavior: which display
/// decision a provider failure, an unavailable tooltip, or a canceled evaluation resolves to, and which
/// failures are reported through the documented event ids.
/// </summary>
public sealed partial class TextHoverControllerTests
{
	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingProvider_ReportsTheDiagnosticFallbackWithoutThrowing()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed.")
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A provider failure must not propagate; available diagnostic content is shown instead, and the
		// failure carries the documented hover request event id.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingOffsetResolution_LogsTheFailureWithoutDisplayDecision()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			GetOffsetFromPoint = _ => throw new InvalidOperationException("Offset resolution failed.")
		};

		using var controller = host.CreateController(logger);

		// A failure before a hovered offset exists has no display target, so only the log reports it and
		// the host tooltip is not touched.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingDisplayCallback_IsContained()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed."),
			OnDisplay = (_, _) => throw new InvalidOperationException("Diagnostic tooltip failed.")
		};

		using var controller = host.CreateController(logger);

		// The provider failure falls back to the diagnostic tooltip, whose own failure must be contained
		// instead of escaping the hover evaluation, and reported with the host-callback event id.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNotNull(host.DisplayCalls[0].DiagnosticInfo);

		// The provider failure is reported first, then the callback failure of the diagnostic fallback.
		Assert.AreEqual(2, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
		Assert.AreEqual(1011, logger.Entries[1].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ContextVersionProviderThrowing_IsContainedAndFallsBackToTheDiagnostic()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's liveness check then
				// fails with a non-cancellation exception.
				if (contextVersionCalls > 1)
					throw new InvalidOperationException("The host session ended.");

				return 1;
			}
		};

		var logger = new CapturingLogger();
		using var controller = host.CreateController(logger);

		// A throwing liveness check is contained like a provider failure: the evaluation falls back to the
		// diagnostic tooltip allowed by the initial state and the failure is logged.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequestWithFallbackDisabled_StillCombinesBoth()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: false, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The fallback flag gates only the not-made and failed paths; a successful request combines the
		// hover content with the diagnostic even when the fallback is disabled.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequestWithDiagnostic_DisplaysBothInOneCall()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A successful hover next to diagnostic content is reported as one combined display decision.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_HoverNotRequested_DisplaysTheDiagnosticFallbackAndSkipsTheProvider()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(
				shouldRequestHover: false,
				requestOffset: -1,
				canShowHoverContent: false,
				canShowDiagnosticFallback: true,
				diagnosticInfo: CreateDiagnostic(severity: TextDiagnosticSeverity.Warning))
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// When hover is not applicable, only the diagnostic tooltip is shown and no provider call is made.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.AreEqual("diagnostic", host.DisplayCalls[0].DiagnosticInfo?.Message);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ContextVersionProviderThrowingOperationCanceled_EndsSilently()
	{
		int contextVersionCalls = 0;

		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowDiagnosticFallback: true, diagnosticInfo: CreateDiagnostic()),
			ContextVersionProvider = () =>
			{
				contextVersionCalls++;

				// The first call captures the version before the request; the host's session ends by the
				// time the completed result is checked, so the check cancels the evaluation.
				if (contextVersionCalls > 1)
					throw new OperationCanceledException();

				return 1;
			}
		};

		using var controller = host.CreateController();

		// The explicit cancellation must not escape and must not fall back to the diagnostic tooltip,
		// even though fallback content is available.
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ProviderThrowingOperationCanceled_EndsWithoutDisplay()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			RequestHoverAsync = static (_, _) => throw new OperationCanceledException()
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A canceled provider reports cancellation rather than a failure: nothing is displayed and nothing
		// is logged.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<string>(), logger.Messages);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_CompletedRequestWithHoverContentForbidden_ReportsHide()
	{
		using var host = new HoverTestHost
		{
			BuildEvaluationState = _ => CreateState(canShowHoverContent: false, diagnosticInfo: CreateDiagnostic())
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A completed request whose display state forbids hover content resolves to a hide decision, even
		// when a diagnostic was resolved alongside it.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_ThrowingProviderWithoutDiagnosticFallback_ReportsHide()
	{
		var logger = new CapturingLogger();
		using var host = new HoverTestHost
		{
			RequestHoverAsync = (_, _) => throw new InvalidOperationException("Hover failed.")
		};

		using var controller = host.CreateController(logger);
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// Without fallback permission the failure resolves to a hide decision, and the failure is still
		// reported with the documented request event id.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1010, logger.Entries[0].EventId.Id);
	}
}
