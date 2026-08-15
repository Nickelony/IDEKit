using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Deserializes code-action responses tolerantly so one malformed element cannot fail the whole response.
/// </summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the
/// converter is not invoked for JSON null. A payload that is not an array produces an empty response
/// with a warning.
/// </para>
/// <para>
/// Entries without a usable title are skipped with a warning; the protocol requires only the title, so every
/// titled entry is kept. A command-only entry stays representable because the host owns the
/// <c>workspace/executeCommand</c> policy, and the command of an edit-bearing action is preserved because a
/// host may execute it after applying the edit. An entry without an edit, command, or resolve data stays
/// representable as idle surface the host can withhold. An edit payload that fails to deserialize drops the
/// edit with a warning; the entry survives. The opaque <c>data</c> member is preserved as a detached clone
/// for hosts that implement <c>codeAction/resolve</c>, so the lazy resolve-only shape is representable. A
/// disabled action keeps its entry together with the server's disable reason so hosts can withhold or gray
/// it out.
/// </para>
/// </remarks>
public sealed class CodeActionsResponseJsonConverter : JsonConverter<CodeActionsResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CodeActionsResponseJsonConverter"/> class.
	/// </summary>
	public CodeActionsResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="CodeActionsResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public CodeActionsResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override CodeActionsResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring malformed code-action payload because its JSON kind {Kind} is not an array.", root.ValueKind);
			return new CodeActionsResponse();
		}

		var actions = new List<CodeActionPayload>();

		foreach (JsonElement actionElement in root.EnumerateArray())
			TryAppendAction(actions, actionElement, options);

		return new CodeActionsResponse(actions);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CodeActionsResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		for (int i = 0; i < value.CodeActions.Count; i++)
			WriteAction(writer, value.CodeActions[i], options);

		writer.WriteEndArray();
	}

	/// <summary>
	/// Parses one code-action element and appends it when it carries a usable title; every other element is
	/// skipped with a warning.
	/// </summary>
	/// <param name="actions">The destination list that receives the parsed action.</param>
	/// <param name="actionElement">The payload element to parse.</param>
	/// <param name="options">The serializer options used for the nested edit payload.</param>
	private void TryAppendAction(List<CodeActionPayload> actions, JsonElement actionElement, JsonSerializerOptions options)
	{
		if (actionElement.ValueKind != JsonValueKind.Object)
		{
			if (actionElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Skipping malformed code action because its JSON kind {Kind} is not an object.", actionElement.ValueKind);

			return;
		}

		string? title = JsonElementReadHelpers.TryGetString(actionElement, "title");

		if (string.IsNullOrWhiteSpace(title))
		{
			_logger.LogWarning("Ignoring a code action that did not contain a usable title.");
			return;
		}

		WorkspaceEditResponse? edit = null;

		if (JsonElementReadHelpers.TryGetProperty(actionElement, "edit", out JsonElement editElement)
			&& editElement.ValueKind == JsonValueKind.Object)
		{
			try
			{
				edit = editElement.Deserialize<WorkspaceEditResponse>(options);
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception, "Ignoring the edit payload of code action '{Title}' because it is malformed.", title);
			}
		}

		CodeActionCommandPayload? command = TryReadCommand(actionElement);
		JsonElement? data = TryCloneData(actionElement);

		if (edit is null && command is null && data is null)
		{
			// The protocol requires only the title, so the entry is kept; the host decides whether an idle
			// action is presented, and the debug line explains why it carries no payload.
			_logger.LogDebug("Code action '{Title}' carries no edit, command, or resolve data.", title);
		}

		actions.Add(new CodeActionPayload
		{
			Title = title,
			Kind = JsonElementReadHelpers.TryGetString(actionElement, "kind"),
			IsPreferred = JsonElementReadHelpers.TryGetBoolean(actionElement, "isPreferred"),
			Edit = edit,
			Command = command,
			Data = data,
			Diagnostics = TryReadDiagnostics(actionElement, options),
			Disabled = TryReadDisabled(actionElement)
		});
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

		if (action.Data is { } data)
		{
			writer.WritePropertyName("data");
			data.WriteTo(writer);
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Parses the optional diagnostics of a code-action element.
	/// </summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <param name="options">The serializer options used for the nested diagnostic payloads.</param>
	/// <returns>
	/// The parsed diagnostics, or <see langword="null"/> when the property is absent or a JSON null; an attached
	/// empty array reads as an empty list.
	/// </returns>
	private ReadOnlyCollection<DiagnosticPayload>? TryReadDiagnostics(JsonElement actionElement, JsonSerializerOptions options)
	{
		if (!JsonElementReadHelpers.TryGetProperty(actionElement, "diagnostics", out JsonElement diagnosticsElement)
			|| diagnosticsElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (diagnosticsElement.ValueKind != JsonValueKind.Array)
		{
			_logger.LogWarning("Ignoring malformed code-action diagnostics because their JSON kind {Kind} is not an array.", diagnosticsElement.ValueKind);
			return null;
		}

		var diagnostics = new List<DiagnosticPayload>();

		// Malformed diagnostics are skipped individually so one bad entry cannot discard the usable ones.
		foreach (JsonElement diagnosticElement in diagnosticsElement.EnumerateArray())
		{
			try
			{
				// A JSON null element binds to a null reference for the record class; it carries no diagnostic
				// data, so it is dropped like a malformed entry instead of being added as a null list entry.
				DiagnosticPayload? diagnostic = diagnosticElement.Deserialize<DiagnosticPayload>(options);

				if (diagnostic is null)
				{
					_logger.LogWarning("Skipping a code-action diagnostic entry because its JSON value is null.");

					continue;
				}

				diagnostics.Add(diagnostic);
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception, "Skipping a malformed code-action diagnostic entry.");
			}
		}

		return Array.AsReadOnly([.. diagnostics]);
	}

	/// <summary>
	/// Parses the optional disabled state of a code-action element.
	/// </summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The parsed disabled state, or <see langword="null"/> when the action is enabled.</returns>
	private CodeActionDisabledPayload? TryReadDisabled(JsonElement actionElement)
	{
		if (!JsonElementReadHelpers.TryGetProperty(actionElement, "disabled", out JsonElement disabledElement)
			|| disabledElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (disabledElement.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning(
				"Ignoring code-action 'disabled' state with JSON kind {Kind}; the action is treated as enabled.",
				disabledElement.ValueKind);

			return null;
		}

		return new CodeActionDisabledPayload(JsonElementReadHelpers.TryGetString(disabledElement, "reason"));
	}

	/// <summary>
	/// Parses the optional command of a code-action element.
	/// </summary>
	/// <remarks>
	/// The <c>command</c> member is either a command object (a <c>CodeAction</c> that carries a command) or a
	/// string (the element itself is a bare <c>Command</c> literal, whose identifier and arguments sit next to the
	/// member on the element).
	/// </remarks>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The parsed command, or <see langword="null"/> when the action carries no usable command.</returns>
	private CodeActionCommandPayload? TryReadCommand(JsonElement actionElement)
	{
		if (!JsonElementReadHelpers.TryGetProperty(actionElement, "command", out JsonElement commandElement))
			return null;

		if (commandElement.ValueKind == JsonValueKind.String)
		{
			// The LSP result is (Command | CodeAction)[], so an element whose 'command' member is a string is a
			// Command literal: the string is the command identifier and the arguments belong to the element.
			string? literalCommand = commandElement.GetString();

			if (string.IsNullOrWhiteSpace(literalCommand))
				return null;

			return new CodeActionCommandPayload(
				JsonElementReadHelpers.TryGetString(actionElement, "title"),
				literalCommand,
				TryCloneArguments(actionElement));
		}

		if (commandElement.ValueKind != JsonValueKind.Object)
			return null;

		string? command = JsonElementReadHelpers.TryGetString(commandElement, "command");

		if (string.IsNullOrWhiteSpace(command))
		{
			_logger.LogWarning(
				"Ignoring the command of code action '{Title}' because it did not carry a usable command identifier.",
				JsonElementReadHelpers.TryGetString(actionElement, "title") ?? "<untitled>");

			return null;
		}

		return new CodeActionCommandPayload(
			JsonElementReadHelpers.TryGetString(commandElement, "title"),
			command,
			TryCloneArguments(commandElement));
	}

	/// <summary>
	/// Clones the optional arguments of a command element so the payload stays valid after its source document is disposed.
	/// </summary>
	/// <param name="commandElement">The command element to read from.</param>
	/// <returns>The cloned arguments in wire order, or <see langword="null"/> when the command carries none.</returns>
	private static List<JsonElement>? TryCloneArguments(JsonElement commandElement)
	{
		if (!JsonElementReadHelpers.TryGetProperty(commandElement, "arguments", out JsonElement argumentsElement)
			|| argumentsElement.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		var arguments = new List<JsonElement>(argumentsElement.GetArrayLength());

		foreach (JsonElement argumentElement in argumentsElement.EnumerateArray())
			arguments.Add(argumentElement.Clone());

		return arguments.Count > 0 ? arguments : null;
	}

	/// <summary>
	/// Clones the opaque <c>data</c> member of a code-action element.
	/// </summary>
	/// <param name="actionElement">The action element to read from.</param>
	/// <returns>The detached clone, or <see langword="null"/> when the action carries no data.</returns>
	private static JsonElement? TryCloneData(JsonElement actionElement)
		=> JsonElementReadHelpers.TryGetProperty(actionElement, "data", out JsonElement dataElement) && dataElement.ValueKind != JsonValueKind.Null
			? dataElement.Clone()
			: null;
}
