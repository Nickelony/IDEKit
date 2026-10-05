namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Builds the requests of the auto-closing planner tests and runs them through <see cref="AutoClosingPlanner"/>.</summary>
internal static class AutoClosingPlannerTestHelpers
{
	/// <summary>The default options, which apply the skip and the pair deletion to tracked closing text only.</summary>
	internal static AutoClosingOptions DefaultOptions { get; } = AutoClosingOptions.Default;

	/// <summary>The default options with the skip and the pair deletion applied whenever the text matches the pair.</summary>
	internal static AutoClosingOptions AlwaysTrackedOptions { get; } =
		AutoClosingOptions.Default with
		{
			ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
			PairDeletionTracking = AutoClosingTrackingMode.Always,
		};

	/// <summary>Plans the action for typing the input at the caret of the text.</summary>
	/// <returns>Whether the planner plans an action.</returns>
	internal static bool Resolve(
		string text,
		int caretOffset,
		string input,
		out AutoClosingAction action,
		AutoClosingOptions? options = null,
		Func<int, bool>? isTrackedClosingText = null,
		bool wrappingSelection = false
	)
	{
		return AutoClosingPlanner.TryPlanAction(
			new AutoClosingRequest(Snapshot: new StringTextSnapshot(text), caretOffset, InputText: input, Options: options ?? DefaultOptions)
			{
				IsWrappingSelection = wrappingSelection,
				IsTrackedClosingText = isTrackedClosingText,
			},

			out action
		);
	}

	/// <summary>Creates options that hold only the given pairs.</summary>
	internal static AutoClosingOptions CreateOptions(params AutoClosingPair[] pairs) => new() { Pairs = pairs };
}
