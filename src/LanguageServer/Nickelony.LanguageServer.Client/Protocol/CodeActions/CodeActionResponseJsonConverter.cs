using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes code-action responses tolerantly so one malformed element cannot fail the whole response.</summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is not invoked
/// for JSON <see langword="null"/>. A payload that is not an array produces an empty response with a warning.
/// </para>
/// <para>
/// Entries without a usable title are skipped with a warning. An edit that fails to deserialize drops only the edit; the entry survives.
/// The opaque <c>data</c> member is preserved as a detached clone.
/// </para>
/// </remarks>
internal sealed class CodeActionResponseJsonConverter : TolerantPayloadJsonConverter<CodeActionResponse>
{
	public CodeActionResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public CodeActionResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override CodeActionResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning("Ignoring malformed code-action payload because its JSON kind {Kind} is not an array.", root.ValueKind);
			return new CodeActionResponse();
		}

		var actions = new List<CodeActionPayload>();

		foreach (JsonElement actionElement in root.EnumerateArray())
			AppendAction(actions, actionElement, options);

		return new CodeActionResponse(actions);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CodeActionResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.CodeActions.Count; i++)
			WriteAction(writer, value.CodeActions[i], options);

		writer.WriteEndArray();
	}

	/// <summary>
	/// Parses one code-action element and appends it when it carries a usable title; any other element is skipped, with a warning
	/// unless it is a JSON <see langword="null"/>.
	/// </summary>
	/// <param name="actions">The destination list that receives the parsed action.</param>
	/// <param name="actionElement">The payload element to parse.</param>
	/// <param name="options">The serializer options used for the nested edit payload.</param>
	private void AppendAction(List<CodeActionPayload> actions, JsonElement actionElement, JsonSerializerOptions options)
	{
		if (actionElement.ValueKind != JsonValueKind.Object)
		{
			if (actionElement.ValueKind != JsonValueKind.Null)
				Logger.LogWarning("Skipping malformed code action because its JSON kind {Kind} is not an object.", actionElement.ValueKind);

			return;
		}

		string? title = JsonElementReader.GetString(actionElement, "title");

		if (string.IsNullOrWhiteSpace(title))
		{
			Logger.LogWarning("Ignoring a code action that did not contain a usable title.");
			return;
		}

		WorkspaceEditResponse? edit = null;

		if (JsonElementReader.TryGetProperty(actionElement, "edit", out JsonElement editElement) && editElement.ValueKind == JsonValueKind.Object)
		{
			try
			{
				edit = editElement.Deserialize<WorkspaceEditResponse>(options);
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				Logger.LogWarning(exception, "Ignoring the edit payload of code action '{Title}' because it is malformed.", title);
			}
		}

		CommandPayload? command = ReadCommand(actionElement);
		JsonElement? data = CloneData(actionElement);

		if (edit is null && command is null && data is null)
		{
			// The protocol requires only the title, so the entry is kept; the host decides whether an idle
			// action is presented, and the debug line explains why it carries no payload.
			Logger.LogDebug("Code action '{Title}' carries no edit, command, or resolve data.", title);
		}

		actions.Add(
			new CodeActionPayload
			{
				Title = title,
				Kind = JsonElementReader.GetString(actionElement, "kind"),
				IsPreferred = JsonElementReader.GetBoolean(actionElement, "isPreferred"),
				Edit = edit,
				Command = command,
				Data = data,
				Diagnostics = ReadDiagnostics(actionElement, options),
				Disabled = ReadDisabled(actionElement),
			}
		);
	}

	private static void WriteAction(Utf8JsonWriter writer, CodeActionPayload action, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (action.Title is not null)
			writer.WriteString("title", action.Title);

		if (action.Kind is not null)
			writer.WriteString("kind", action.Kind);

		if (action.IsPreferred is { } isPreferred)
			writer.WriteBoolean("isPreferred", isPreferred);

		if (action.Edit is { } edit)
		{
			writer.WritePropertyName("edit");
			JsonSerializer.Serialize(writer, edit, options);
		}

		// An attached diagnostics list is written even when it is empty, so a round trip keeps an empty
		// list distinguishable from an omitted member (the read side preserves the same distinction).
		if (action.Diagnostics is { } diagnostics)
		{
			writer.WritePropertyName("diagnostics");
			writer.WriteStartArray();

			for (int i = 0; i < diagnostics.Count; i++)
				JsonSerializer.Serialize(writer, diagnostics[i], options);

			writer.WriteEndArray();
		}

		if (action.Disabled is { } disabled)
		{
			writer.WritePropertyName("disabled");
			writer.WriteStartObject();

			// The protocol requires the reason member; a missing reason is written as an empty string so the
			// disabled state stays representable without emitting a schema-invalid empty object.
			writer.WriteString("reason", disabled.Reason ?? string.Empty);
			writer.WriteEndObject();
		}

		if (action.Command is { } command)
		{
			writer.WritePropertyName("command");
			writer.WriteStartObject();

			if (command.Title is not null)
				writer.WriteString("title", command.Title);

			if (command.Command is not null)
				writer.WriteString("command", command.Command);

			if (command.Arguments is { Count: > 0 } arguments)
			{
				writer.WritePropertyName("arguments");
				writer.WriteStartArray();

				for (int i = 0; i < arguments.Count; i++)
					arguments[i].WriteTo(writer);

				writer.WriteEndArray();
			}

			writer.WriteEndObject();
		}

		if (action.Data is { } data && data.ValueKind != JsonValueKind.Undefined)
		{
			writer.WritePropertyName("data");
			data.WriteTo(writer);
		}

		writer.WriteEndObject();
	}

	/// <summary>Parses the optional diagnostics of a code-action element.</summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <param name="options">The serializer options used for the nested diagnostic payloads.</param>
	/// <returns>
	/// The parsed diagnostics, or <see langword="null"/> when the property is absent or a JSON <see langword="null"/>; an attached empty array reads
	/// as an empty list.
	/// </returns>
	private ReadOnlyCollection<DiagnosticPayload>? ReadDiagnostics(JsonElement actionElement, JsonSerializerOptions options)
	{
		if (
			!JsonElementReader.TryGetProperty(actionElement, "diagnostics", out JsonElement diagnosticsElement)
			|| diagnosticsElement.ValueKind == JsonValueKind.Null
		)
		{
			return null;
		}

		if (diagnosticsElement.ValueKind != JsonValueKind.Array)
		{
			Logger.LogWarning(
				"Ignoring malformed code-action diagnostics because their JSON kind {Kind} is not an array.",
				diagnosticsElement.ValueKind
			);

			return null;
		}

		var diagnostics = new List<DiagnosticPayload>(diagnosticsElement.GetArrayLength());

		// Malformed diagnostics are skipped individually so one bad entry cannot discard the usable ones.
		foreach (JsonElement diagnosticElement in diagnosticsElement.EnumerateArray())
		{
			// A JSON null element is dropped like a malformed entry instead of being added as a null list entry.
			if (
				TryReadElement(
					element: diagnosticElement,
					options,
					elementDescription: "code-action diagnostic",
					value: out DiagnosticPayload? diagnostic
				)
			)
			{
				diagnostics.Add(diagnostic);
			}
		}

		return Array.AsReadOnly([.. diagnostics]);
	}

	/// <summary>Parses the optional disabled state of a code-action element.</summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The parsed disabled state, or <see langword="null"/> when the action is enabled.</returns>
	private CodeActionDisabledPayload? ReadDisabled(JsonElement actionElement)
	{
		if (
			!JsonElementReader.TryGetProperty(actionElement, "disabled", out JsonElement disabledElement)
			|| disabledElement.ValueKind == JsonValueKind.Null
		)
		{
			return null;
		}

		if (disabledElement.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning(
				"Ignoring code-action 'disabled' state with JSON kind {Kind}; the action is treated as enabled.",
				disabledElement.ValueKind
			);

			return null;
		}

		return new CodeActionDisabledPayload(JsonElementReader.GetString(disabledElement, "reason"));
	}

	/// <summary>Parses the optional command of a code-action element.</summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The parsed command, or <see langword="null"/> when the action carries no usable command.</returns>
	/// <remarks>
	/// The <c>command</c> member is either a command object, which a <c>CodeAction</c> carries, or a string. A string means the element
	/// is itself a bare <c>Command</c> literal, so the string is the identifier and the arguments sit next to the member on the element.
	/// </remarks>
	private CommandPayload? ReadCommand(JsonElement actionElement)
	{
		if (!JsonElementReader.TryGetProperty(actionElement, "command", out JsonElement commandElement))
			return null;

		if (commandElement.ValueKind == JsonValueKind.String)
		{
			// The LSP result is (Command | CodeAction)[], so an element whose 'command' member is a string is a
			// Command literal: the string is the command identifier and the arguments belong to the element.
			string? literalCommand = commandElement.GetString();

			if (string.IsNullOrWhiteSpace(literalCommand))
				return null;

			return new CommandPayload(JsonElementReader.GetString(actionElement, "title"), literalCommand, CloneArguments(actionElement));
		}

		if (commandElement.ValueKind != JsonValueKind.Object)
			return null;

		string? command = JsonElementReader.GetString(commandElement, "command");

		if (string.IsNullOrWhiteSpace(command))
		{
			Logger.LogWarning(
				"Ignoring the command of code action '{Title}' because it did not carry a usable command identifier.",
				JsonElementReader.GetString(actionElement, "title") ?? "<untitled>"
			);

			return null;
		}

		return new CommandPayload(JsonElementReader.GetString(commandElement, "title"), command, CloneArguments(commandElement));
	}

	/// <summary>Clones the optional arguments of a command element so the payload stays valid after its source document is disposed.</summary>
	/// <param name="commandElement">The command element to read from.</param>
	/// <returns>The cloned arguments in wire order, or <see langword="null"/> when the command carries none.</returns>
	private static List<JsonElement>? CloneArguments(JsonElement commandElement)
	{
		if (
			!JsonElementReader.TryGetProperty(commandElement, "arguments", out JsonElement argumentsElement)
			|| argumentsElement.ValueKind != JsonValueKind.Array
		)
		{
			return null;
		}

		var arguments = new List<JsonElement>(argumentsElement.GetArrayLength());

		foreach (JsonElement argumentElement in argumentsElement.EnumerateArray())
			arguments.Add(argumentElement.Clone());

		return arguments.Count > 0 ? arguments : null;
	}

	/// <summary>Clones the opaque <c>data</c> member of a code-action element.</summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The detached clone, or <see langword="null"/> when the action carries no data.</returns>
	private static JsonElement? CloneData(JsonElement actionElement)
	{
		return JsonElementReader.TryGetProperty(actionElement, "data", out JsonElement dataElement) && dataElement.ValueKind != JsonValueKind.Null
			? dataElement.Clone()
			: null;
	}
}
