using Nickelony.Infrastructure;

namespace Nickelony.IDEKit.Core.Paths;

/// <summary>Selects the comparison used for local file-system identity paths.</summary>
/// <param name="IgnoreCase"><see langword="true"/> to compare identity paths case-insensitively.</param>
/// <remarks>
/// <see cref="ForCurrentPlatform"/> is an operating-system assumption, not a probe of the actual volume: case-insensitive on Windows and macOS
/// and ordinal elsewhere. The policy covers case only - no path or Unicode normalization, so NFC and NFD spellings compare unequal and a caller
/// keying a store by path must normalize it. Every component that resolves identities together needs the same value.
/// </remarks>
public readonly record struct LocalPathComparisonPolicy(bool IgnoreCase)
{
	/// <summary>The comparison for the current operating system's usual file-system semantics.</summary>
	public static LocalPathComparisonPolicy ForCurrentPlatform => new(LocalPathRules.IgnoresCaseByDefault);

	public static LocalPathComparisonPolicy CaseInsensitive => new(true);

	/// <summary>An ordinal, case-sensitive comparison.</summary>
	public static LocalPathComparisonPolicy CaseSensitive => new(false);

	/// <summary>The <see cref="StringComparison"/> form of the policy.</summary>
	public StringComparison Comparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>The <see cref="StringComparer"/> form of the policy.</summary>
	public StringComparer Comparer => IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
