using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// A presenter that records the decisions it was given and the threads it was called on, and shows a list for any decision that carries items.
/// </summary>
internal sealed class RecordingCompletionPresenter : ICompletionPresenter
{
	/// <summary>The decisions the controller handed over, in order.</summary>
	internal List<CompletionSessionDirective> Applied { get; } = [];

	/// <summary>The threads <see cref="ApplyDirective"/> ran on, in order.</summary>
	internal List<int> ApplyThreadIds { get; } = [];

	/// <summary>The threads <see cref="Dismiss"/> ran on, in order.</summary>
	internal List<int> DismissThreadIds { get; } = [];

	/// <summary>Replaces what <see cref="ApplyDirective"/> reports; the default reports that the decision carries items.</summary>
	internal Func<CompletionSessionDirective, bool>? OnApply { get; set; }

	public bool IsListVisible { get; set; }

	public bool IsTooltipVisible { get; set; }

	public CompletionTooltipContent? TooltipContent { get; set; }

	public bool ApplyDirective(CompletionSessionDirective directive)
	{
		Applied.Add(directive);
		ApplyThreadIds.Add(Environment.CurrentManagedThreadId);

		if (directive.ShouldClose)
			IsListVisible = false;

		bool opened = OnApply?.Invoke(directive) ?? directive.Items is { Count: > 0 };

		IsListVisible |= opened;

		return opened;
	}

	public void Dismiss()
	{
		DismissThreadIds.Add(Environment.CurrentManagedThreadId);

		IsListVisible = false;
		IsTooltipVisible = false;
		TooltipContent = null;
	}
}
