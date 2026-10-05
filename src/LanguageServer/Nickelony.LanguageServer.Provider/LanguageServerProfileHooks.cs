using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Provider;

/// <summary>The rules of a language that only code can express, bundled for a <see cref="LanguageServerProfile"/>.</summary>
/// <remarks>
/// No delegate runs while the provider is constructed: each runs lazily on the thread that triggers it, including connection,
/// watcher and scheduler threads. <see cref="Default"/> holds the delegates of a language that has no configuration file, sends no settings and sends
/// no session-start notifications.
/// </remarks>
public sealed class LanguageServerProfileHooks
{
	private static readonly Func<string, bool> s_noConfigurationPath = static _ => false;
	private static readonly Func<JsonNode> s_emptySettings = static () => new JsonObject();
	private static readonly Func<object?> s_emptyInitializationOptions = static () => new { };

	private readonly Func<string, bool> _isConfigurationPath = s_noConfigurationPath;
	private readonly Func<JsonNode> _buildSettings = s_emptySettings;
	private readonly Func<object?> _buildInitializationOptions = s_emptyInitializationOptions;

	/// <summary>The hooks of a language with no configuration file, an empty settings document and no initialization options.</summary>
	public static LanguageServerProfileHooks Default { get; } =
		new()
		{
			IsConfigurationPath = s_noConfigurationPath,
			BuildSettings = s_emptySettings,
			BuildInitializationOptions = s_emptyInitializationOptions,
		};

	/// <summary>
	/// Reports whether a normalized changed workspace path is a configuration file of the language, so the provider asks the client to push
	/// the settings before it forwards the change.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// A throwing delegate is contained and counts as <see langword="false"/>.
	/// </remarks>
	public required Func<string, bool> IsConfigurationPath
	{
		get => _isConfigurationPath;
		init => _isConfigurationPath = value ?? throw new ArgumentNullException(nameof(IsConfigurationPath));
	}

	/// <summary>Builds the settings document that the client serves and pushes.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// It runs only for a client that the provider creates, and a throw is contained by the client.
	/// </remarks>
	public required Func<JsonNode> BuildSettings
	{
		get => _buildSettings;
		init => _buildSettings = value ?? throw new ArgumentNullException(nameof(BuildSettings));
	}

	/// <summary>Builds the language-specific initialization options of the <c>initialize</c> request, or <see langword="null"/> for none.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// It runs only for a client that the provider creates.
	/// </remarks>
	public required Func<object?> BuildInitializationOptions
	{
		get => _buildInitializationOptions;
		init => _buildInitializationOptions = value ?? throw new ArgumentNullException(nameof(BuildInitializationOptions));
	}

	/// <summary>
	/// Builds the notifications sent right after <c>initialized</c> on every session start, from the normalized workspace roots;
	/// <see langword="null"/> sends none.
	/// </summary>
	/// <remarks>
	/// It runs only for a client that the provider creates, on each start of its session, a restart included. The client contains a throw,
	/// as <see cref="LanguageServerClientOptions.SessionStartNotificationsProvider"/> describes.
	/// </remarks>
	public Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>>? BuildSessionStartNotifications { get; init; }
}
