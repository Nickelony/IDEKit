namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class TextAutoClosingResolverTests
{
	private static readonly TextAutoClosingOptions s_options = TextAutoClosingOptions.Default;

	private static readonly TextAutoClosingOptions s_alwaysOptions = TextAutoClosingOptions.Default with
	{
		ClosingTextSkipProvenance = TextAutoClosingProvenance.Always,
		PairDeletionProvenance = TextAutoClosingProvenance.Always
	};

	private static bool Resolve(
		string text,
		int caretOffset,
		string input,
		out TextAutoClosingAction action,
		TextAutoClosingOptions? options = null,
		Func<int, bool>? isTrackedClosingText = null,
		bool wrappingSelection = false)
		=> TextAutoClosingResolver.TryResolveAction(
			new TextAutoClosingRequest(
				new StringTextSnapshot(text),
				caretOffset,
				input,
				options ?? s_options)
			{
				IsWrappingSelection = wrappingSelection,
				IsTrackedClosingText = isTrackedClosingText
			},
			out action);

	private static TextAutoClosingOptions CreateOptions(params TextAutoClosingPair[] pairs)
		=> new() { Pairs = pairs };
}
