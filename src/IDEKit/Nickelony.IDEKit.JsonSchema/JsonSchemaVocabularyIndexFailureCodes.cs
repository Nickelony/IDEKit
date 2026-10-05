namespace Nickelony.IDEKit.JsonSchema;

/// <summary>Lists the stable failure codes of <see cref="JsonSchemaVocabularyIndexFailure"/>.</summary>
public static class JsonSchemaVocabularyIndexFailureCodes
{
	/// <summary>The schema text could not be read from its source.</summary>
	public const string ReadFailed = "ReadFailed";

	/// <summary>The schema text is not valid JSON, or the schema root is neither an object nor a boolean.</summary>
	public const string ParseFailed = "ParseFailed";
}
