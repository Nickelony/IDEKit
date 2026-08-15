namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Configures the LuaLS settings payload the provider sends for a workspace.
/// </summary>
/// <remarks>
/// <para>
/// The default values mirror the settings area of LuaLS, so an unconfigured provider behaves like a
/// default LuaLS installation. Two defaults deviate deliberately: third-party checks are disabled
/// because a library cannot answer LuaLS's interactive prompts, and completion call snippets are
/// enabled with <c>"Replace"</c> because the provider consumes snippet insert texts end to end; no
/// diagnostics are suppressed by default.
/// </para>
/// <para>
/// Pass a <see cref="LuaLanguageServerOptions"/> instance to the provider constructor overload that
/// accepts one to override the settings for that provider. Additional library folders and disabled
/// diagnostics are forwarded to LuaLS with blank entries dropped and every remaining entry unchanged,
/// so workspace-relative paths are resolved by LuaLS against the workspace folder. A consumer that
/// adds library folders to the workspace itself can declare them in the workspace's LuaLS
/// configuration file (<c>.luarc.json</c>, <c>workspace.library</c>); LuaLS re-reads that file when
/// it changes, so a folder that appears later is picked up automatically.
/// </para>
/// <para>
/// Record equality compares <see cref="AdditionalLibraryDirectories"/> and <see cref="DisabledDiagnostics"/> by
/// reference, so two content-equal instances built from equal-but-distinct lists are not equal. Compare the
/// settings explicitly when a cache needs to react to a change.
/// </para>
/// </remarks>
public sealed record LuaLanguageServerOptions
{
	private string _runtimeVersion = "Lua 5.4";
	private IReadOnlyList<string> _additionalLibraryDirectories = [];
	private IReadOnlyList<string> _disabledDiagnostics = [];

	/// <summary>
	/// Gets the shared default options instance.
	/// </summary>
	/// <remarks>
	/// The instance is shared by every provider constructed without explicit options. It is immutable -
	/// the list properties expose read-only snapshots - and must be treated as such.
	/// </remarks>
	public static LuaLanguageServerOptions Default { get; } = new();

	/// <summary>
	/// Gets the Lua runtime version reported to LuaLS. Defaults to <c>"Lua 5.4"</c>.
	/// </summary>
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

	/// <summary>
	/// Gets the additional library folders forwarded to LuaLS. Defaults to an empty list.
	/// </summary>
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

	/// <summary>
	/// Gets the diagnostic names disabled in LuaLS. Defaults to an empty list.
	/// </summary>
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

	/// <summary>
	/// Gets a value indicating whether semantic highlighting is enabled. Defaults to <see langword="true"/>.
	/// </summary>
	public bool EnableSemanticHighlighting { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether semantic annotation highlighting is enabled. Defaults to
	/// <see langword="true"/>.
	/// </summary>
	public bool EnableSemanticAnnotationHighlighting { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether semantic variable highlighting is enabled. Defaults to
	/// <see langword="true"/>.
	/// </summary>
	public bool EnableSemanticVariableHighlighting { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether semantic keyword highlighting is enabled. Defaults to
	/// <see langword="false"/>, mirroring the LuaLS default; the other semantic switches default to
	/// <see langword="true"/>.
	/// </summary>
	public bool EnableSemanticKeywordHighlighting { get; init; }
}
