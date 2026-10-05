using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Converts the <c>workspaceSymbolProvider</c> capability, which may be advertised as either a boolean or an options object.</summary>
/// <remarks>
/// Reading accepts a boolean or an options object; writing always emits the boolean form. A non-boolean <c>resolveProvider</c> counts
/// as not advertised.
/// </remarks>
internal sealed class WorkspaceSymbolProviderCapabilityJsonConverter : JsonConverter<WorkspaceSymbolProviderCapability>
{
	/// <inheritdoc/>
	public override WorkspaceSymbolProviderCapability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		return reader.TokenType switch
		{
			JsonTokenType.True => new WorkspaceSymbolProviderCapability(true, null),
			JsonTokenType.False => new WorkspaceSymbolProviderCapability(false, null),
			JsonTokenType.StartObject => ReadObject(ref reader),
			JsonTokenType.Null => default,
			_ => ReadUnsupported(ref reader),
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, WorkspaceSymbolProviderCapability value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value.IsSupported);

	private static WorkspaceSymbolProviderCapability ReadObject(ref Utf8JsonReader reader)
	{
		using var document = JsonDocument.ParseValue(ref reader);

		bool? resolveProvider = JsonElementReader.TryGetProperty(document.RootElement, "resolveProvider", out JsonElement resolveProviderElement)
			? resolveProviderElement.ValueKind switch
			{
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				_ => null,
			}
			: null;

		return new WorkspaceSymbolProviderCapability(true, resolveProvider);
	}

	private static WorkspaceSymbolProviderCapability ReadUnsupported(ref Utf8JsonReader reader)
	{
		using var ignored = JsonDocument.ParseValue(ref reader);
		return new WorkspaceSymbolProviderCapability(false, null);
	}
}
