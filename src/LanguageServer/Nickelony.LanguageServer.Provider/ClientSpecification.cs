namespace Nickelony.LanguageServer.Provider;

/// <summary>The options a provider hands the client it creates from a profile.</summary>
/// <param name="Options">The payload factories of the client, each of which reads its profile member when the client runs it.</param>
/// <param name="Launch">The launch settings of the stdio transport that connects the client.</param>
/// <remarks>
/// The provider keeps the specification of the client it created, so a test reads what the constructor handed the client.
/// </remarks>
internal sealed record ClientSpecification(LanguageServerClientOptions Options, StdioLanguageServerTransportOptions Launch)
{
	/// <summary>Maps the members of <paramref name="profile"/> that the client consumes.</summary>
	/// <param name="profile">The profile whose hooks, client capabilities and launch settings the client takes.</param>
	/// <returns>The specification of the client.</returns>
	internal static ClientSpecification From(LanguageServerProfile profile)
	{
		var options = new LanguageServerClientOptions
		{
			SettingsProvider = profile.Hooks.BuildSettings,
			ClientCapabilitiesProvider = _ => LanguageServerClientCapabilitiesBuilder.Build(profile.ClientCapabilities),
			InitializationOptionsProvider = _ => profile.Hooks.BuildInitializationOptions(),
		};

		if (profile.Hooks.BuildSessionStartNotifications is { } buildSessionStartNotifications)
			options = options with { SessionStartNotificationsProvider = buildSessionStartNotifications };

		return new ClientSpecification(options, profile.Launch);
	}
}
