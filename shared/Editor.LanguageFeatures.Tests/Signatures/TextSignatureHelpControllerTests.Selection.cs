#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextSignatureHelpControllerTests
{
	[TestMethod]
	public async Task SelectNextSignature_WrapsAroundAndNotifiesHost()
	{
		var signature = new TextSignatureHelp(
			[
				new TextSignatureInformation("f(a)"),
				new TextSignatureInformation("f(a, b)"),
				new TextSignatureInformation("f(a, b, c)")
			],
			activeSignatureIndex: 2);

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		// The selection wraps from the last signature to the first one, and the host is asked to
		// show the updated payload again.
		Assert.IsTrue(controller.SelectNextSignature());
		Assert.AreEqual(0, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.AreEqual(0, host.ShownSignatures[^1].ActiveSignatureIndex);
		Assert.AreEqual(2, host.ShownSignatures.Count);
	}

	[TestMethod]
	public async Task SelectPreviousSignature_WrapsAroundToLastSignature()
	{
		var signature = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("f(a, b)")]);

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		Assert.IsTrue(controller.SelectPreviousSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
	}

	[TestMethod]
	public async Task SelectNextSignature_CycleDisabledAtLastSignature_DismissesInsteadOfWrapping()
	{
		var signature = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("f(a, b)")],
			activeSignatureIndex: 1);

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController(TextSignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5);

		// With cycling disabled, navigating past the last signature dismisses the presentation instead of
		// wrapping to the first one; no wrapped payload is shown.
		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.AreEqual(1, host.DismissCount);
		Assert.AreEqual(1, host.ShownSignatures.Count);
	}

	[TestMethod]
	public async Task SelectPreviousSignature_CycleDisabledAtFirstSignature_DismissesInsteadOfWrapping()
	{
		var signature = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("f(a, b)")]);

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController(TextSignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5);

		Assert.IsFalse(controller.SelectPreviousSignature());
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.AreEqual(1, host.DismissCount);
	}

	[TestMethod]
	public async Task SelectNextSignature_CycleDisabledBetweenSignatures_Advances()
	{
		var signature = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("f(a, b)")]);

		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		using var controller = host.CreateController(TextSignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5);

		// The edge policy only applies at the edges; interior navigation still advances.
		Assert.IsTrue(controller.SelectNextSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.AreEqual(0, host.DismissCount);
	}

	[TestMethod]
	public void SelectNextSignature_WithoutVisibleSignature_ReturnsFalse()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		using var controller = host.CreateController();

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.SelectPreviousSignature());
	}

	[TestMethod]
	public async Task SelectNextSignature_SingleSignature_ReturnsFalse()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.AreEqual(0, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
	}

	[TestMethod]
	public async Task SelectNextSignature_ThrowingShowCallback_KeepsPreviousSelection()
	{
		var logger = new CapturingLogger();
		var signature = new TextSignatureHelp(
			[new TextSignatureInformation("f(a)"), new TextSignatureInformation("f(a, b)")],
			activeSignatureIndex: 1);

		int showCount = 0;
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature),
			ShowCallback = _ =>
			{
				if (showCount++ > 0)
					throw new InvalidOperationException("Showing the updated payload failed.");
			}
		};

		using var controller = host.CreateController(logger: logger);
		await controller.RequestAsync(5);

		// A failing show callback must not leave the controller claiming a selection it never
		// established, and the failure carries the documented host-callback event id.
		Assert.IsFalse(controller.SelectNextSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.AreEqual(1, logger.Entries.Count);
		Assert.AreEqual(1021, logger.Entries[0].EventId.Id);
	}
}
