namespace Nickelony.LanguageServer.Lua;

/// <summary>Configures the LuaLS settings payload the provider sends for a workspace.</summary>
/// <remarks>
/// <para>
/// Additional library folders and disabled diagnostics are forwarded to LuaLS with blank entries dropped and every remaining entry unchanged,
/// so workspace-relative paths are resolved by LuaLS against the workspace folder. A consumer can instead declare
/// them in the workspace's own <c>.luarc.json</c>, which LuaLS re-reads on change.
/// </para>
/// <para>
/// Record equality compares <see cref="AdditionalLibraryDirectories"/> and <see cref="DisabledDiagnostics"/> by reference, and each initializer
/// stores its own copy of the list, so two instances that each set a list are not equal even when both were built from the same source list.
/// </para>
/// </remarks>
public sealed record LuaLanguageServerOptions
{
	private string _runtimeVersion = "Lua 5.4";

	private IReadOnlyList<string> _additionalLibraryDirectories = [];
	private IReadOnlyList<string> _disabledDiagnostics = [];

	/// <summary>The shared default options instance.</summary>
	/// <remarks>
	/// The instance is shared by every profile created without explicit options. It is immutable - the list properties expose read-only snapshots -
	/// and must be treated as such.
	/// </remarks>
	public static LuaLanguageServerOptions Default { get; } = new();

	/// <summary>The Lua runtime version reported to LuaLS, which defaults to <c>"Lua 5.4"</c>.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	public string RuntimeVersion
	{
		get => _runtimeVersion;
		init
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			_runtimeVersion = value;
		}
	}

	/// <summary>The additional library folders forwarded to LuaLS, which defaults to an empty list.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public IReadOnlyList<string> AdditionalLibraryDirectories
	{
		get => _additionalLibraryDirectories;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_additionalLibraryDirectories = [.. value];
		}
	}

	/// <summary>The diagnostic names disabled in LuaLS, which defaults to an empty list.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public IReadOnlyList<string> DisabledDiagnostics
	{
		get => _disabledDiagnostics;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_disabledDiagnostics = [.. value];
		}
	}

	/// <summary>Indicates whether semantic highlighting is enabled, which defaults to <see langword="true"/>.</summary>
	public bool EnableSemanticHighlighting { get; init; } = true;

	/// <summary>Indicates whether semantic annotation highlighting is enabled, which defaults to <see langword="true"/>.</summary>
	public bool EnableSemanticAnnotationHighlighting { get; init; } = true;

	/// <summary>Indicates whether semantic variable highlighting is enabled, which defaults to <see langword="true"/>.</summary>
	public bool EnableSemanticVariableHighlighting { get; init; } = true;

	/// <summary>Whether semantic keyword highlighting is enabled, which defaults to <see langword="false"/> to mirror the LuaLS default.</summary>
	public bool EnableSemanticKeywordHighlighting { get; init; }
}
