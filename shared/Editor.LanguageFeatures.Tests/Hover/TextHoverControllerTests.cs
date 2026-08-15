#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Windows;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers the hover controller's basic display and hide decisions, constructor validation, and the shared
/// state/diagnostic helpers; the diagnostic-fallback and request-lifecycle concerns live in the
/// <c>TextHoverControllerTests.Diagnostics.cs</c> and <c>TextHoverControllerTests.RequestLifecycle.cs</c>
/// partials.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class TextHoverControllerTests
{
	[TestMethod]
	public async Task HandleMouseHoverAsync_WithoutCurrentRequestOffsetHook_UsesTheHoveredOffset()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// Without the optional hook the hovered offset doubles as the request offset.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_AfterDisposal_ReportsNoDisplayDecision()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		controller.Dispose();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A disposed controller must not report hover, diagnostic, or hide decisions.
		CollectionAssert.AreEqual(Array.Empty<(TextHoverInfo?, TextDiagnostic?)>(), host.DisplayCalls);
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_SuccessfulRequest_DisplaysTheHoverContent()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// The resolved hover content is reported through the display callback with no diagnostic.
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.AreEqual("hover", host.DisplayCalls[0].HoverInfo?.Content);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_NoHoveredOffset_RequestsNothingAndReportsHide()
	{
		using var host = new HoverTestHost
		{
			GetOffsetFromPoint = static _ => null
		};

		using var controller = host.CreateController();
		await controller.HandleMouseHoverAsync(HoverTestHost.CreateMouseEventArgs());

		// A hover that maps to no document offset supersedes pending work, requests nothing, and tells the
		// host to hide its tooltip.
		CollectionAssert.AreEqual(Array.Empty<int>(), host.RequestOffsets);
		Assert.AreEqual(1, host.DisplayCalls.Count);
		Assert.IsNull(host.DisplayCalls[0].HoverInfo);
		Assert.IsNull(host.DisplayCalls[0].DiagnosticInfo);
	}

	[TestMethod]
	public void Constructor_NullOwnerOrHooks_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();
		var hooks = new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = _ => CreateState(),
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowTooltip = static (_, _) => { }
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(null!, hooks));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, null!));
	}

	[TestMethod]
	public void Constructor_EachNullRequiredHook_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();

		ArgumentNullException offsetNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = null!,
			BuildEvaluationState = static _ => new TextHoverEvaluationState
			{
				ShouldRequestHover = true,
				RequestOffset = 5,
				CanShowHoverContent = true,
				CanShowDiagnosticFallback = false,
				DiagnosticInfo = null
			},
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowTooltip = static (_, _) => { }
		}));
		StringAssert.Contains(offsetNull.ParamName, "GetOffsetFromPoint");

		ArgumentNullException stateNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = null!,
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowTooltip = static (_, _) => { }
		}));
		StringAssert.Contains(stateNull.ParamName, "BuildEvaluationState");

		ArgumentNullException requestNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = static _ => new TextHoverEvaluationState
			{
				ShouldRequestHover = true,
				RequestOffset = 5,
				CanShowHoverContent = true,
				CanShowDiagnosticFallback = false,
				DiagnosticInfo = null
			},
			RequestHoverAsync = null!,
			ShowTooltip = static (_, _) => { }
		}));
		StringAssert.Contains(requestNull.ParamName, "RequestHoverAsync");

		ArgumentNullException showNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverController(host.Owner, new TextHoverControllerHooks
		{
			GetOffsetFromPoint = static _ => 5,
			BuildEvaluationState = static _ => new TextHoverEvaluationState
			{
				ShouldRequestHover = true,
				RequestOffset = 5,
				CanShowHoverContent = true,
				CanShowDiagnosticFallback = false,
				DiagnosticInfo = null
			},
			RequestHoverAsync = static (_, _) => Task.FromResult<TextHoverInfo?>(null),
			ShowTooltip = null!
		}));
		StringAssert.Contains(showNull.ParamName, "ShowTooltip");
	}

	[TestMethod]
	public async Task HandleMouseHoverAsync_NullEventArgs_ThrowsArgumentNullException()
	{
		using var host = new HoverTestHost();
		using var controller = host.CreateController();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => controller.HandleMouseHoverAsync(null!));
	}

	private static TextHoverEvaluationState CreateState(
		bool shouldRequestHover = true,
		int requestOffset = 5,
		bool canShowHoverContent = true,
		bool canShowDiagnosticFallback = false,
		TextDiagnostic? diagnosticInfo = null)
		=> new()
		{
			ShouldRequestHover = shouldRequestHover,
			RequestOffset = requestOffset,
			CanShowHoverContent = canShowHoverContent,
			CanShowDiagnosticFallback = canShowDiagnosticFallback,
			DiagnosticInfo = diagnosticInfo
		};

	private static TextDiagnostic CreateDiagnostic(
		string message = "diagnostic",
		TextDiagnosticSeverity severity = TextDiagnosticSeverity.Error)
		=> new(severity, message, 0, 1);
}
