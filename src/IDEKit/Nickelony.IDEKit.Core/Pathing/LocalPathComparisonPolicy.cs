namespace Nickelony.IDEKit.Core.Pathing;

/// <summary>
/// Selects the comparison used for local file-system identity paths.
/// </summary>
/// <remarks>
/// <para>
/// The usual file-system semantics follow the operating system: case-insensitive on Windows and
/// macOS, and ordinal on other platforms (<see cref="ForCurrentPlatform"/>). This is an
/// operating-system assumption, not a probe of the actual volume: a host that tracks paths on a
/// case-sensitive macOS or Linux volume must pass <see cref="CaseSensitive"/> explicitly, and a
/// case-insensitive mount can pass <see cref="CaseInsensitive"/>.
/// </para>
/// <para>
/// The policy covers case only. It does not normalize paths, and it does not model the Unicode
/// normalization that some macOS volumes apply: two spellings of one file (NFC and NFD) compare
/// unequal here. A caller that keys a store by path must apply its own normalization (separators,
/// trailing separators, and Unicode form) before comparing or storing paths; Core deliberately
/// normalizes nothing.
/// </para>
/// <para>
/// Supply the same value to every component that resolves path identities together, so the policy
/// cannot drift between them.
/// </para>
/// </remarks>
/// <param name="IgnoreCase"><see langword="true"/> to compare identity paths case-insensitively.</param>
public readonly record struct LocalPathComparisonPolicy(bool IgnoreCase)
{
	/// <summary>
	/// Gets the comparison for the current operating system's usual file-system semantics:
	/// case-insensitive on Windows and macOS, and ordinal on other platforms.
	/// </summary>
	public static LocalPathComparisonPolicy ForCurrentPlatform => new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

	/// <summary>
	/// Gets a case-insensitive comparison.
	/// </summary>
	public static LocalPathComparisonPolicy CaseInsensitive => new(true);

	/// <summary>
	/// Gets an ordinal, case-sensitive comparison.
	/// </summary>
	public static LocalPathComparisonPolicy CaseSensitive => new(false);

	/// <summary>
	/// Gets the <see cref="StringComparison"/> form of the policy.
	/// </summary>
	public StringComparison Comparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>
	/// Gets the <see cref="StringComparer"/> form of the policy.
	/// </summary>
	public StringComparer Comparer => IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
