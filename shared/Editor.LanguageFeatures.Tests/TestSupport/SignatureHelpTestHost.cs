#if AVALONIAEDIT
using Avalonia.Threading;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Windows.Threading;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Builds a signature help controller over mutable hooks and records every host callback the controller
/// makes, so the tests do not repeat the hook set.
/// </summary>
internal sealed class SignatureHelpTestHost
{
	/// <summary>Gets the payloads the controller asked the host to show, in order.</summary>
	public List<TextSignatureHelp> ShownSignatures { get; } = [];

	/// <summary>Gets the request contexts the controller reported to the provider, in order.</summary>
	public List<TextSignatureHelpContext> Contexts { get; } = [];

	/// <summary>Gets the offsets the controller requested, in order.</summary>
	public List<int> RequestOffsets { get; } = [];

	/// <summary>Gets the cancellation tokens the controller passed to the provider, in order.</summary>
	public List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>Gets the number of times the controller invoked the dismiss callback.</summary>
	public int DismissCount { get; private set; }

	/// <summary>Gets or sets the caret-offset hook. Defaults to offset <c>0</c>.</summary>
	public Func<int> GetCurrentCaretOffset { get; set; } = static () => 0;

	/// <summary>Gets or sets the provider hook. Defaults to a null result.</summary>
	public Func<int, TextSignatureHelpContext, CancellationToken, Task<TextSignatureHelp?>> RequestSignatureHelpAsync { get; set; } =
		static (_, _, _) => Task.FromResult<TextSignatureHelp?>(null);

	/// <summary>Gets or sets an optional callback invoked after the show callback recorded the payload.</summary>
	public Action<TextSignatureHelp>? ShowCallback { get; set; }

	/// <summary>Gets or sets an optional callback invoked after the dismiss invocation was recorded.</summary>
	public Action? DismissCallback { get; set; }

	/// <summary>
	/// Creates a controller wired to this host's hooks.
	/// </summary>
	/// <param name="options">The controller options, or <see langword="null"/> for the defaults.</param>
	/// <param name="logger">The optional logger for controller failures.</param>
	/// <param name="dispatcher">The optional dispatcher the refresh timer and continuations run on.</param>
	/// <returns>The created controller.</returns>
	public TextSignatureHelpController CreateController(
		TextSignatureHelpControllerOptions? options = null,
		Microsoft.Extensions.Logging.ILogger? logger = null,
		Dispatcher? dispatcher = null)
		=> new(
			new TextSignatureHelpControllerHooks
			{
				GetCurrentCaretOffset = () => GetCurrentCaretOffset(),
				RequestSignatureHelpAsync = (offset, context, cancellationToken) =>
				{
					RequestOffsets.Add(offset);
					Contexts.Add(context);
					RequestTokens.Add(cancellationToken);
					return RequestSignatureHelpAsync(offset, context, cancellationToken);
				},
				ShowSignatureHelp = signatureInfo =>
				{
					ShownSignatures.Add(signatureInfo);
					ShowCallback?.Invoke(signatureInfo);
				},
				DismissSignatureHelp = () =>
				{
					DismissCount++;
					DismissCallback?.Invoke();
				}
			},
			options,
			logger,
			dispatcher);
}
