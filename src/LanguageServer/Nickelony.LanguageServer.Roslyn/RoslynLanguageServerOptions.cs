namespace Nickelony.LanguageServer.Roslyn;

/// <summary>Configures the Roslyn language server session that a provider over a Roslyn profile starts.</summary>
/// <remarks>
/// <para>
/// The defaults keep the server's own behavior: automatic project loading is enabled with the server's default project cap, and completion items
/// from unimported namespaces are offered.
/// </para>
/// <para>
/// Pass a <see cref="RoslynLanguageServerOptions"/> instance to <see cref="RoslynLanguageServerProfileFactory"/> to override the session
/// of that profile. The maximum is applied on the launch command line, carried in the initialization options and caps the projects the profile
/// asks the server to load on each session start; the completion setting is mirrored into the settings document the provider serves for the server's
/// <c>workspace/configuration</c> requests.
/// </para>
/// </remarks>
public sealed record RoslynLanguageServerOptions
{
	private int? _autoLoadProjectsMaximum;

	/// <summary>The shared default options instance.</summary>
	/// <remarks>
	/// The instance is shared by every profile created without explicit options.
	/// </remarks>
	public static RoslynLanguageServerOptions Default { get; } = new();

	/// <summary>
	/// The maximum number of projects loaded automatically: the cap of the server's own discovery and of the project files the profile asks
	/// it to open; <see langword="null"/> keeps the server's default cap and opens every project file found.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	/// <remarks>
	/// <c>0</c> disables automatic project loading for the session, so the profile asks the server to open no solution or project. A positive value
	/// caps the project files across the workspace roots; a solution is opened whole. The value only applies to this session.
	/// </remarks>
	public int? AutoLoadProjectsMaximum
	{
		get => _autoLoadProjectsMaximum;
		init
		{
			if (value is < 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					value,
					"The auto-load-projects maximum must be zero (disabled) or a positive project count."
				);
			}

			_autoLoadProjectsMaximum = value;
		}
	}

	/// <summary>Whether completion offers types and extension methods that are not imported yet, which defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// Committing such an item adds the required import.
	/// </remarks>
	public bool ShowCompletionItemsFromUnimportedNamespaces { get; init; } = true;
}
