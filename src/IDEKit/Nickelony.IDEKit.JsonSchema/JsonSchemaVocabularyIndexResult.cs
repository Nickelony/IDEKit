namespace Nickelony.IDEKit.JsonSchema;

/// <summary>Represents the outcome of building a <see cref="JsonSchemaVocabularyIndex"/> from a schema.</summary>
/// <remarks>
/// A build either produces an index, which may carry warnings, or fails with a <see cref="Failure"/>; the two are never combined.
/// </remarks>
public sealed class JsonSchemaVocabularyIndexResult
{
	/// <summary>Indicates whether an index is available.</summary>
	public bool Succeeded { get; }

	/// <summary>The built vocabulary index, or <see langword="null"/> when the build failed.</summary>
	public JsonSchemaVocabularyIndex? Index { get; }

	/// <summary>The messages about schema values the build skipped, such as a value that is not a schema or a <c>$ref</c> that does not resolve; empty when nothing was skipped and when the build failed.</summary>
	public IReadOnlyList<string> Warnings { get; }

	/// <summary>The reason the build failed, or <see langword="null"/> when an index is available.</summary>
	public JsonSchemaVocabularyIndexFailure? Failure { get; }

	internal JsonSchemaVocabularyIndexResult(JsonSchemaVocabularyIndex index, IReadOnlyList<string> warnings)
	{
		Succeeded = true;
		Index = index;
		Warnings = Array.AsReadOnly([.. warnings]);
	}

	internal JsonSchemaVocabularyIndexResult(JsonSchemaVocabularyIndexFailure failure)
	{
		Failure = failure;
		Warnings = [];
	}
}
