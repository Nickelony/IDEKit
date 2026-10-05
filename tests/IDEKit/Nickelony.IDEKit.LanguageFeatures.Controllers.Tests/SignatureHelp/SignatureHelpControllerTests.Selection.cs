using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class SignatureHelpControllerTests
{
	[TestMethod]
	public async Task SelectNextSignature_WrapsAroundAndNotifiesHost()
	{
		var signature = new SignatureHelpInformation(
			[new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)"), new SignatureInformation("f(a, b, c)")],
			activeSignatureIndex: 2
		);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// The selection wraps from the last signature to the first one, and the host is asked to
		// show the updated payload again.
		Assert.IsTrue(controller.SelectNextSignature());
		Assert.AreEqual(0, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.AreEqual(0, host.ShownSignatures[^1].ActiveSignatureIndex);
		Assert.HasCount(2, host.ShownSignatures);
	}

	[TestMethod]
	public async Task SelectNextSignature_ShowsTheNextPayloadAtTheOffsetOfTheRequest()
	{
		var signature = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")]);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(7, cancellationToken: TestContext.CancellationToken);

		// The answer shows at the offset of its request, and an overload step shows the next payload where the popup already sits.
		Assert.IsTrue(controller.SelectNextSignature());
		Assert.AreSequenceEqual([7, 7], host.ShownOffsets);
	}

	[TestMethod]
	public async Task SelectPreviousSignature_WrapsAroundToLastSignature()
	{
		var signature = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")]);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.IsTrue(controller.SelectPreviousSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
	}

	[TestMethod]
	public async Task SelectNextSignature_CycleDisabledAtLastSignature_DismissesInsteadOfWrapping()
	{
		var signature = new SignatureHelpInformation(
			[new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")],
			activeSignatureIndex: 1
		);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController(SignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// With cycling disabled, navigating past the last signature dismisses the presentation instead of
		// wrapping to the first one; no wrapped payload is shown.
		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.AreEqual(1, host.DismissCount);
		Assert.HasCount(1, host.ShownSignatures);
	}

	[TestMethod]
	public async Task SelectPreviousSignature_CycleDisabledAtFirstSignature_DismissesInsteadOfWrapping()
	{
		var signature = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")]);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController(SignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.IsFalse(controller.SelectPreviousSignature());
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.AreEqual(1, host.DismissCount);
	}

	[TestMethod]
	public async Task SelectNextSignature_CycleDisabledBetweenSignatures_Advances()
	{
		var signature = new SignatureHelpInformation([new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")]);

		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		using SignatureHelpController controller = host.CreateController(SignatureHelpControllerOptions.Default with { Cycle = false });
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// The edge policy only applies at the edges; interior navigation still advances.
		Assert.IsTrue(controller.SelectNextSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.AreEqual(0, host.DismissCount);
	}

	[TestMethod]
	public void SelectNextSignature_WithoutVisibleSignature_ReturnsFalse()
	{
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null) };

		using SignatureHelpController controller = host.CreateController();

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.IsFalse(controller.SelectPreviousSignature());
	}

	[TestMethod]
	public async Task SelectNextSignature_SingleSignature_ReturnsFalse()
	{
		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		Assert.IsFalse(controller.SelectNextSignature());
		Assert.AreEqual(0, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
	}

	[TestMethod]
	public async Task SelectNextSignature_ThrowingShowCallback_KeepsPreviousSelection()
	{
		var logger = new RecordingLogger();

		var signature = new SignatureHelpInformation(
			[new SignatureInformation("f(a)"), new SignatureInformation("f(a, b)")],
			activeSignatureIndex: 1
		);

		int showCount = 0;

		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature),

			ShowCallback = _ =>
			{
				if (showCount++ > 0)
					throw new InvalidOperationException("Showing the updated payload failed.");
			},
		};

		using SignatureHelpController controller = host.CreateController(logger: logger);
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// A failing show callback must not leave the controller claiming a selection it never
		// established, and the failure carries the documented host-callback event id.
		Assert.IsFalse(controller.SelectNextSignature());
		Assert.AreEqual(1, controller.CurrentPresentation.SignatureHelp!.ActiveSignatureIndex);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1021, logger.Entries[0].EventId.Id);
	}
}
