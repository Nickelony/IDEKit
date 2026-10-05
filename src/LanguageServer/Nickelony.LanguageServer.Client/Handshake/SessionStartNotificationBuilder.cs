using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Client;

/// <summary>Builds the payloads of the notifications that the host's factory supplies for one session start.</summary>
internal sealed class SessionStartNotificationBuilder
{
	private readonly WorkspaceRoots _roots;
	private readonly Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>> _sessionStartNotificationsProvider;
	private readonly ILogger _logger;

	/// <param name="roots">The workspace roots that the factory receives.</param>
	/// <param name="sessionStartNotificationsProvider">Supplies the host's notifications for the roots.</param>
	/// <param name="logger">The logger for a factory that fails.</param>
	internal SessionStartNotificationBuilder(
		WorkspaceRoots roots,
		Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>> sessionStartNotificationsProvider,
		ILogger logger
	)
	{
		_roots = roots;
		_sessionStartNotificationsProvider = sessionStartNotificationsProvider;
		_logger = logger;
	}

	/// <summary>Builds the notifications of one session start, containing a fault of the host's factory.</summary>
	/// <returns>
	/// The method and serialized parameters of each notification in the host's order; empty when the factory throws or a payload does not serialize.
	/// </returns>
	/// <remarks>
	/// The parameters are serialized with the protocol serializer options, which apply the lower-camel naming policy,
	/// as the initialization options are. Serializing here, before anything is sent, keeps a payload that cannot be serialized from failing
	/// the start.
	/// </remarks>
	internal IReadOnlyList<(string Method, JsonNode? Parameters)> Build()
	{
		try
		{
			IReadOnlyList<SessionStartNotification>? notifications = _sessionStartNotificationsProvider(_roots.Paths);

			if (notifications is null || notifications.Count == 0)
				return [];

			var built = new List<(string Method, JsonNode? Parameters)>(notifications.Count);

			for (int i = 0; i < notifications.Count; i++)
			{
				if (notifications[i] is not { } notification)
					continue;

				JsonNode? parameters = notification.Parameters is null
					? null
					: JsonSerializer.SerializeToNode(notification.Parameters, ProtocolSerialization.ForPayloads());

				built.Add((notification.Method, parameters));
			}

			return built;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(
				exception,
				"The session-start notifications factory failed for workspace '{Workspace}'; the session starts without its notifications.",
				_roots.DisplayText
			);

			return [];
		}
	}
}
