using System.Text.Json;
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Builds the parameters of the initialize request for the configured workspace roots.</summary>
internal sealed class InitializeParamsBuilder
{
	private readonly WorkspaceRoots _roots;

	private readonly Func<IReadOnlyList<string>, object?> _initializationOptionsProvider;
	private readonly Func<IReadOnlyList<string>, object?> _clientCapabilitiesProvider;

	private readonly LanguageServerClientHooks _hooks;

	private readonly ILogger _logger;

	/// <param name="roots">The workspace roots that the request advertises and that the providers receive.</param>
	/// <param name="initializationOptionsProvider">Supplies the host's initialization options for the roots.</param>
	/// <param name="clientCapabilitiesProvider">Supplies the host's client capabilities for the roots.</param>
	/// <param name="hooks">The host hooks, whose presence decides the capabilities that the client handles itself.</param>
	/// <param name="logger">The logger for the payloads that cannot be sent as supplied.</param>
	internal InitializeParamsBuilder(
		WorkspaceRoots roots,
		Func<IReadOnlyList<string>, object?> initializationOptionsProvider,
		Func<IReadOnlyList<string>, object?> clientCapabilitiesProvider,
		LanguageServerClientHooks hooks,
		ILogger logger
	)
	{
		_roots = roots;
		_initializationOptionsProvider = initializationOptionsProvider;
		_clientCapabilitiesProvider = clientCapabilitiesProvider;
		_hooks = hooks;
		_logger = logger;
	}

	/// <summary>Builds the initialize request payload for the configured workspace roots.</summary>
	/// <remarks>
	/// The initialization options are serialized with the protocol serializer options, which apply the lower-camel naming policy, so a host DTO
	/// with PascalCase members does not ship mixed casing next to the normalized client capabilities.
	/// </remarks>
	internal InitializeParams Build()
	{
		return new InitializeParams(
			ProcessId: Environment.ProcessId,
			InitializationOptions: BuildInitializationOptionsPayload(),
			RootUri: _roots.Folders.Count > 0 ? _roots.Folders[0].Uri : null,
			WorkspaceFolders: _roots.Folders.Count > 0 ? _roots.Folders : null,
			Capabilities: ClientCapabilitiesBuilder.Build(_clientCapabilitiesProvider(_roots.Paths), _hooks, _logger)
		);
	}

	/// <summary>Builds the initialization-options payload, normalizing member casing to the protocol's lower-camel convention.</summary>
	/// <returns>The normalized initialization options, or <see langword="null"/> when the host supplied none.</returns>
	private JsonNode? BuildInitializationOptionsPayload()
	{
		object? initializationOptions = _initializationOptionsProvider(_roots.Paths);
		return initializationOptions is null ? null : JsonSerializer.SerializeToNode(initializationOptions, ProtocolSerialization.ForPayloads());
	}
}
