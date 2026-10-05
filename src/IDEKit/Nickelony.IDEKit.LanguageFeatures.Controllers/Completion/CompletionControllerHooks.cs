namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Groups the optional host hooks used by the <see cref="CompletionController"/>.</summary>
/// <remarks>
/// Every hook is optional. Without hooks, every offset counts as code and no host state guards a request. The hooks that restyle the window,
/// map the items and build the tooltip belong to an editor binding's presenter, which takes its own hook bag.
/// </remarks>
public sealed class CompletionControllerHooks
{
	/// <summary>
	/// The hook that answers the scope of the character typed before a caret offset, or <see langword="null"/> to treat every offset
	/// as <see cref="CompletionScopes.Code"/>.
	/// </summary>
	/// <remarks>
	/// The controller calls it on its dispatcher's thread after each typed character, with the caret offset after the character,
	/// and the <see cref="CompletionControllerOptions.Triggers"/> rule decides from the answer whether the character opens completion.
	/// An answer of <see cref="CompletionScopes.None"/> opens nothing, trigger characters included, so the hook also turns
	/// typing triggers off at run time, for example while a host's completion setting is off. Telling a comment from code is language work,
	/// so the hook belongs to the host, and it must be cheap: a hook that rescans the document on each keystroke slows typing in a large file.
	/// A failure it throws is logged, and the character opens no completion.
	/// </remarks>
	public Func<int, CompletionScopes>? ScopeAt { get; init; }

	/// <summary>The optional provider of a host context version that guards asynchronous completion work.</summary>
	/// <remarks>
	/// The controller captures the version when <see cref="CompletionController.RequestAsync"/> starts and discards the completed decision
	/// when the version changed while the request was in flight. The provider must be cheap to call and return a value that changes when pending work
	/// admitted for an earlier host state must be abandoned, for example a document session, load, or replacement generation counter.
	/// </remarks>
	public Func<int>? ContextVersionProvider { get; init; }
}
