namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// Configures the Roslyn language server session a Roslyn provider starts.
/// </summary>
/// <remarks>
/// <para>
/// The defaults keep the server's own behavior: automatic project loading is enabled with the server's default
/// project cap, and completion items from unimported namespaces are offered.
/// </para>
/// <para>
/// Pass a <see cref="RoslynLanguageServerOptions"/> instance to a language provider constructor to override the
/// session for that provider. The maximum is applied on the launch command line; the completion setting is
/// mirrored into the settings document the provider serves for the server's <c>workspace/configuration</c>
/// requests.
/// </para>
/// <para>
/// Record equality compares the members by value, so two instances built with the same values are equal.
/// </para>
/// </remarks>
public sealed record RoslynLanguageServerOptions
{
	private int? _autoLoadProjectsMaximum;

	/// <summary>
	/// Gets the shared default options instance.
	/// </summary>
	/// <remarks>
	/// The instance is shared by every provider constructed without explicit options. It is immutable and must be
	/// treated as such.
	/// </remarks>
	public static RoslynLanguageServerOptions Default { get; } = new();

	/// <summary>
	/// Gets the maximum number of projects the server discovers and loads when it falls back to project discovery.
	/// Defaults to <see langword="null"/>, which uses the server's own default (500).
	/// </summary>
	/// <remarks>
	/// <c>0</c> disables automatic project loading for the session, even though the launch command line enables it;
	/// a positive value caps the number of discovered projects. The value only applies to this session.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public int? AutoLoadProjectsMaximum
	{
		get => _autoLoadProjectsMaximum;
		init
		{
			if (value is < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, "The auto-load-projects maximum must be zero (disabled) or a positive project count.");

			_autoLoadProjectsMaximum = value;
		}
	}

	/// <summary>
	/// Gets a value indicating whether completion offers types and extension methods that are not imported yet,
	/// adding the required import when such an item is committed. Defaults to <see langword="true"/>.
	/// </summary>
	public bool ShowCompletionItemsFromUnimportedNamespaces { get; init; } = true;
}
