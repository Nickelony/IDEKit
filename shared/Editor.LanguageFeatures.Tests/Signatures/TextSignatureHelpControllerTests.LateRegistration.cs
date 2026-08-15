#if AVALONIAEDIT
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.IntelliSense.Signatures;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextSignatureHelpControllerTests
{
	[TestMethod]
	public void Dismiss_WhileProviderInFlight_LateProviderContinuationDoesNotThrow()
	{
		var completion = new TaskCompletionSource<TextSignatureHelp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool observedCanceledRegistration = false;

		var host = new SignatureHelpTestHost
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
			}
		};

		using var controller = host.CreateController();

		Task request = controller.RequestAsync(0);
		controller.Dismiss();
		completion.SetResult(null);

		DispatcherTestUtils.PumpUntil(() => request.IsCompleted);

		Assert.IsTrue(observedCanceledRegistration);
		Assert.IsTrue(request.IsCompletedSuccessfully);
		Assert.AreEqual(0, host.ShownSignatures.Count);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}
}
