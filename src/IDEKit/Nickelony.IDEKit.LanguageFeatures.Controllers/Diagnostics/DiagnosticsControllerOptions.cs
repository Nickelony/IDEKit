using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>Stores the options used by a diagnostics controller.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned,
/// so an invalid value fails at the offending expression rather than at controller construction.
/// </remarks>
public sealed record DiagnosticsControllerOptions
{
	private readonly TimeSpan _settleDelay = TimeSpan.FromMilliseconds(300.0);

	/// <summary>How long the text must stay unchanged after an edit before the controller pulls diagnostics; defaults to 300 milliseconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Every edit restarts the delay, so typing pulls once, after the last keystroke.
	/// </remarks>
	public TimeSpan SettleDelay
	{
		get => _settleDelay;
		init => _settleDelay = NumericValidation.NonNegative(value, nameof(SettleDelay));
	}

	/// <summary>
	/// Compares the document ids of a source's updates with the controller's document id, or <see langword="null"/> for an ordinal comparison.
	/// </summary>
	/// <remarks>
	/// The language-server provider names each document by the id the host opened it with, so the ordinal default matches its updates;
	/// a source that spells the ids another way needs a comparer that matches its spelling.
	/// </remarks>
	public IEqualityComparer<string>? DocumentIdComparer { get; init; }

	public static DiagnosticsControllerOptions Default { get; } = new();
}
