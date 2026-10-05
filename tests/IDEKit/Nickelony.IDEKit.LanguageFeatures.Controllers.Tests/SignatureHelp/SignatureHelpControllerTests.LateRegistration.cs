using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class SignatureHelpControllerTests
{
	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void Dismiss_WhileProviderInFlight_LateProviderContinuationDoesNotThrow(bool installSynchronizationContext)
	{
		var completion = new TaskCompletionSource<SignatureHelpInformation?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool observedCanceledRegistration = false;

		using var host = new SignatureHelpTestHost(installSynchronizationContext)
		{
			RequestSignatureHelpAsync = async (_, _, cancellationToken) =>
			{
				await completion.Task.ConfigureAwait(true);

				// The provider observes the already-canceled token after the dismissal; registering on it
				// must report cancellation instead of throwing ObjectDisposedException, and the late result
				// must not be applied.
				using CancellationTokenRegistration registration = cancellationToken.Register(static () => { });
				observedCanceledRegistration = true;

				return null;
			},
		};

		using SignatureHelpController controller = host.CreateController();

		Task request = controller.RequestAsync(0, cancellationToken: TestContext.CancellationToken);
		controller.Dismiss();
		completion.SetResult(null);

		host.Dispatcher.PumpUntil(() => request.IsCompleted);

		Assert.IsTrue(observedCanceledRegistration);
		Assert.IsTrue(request.IsCompletedSuccessfully);
		Assert.IsEmpty(host.ShownSignatures);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}
}
