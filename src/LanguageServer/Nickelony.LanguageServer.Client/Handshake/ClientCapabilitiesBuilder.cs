using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Rewrites the host's client capabilities into the payload of the initialize request.</summary>
/// <remarks>
/// <para>
/// The payload is serialized with the lower-camel naming policy of the protocol, so a host DTO with PascalCase members does not ship mixed casing.
/// </para>
/// <para>
/// This client does not service dynamic capability registration requests (the RPC target logs
/// and ignores <c>client/registerCapability</c> and <c>client/unregisterCapability</c>), so the payload forces the flag off wherever it is present
/// under <c>workspace</c> and <c>textDocument</c> rather than tracking a fixed capability list.
/// </para>
/// </remarks>
internal static class ClientCapabilitiesBuilder
{
	private const string DynamicRegistrationPropertyName = "dynamicRegistration";

	/// <summary>
	/// Builds the client-capabilities payload, normalizing member casing to the protocol's lower-camel convention, forcing dynamic registration
	/// and semantic-token multiline and overlapping support off, setting work-done progress and workspace applyEdit to whether their hooks are set,
	/// and pinning the UTF-16 position encoding.
	/// </summary>
	/// <param name="clientCapabilities">The host's capabilities, or <see langword="null"/> for none.</param>
	/// <param name="hooks">The host hooks, whose presence decides the work-done progress and applyEdit capabilities.</param>
	/// <param name="logger">The logger that receives the warning for a payload that is not a JSON object.</param>
	/// <returns>The normalized capabilities, or an empty object when the payload does not serialize to a JSON object.</returns>
	internal static JsonObject Build(object? clientCapabilities, LanguageServerClientHooks hooks, ILogger logger)
	{
		JsonObject capabilitiesObject;

		if (clientCapabilities is null)
		{
			capabilitiesObject = [];
		}
		else if (JsonSerializer.SerializeToNode(clientCapabilities, ProtocolSerialization.ForPayloads()) is JsonObject serializedObject)
		{
			capabilitiesObject = serializedObject;
		}
		else
		{
			logger.LogWarning(
				"The client-capabilities payload does not serialize to a JSON object, so dynamic registration, work-done progress, and the UTF-16 position encoding cannot be enforced for it; sending empty client capabilities to keep the initialize payload protocol-valid."
			);

			return [];
		}

		EnforceDynamicRegistrationFalse(capabilitiesObject, "workspace");
		EnforceDynamicRegistrationFalse(capabilitiesObject, "textDocument");

		SetHookCapability(
			rootObject: capabilitiesObject,
			parentPropertyName: "window",
			flagPropertyName: "workDoneProgress",
			isHandled: hooks.Progress is not null
		);

		SetHookCapability(
			rootObject: capabilitiesObject,
			parentPropertyName: "workspace",
			flagPropertyName: "applyEdit",
			isHandled: hooks.ApplyWorkspaceEdit is not null
		);

		EnforceUtf16PositionEncoding(capabilitiesObject);
		EnforceSemanticTokenSupportFalse(capabilitiesObject);

		return capabilitiesObject;
	}

	/// <summary>
	/// Forces <c>textDocument.semanticTokens.multilineTokenSupport</c> and <c>overlappingTokenSupport</c> to <see langword="false"/> when the host
	/// payload advertises them.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	/// <remarks>
	/// The semantic-token decoder implements the unsupported-multi-line-token rule (a token is clamped to its line end), and the decoded model
	/// cannot represent overlapping tokens.
	/// </remarks>
	private static void EnforceSemanticTokenSupportFalse(JsonObject rootObject)
	{
		if (
			!TryGetJsonObjectProperty(rootObject, "textDocument", out JsonObject? textDocumentObject)
			|| !TryGetJsonObjectProperty(textDocumentObject, "semanticTokens", out JsonObject? semanticTokensObject)
		)
		{
			return;
		}

		if (semanticTokensObject.ContainsKey("multilineTokenSupport"))
			semanticTokensObject["multilineTokenSupport"] = false;

		if (semanticTokensObject.ContainsKey("overlappingTokenSupport"))
			semanticTokensObject["overlappingTokenSupport"] = false;
	}

	/// <summary>
	/// Sets one capability flag to whether the client handles the matching server message, so a server sends the message only to a client
	/// that answers it.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	/// <param name="parentPropertyName">The capability group that holds the flag.</param>
	/// <param name="flagPropertyName">The flag to set.</param>
	/// <param name="isHandled">Whether the host set the hook that handles the message.</param>
	/// <remarks>
	/// A handled message sets the flag to <see langword="true"/>, adding the group when the host payload has none. An unhandled one forces the flag
	/// to <see langword="false"/> when the host payload advertises it, and otherwise adds nothing.
	/// </remarks>
	private static void SetHookCapability(JsonObject rootObject, string parentPropertyName, string flagPropertyName, bool isHandled)
	{
		if (isHandled)
		{
			if (!TryGetJsonObjectProperty(rootObject, parentPropertyName, out JsonObject? parentObject))
			{
				parentObject = [];
				rootObject[parentPropertyName] = parentObject;
			}

			parentObject[flagPropertyName] = true;
			return;
		}

		if (TryGetJsonObjectProperty(rootObject, parentPropertyName, out JsonObject? existingParent) && existingParent.ContainsKey(flagPropertyName))
			existingParent[flagPropertyName] = false;
	}

	/// <summary>
	/// Pins <c>general.positionEncodings</c> to UTF-16, the only encoding the coordinate math implements, so a host payload cannot negotiate
	/// an encoding that would corrupt every position and range.
	/// </summary>
	/// <param name="rootObject">The root capabilities object.</param>
	private static void EnforceUtf16PositionEncoding(JsonObject rootObject)
	{
		JsonObject generalObject =
			rootObject.TryGetPropertyValue("general", out JsonNode? generalNode) && generalNode is JsonObject existingGeneralObject
				? existingGeneralObject
				: [];

		generalObject["positionEncodings"] = new JsonArray("utf-16");
		rootObject["general"] = generalObject;
	}

	/// <summary>Forces <c>dynamicRegistration = false</c> on every capability object of one capability parent.</summary>
	/// <param name="rootObject">The root capabilities object.</param>
	/// <param name="parentPropertyName">The parent property whose capability objects should be rewritten.</param>
	private static void EnforceDynamicRegistrationFalse(JsonObject rootObject, string parentPropertyName)
	{
		if (!TryGetJsonObjectProperty(rootObject, parentPropertyName, out JsonObject? parentObject))
			return;

		RewriteDynamicRegistrationFalse(parentObject);
	}

	/// <summary>Forces <c>dynamicRegistration = false</c> on one capability object and every nested object that declares the member.</summary>
	/// <param name="capabilityObject">The capability object to rewrite.</param>
	private static void RewriteDynamicRegistrationFalse(JsonObject capabilityObject)
	{
		foreach (KeyValuePair<string, JsonNode?> capabilityEntry in capabilityObject)
		{
			if (capabilityEntry.Value is not JsonObject nestedObject)
				continue;

			// Only existing members are rewritten: capability objects that never declared dynamicRegistration
			// must not gain a member, and nested capability objects such as workspace.fileOperations
			// carry their own member that a shallow rewrite would miss.
			if (nestedObject.ContainsKey(DynamicRegistrationPropertyName))
				nestedObject[DynamicRegistrationPropertyName] = false;

			RewriteDynamicRegistrationFalse(nestedObject);
		}
	}

	/// <summary>Tries to read one JSON object property while rejecting non-object values.</summary>
	/// <param name="rootObject">The containing JSON object.</param>
	/// <param name="propertyName">The property name to read.</param>
	/// <param name="propertyObject">Receives the nested object value when present.</param>
	/// <returns><see langword="true"/> when the property exists and is a JSON object.</returns>
	private static bool TryGetJsonObjectProperty(JsonObject rootObject, string propertyName, [NotNullWhen(true)] out JsonObject? propertyObject)
	{
		propertyObject = null;

		if (!rootObject.TryGetPropertyValue(propertyName, out JsonNode? propertyNode))
			return false;

		propertyObject = propertyNode as JsonObject;
		return propertyObject is not null;
	}
}
