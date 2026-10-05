using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// A completion request whose provider waits on a gate, so a test can act on the controller while the request is in flight;
/// the provider then answers one item, which opens a window.
/// </summary>
internal sealed class GatedCompletionRequest : IDisposable
{
	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <param name="controller">The controller that runs the request.</param>
	/// <param name="provider">The provider of <paramref name="controller"/>, whose answer the gate holds back.</param>
	/// <param name="label">The label of the item the provider answers.</param>
	internal GatedCompletionRequest(CompletionController controller, ScriptedCompletionProvider provider, string label = "sample")
	{
		provider.Answer = async (_, cancellationToken) =>
		{
			Token = cancellationToken;
			await _gate.Task.ConfigureAwait(true);

			return [new CompletionItem(label)];
		};

		Result = controller.RequestAsync();
	}

	/// <summary>The task that <see cref="CompletionController.RequestAsync"/> returned.</summary>
	internal Task<bool> Result { get; }

	/// <summary>The cancellation token the controller gave the provider.</summary>
	internal CancellationToken Token { get; private set; }

	/// <summary>Lets the provider return its decision.</summary>
	internal void Release() => _gate.TrySetResult();

	/// <summary>Releases the provider, so a test that fails before it releases the gate leaves no pending request behind.</summary>
	public void Dispose() => Release();
}
