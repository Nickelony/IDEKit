using System.IO;
using Nickelony.IDEKit.JsonSchema;

namespace Nickelony.Samples;

/// <summary>The JSON Schema of the sample's settings file, and the vocabulary index that its completion and hover read.</summary>
internal static class SampleSettingsSchema
{
	internal const string Text = """
		{
		  "$schema": "http://json-schema.org/draft-07/schema#",
		  "type": "object",
		  "properties": {
		    "theme": { "type": "string", "enum": ["Light", "Dark"], "description": "The color theme of the editor." },
		    "fontSize": { "type": "integer", "description": "The editor font size in points." },
		    "tabSize": { "type": "integer", "description": "The number of spaces one indentation level inserts, or the width of a tab, in a file that does not show its own size." },
		    "hoverDelay": { "type": "integer", "description": "How many milliseconds the pointer rests over a name before its hover shows." },
		    "trimTrailingWhitespace": { "type": "boolean", "description": "Whether saving removes the trailing whitespace of every line." },
		    "runCommand": { "type": "string", "description": "The command the Run button starts." }
		  }
		}
		""";

	/// <summary>The vocabulary of the schema: its property names with their descriptions, and its enumerated values.</summary>
	internal static JsonSchemaVocabularyIndex Index { get; } = Build();

	private static JsonSchemaVocabularyIndex Build()
	{
		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(new StringReader(Text));

		JsonSchemaVocabularyIndexFailure? failure = result.Failure;

		return result.Index ?? throw new InvalidOperationException("The sample schema is invalid.", failure?.Exception);
	}
}
