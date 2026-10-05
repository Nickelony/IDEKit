namespace Nickelony.IDEKit.JsonSchema;

/// <summary>Describes why a <see cref="JsonSchemaVocabularyIndex"/> could not be built.</summary>
/// <param name="Code">One of the <see cref="JsonSchemaVocabularyIndexFailureCodes"/> values, which names the step that failed.</param>
/// <param name="Message">A human-readable failure message that includes the message of <paramref name="Exception"/>.</param>
/// <param name="Exception">The exception the builder caught while reading or parsing the schema, or a <see cref="System.Text.Json.JsonException"/> that describes a root that is not a schema.</param>
/// <remarks>
/// Use <see cref="Message"/> for display or logging; <see cref="Exception"/> keeps the evidence for a host that wants the stack or the type.
/// </remarks>
public sealed record JsonSchemaVocabularyIndexFailure(string Code, string Message, Exception Exception);
