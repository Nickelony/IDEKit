namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Represents the outcome of building a <see cref="JsonSchemaVocabularyIndex"/> from a schema.
/// Failed builds expose diagnostics; successful builds may also contain diagnostics for skipped
/// entries.
/// </summary>
public sealed class JsonSchemaVocabularyIndexResult
{
	/// <summary>
	/// Gets a value indicating whether an index is available. A successful build may still have diagnostics for
	/// individual entries that were skipped.
	/// </summary>
	public bool Succeeded { get; }

	/// <summary>
	/// Gets the built vocabulary index, or <see langword="null"/> when no index was produced.
	/// </summary>
	public JsonSchemaVocabularyIndex? Index { get; }

	/// <summary>
	/// Gets the diagnostics produced while building the vocabulary index. Failed builds contain an
	/// error message; successful builds may contain messages about skipped definitions.
	/// </summary>
	public IReadOnlyList<string> Diagnostics { get; }

	internal JsonSchemaVocabularyIndexResult(JsonSchemaVocabularyIndex? index, IReadOnlyList<string> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		Succeeded = index is not null;
		Index = index;
		Diagnostics = Array.AsReadOnly([.. diagnostics]);
	}
}
