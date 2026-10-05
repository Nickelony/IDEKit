using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;

/// <summary>Stores the options used by a project diagnostics source.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned,
/// so an invalid value fails at the offending expression rather than at source construction.
/// </remarks>
public sealed record ProjectDiagnosticsSourceOptions
{
	private readonly TimeSpan _settleDelay = TimeSpan.FromMilliseconds(500.0);

	/// <summary>
	/// How long the project must stay unchanged after a reported change before the source analyzes it; defaults to 500 milliseconds.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Every reported change restarts the delay, so typing analyzes once, after the last keystroke.
	/// </remarks>
	public TimeSpan SettleDelay
	{
		get => _settleDelay;
		init => _settleDelay = NumericValidation.NonNegative(value, nameof(SettleDelay));
	}

	/// <summary>
	/// Compares the document ids of the analysis result with the ids that <see cref="ProjectDiagnosticsSource.GetDiagnostics"/> receives,
	/// or <see langword="null"/> for an ordinal comparison.
	/// </summary>
	/// <remarks>
	/// A host whose ids are file paths on a case-insensitive file system passes the comparer that its path policy uses, so an editor
	/// that spells a path in another case still finds its document. Ids of one result that the comparer treats as equal name one document,
	/// and the source adds up their diagnostics under the first spelling. An editor's subscription compares ids with its own comparer,
	/// which the <c>documentIdComparer</c> parameter of <c>Subscribe</c> and <see cref="DiagnosticsControllerOptions.DocumentIdComparer"/> set.
	/// </remarks>
	public IEqualityComparer<string>? DocumentIdComparer { get; init; }

	public static ProjectDiagnosticsSourceOptions Default { get; } = new();
}
