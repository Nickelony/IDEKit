using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>Walks the schemas of one JSON Schema document, follows its same-document <c>$ref</c> values, and guards against cycles.</summary>
internal sealed class SchemaTraversal
{
	private static readonly string[] s_definitionSectionNames = ["definitions", "$defs"];

	private readonly JsonElement _root;
	private readonly List<string> _warnings;

	// A schema is identified by its canonical JSON Pointer, so one schema reached directly and through a $ref is visited once.
	private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
	private readonly List<JsonElement> _schemas = [];
	private readonly Stack<(JsonElement Schema, string Pointer)> _pending = new();
	private readonly List<(JsonElement Schema, string Pointer)> _children = [];

	private SchemaTraversal(JsonElement root, List<string> warnings)
	{
		_root = root;
		_warnings = warnings;
	}

	/// <summary>
	/// Returns the schema objects of a document: the root and the schemas reachable through properties, array items, combinators, and
	/// resolved <c>$ref</c> targets, then the same for each entry of the root's <c>definitions</c> and <c>$defs</c> sections.
	/// </summary>
	/// <param name="root">The root schema, which is a JSON object or a boolean.</param>
	/// <param name="warnings">Receives a message for each value in a schema position that is not a schema and each <c>$ref</c> that does not resolve.</param>
	/// <remarks>
	/// A <c>$ref</c> resolves when it is <c>#</c> or a JSON Pointer fragment such as <c>#/$defs/name</c> into the root document.
	/// A reference to another document or to a named anchor does not resolve.
	/// </remarks>
	internal static IReadOnlyList<JsonElement> FlattenSchemas(JsonElement root, List<string> warnings)
	{
		var traversal = new SchemaTraversal(root, warnings);

		traversal.WalkDocument();

		return traversal._schemas;
	}

	private static string EscapeSegment(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

	private static string UnescapeSegment(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

	private static bool TryStep(JsonElement current, string name, out JsonElement next)
	{
		if (current.ValueKind == JsonValueKind.Object)
			return current.TryGetProperty(name, out next);

		// An array index is a decimal number without a sign or a leading zero.
		if (
			current.ValueKind == JsonValueKind.Array
			&& (name == "0" || (name.Length > 0 && name[0] != '0'))
			&& int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
			&& index < current.GetArrayLength()
		)
		{
			next = current[index];
			return true;
		}

		next = default;

		return false;
	}

	private void WalkDocument()
	{
		Walk(_root, string.Empty);

		if (_root.ValueKind != JsonValueKind.Object)
			return;

		// Walk the definition sections directly so definitions that nothing references are included as well.
		foreach (string sectionName in s_definitionSectionNames)
		{
			if (!_root.TryGetProperty(sectionName, out JsonElement section) || section.ValueKind != JsonValueKind.Object)
				continue;

			string sectionPointer = "/" + EscapeSegment(sectionName);

			foreach (JsonProperty definition in section.EnumerateObject())
				Walk(definition.Value, sectionPointer + "/" + EscapeSegment(definition.Name));
		}
	}

	private void Walk(JsonElement start, string startPointer)
	{
		AddChild(start, startPointer);
		PushChildren();

		while (_pending.Count > 0)
		{
			(JsonElement schema, string pointer) = _pending.Pop();

			if (!_visited.Add(pointer))
				continue;

			_schemas.Add(schema);

			AddChildren(schema, pointer);
			PushChildren();
		}
	}

	// The children go on the stack in reverse, so they pop in declaration order and the walk is depth-first in document order.
	private void PushChildren()
	{
		for (int index = _children.Count - 1; index >= 0; index--)
			_pending.Push(_children[index]);

		_children.Clear();
	}

	private void AddChildren(JsonElement schema, string pointer)
	{
		if (schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object)
		{
			string propertiesPointer = pointer + "/properties";

			foreach (JsonProperty property in properties.EnumerateObject())
				AddChild(property.Value, propertiesPointer + "/" + EscapeSegment(property.Name));
		}

		if (schema.TryGetProperty("items", out JsonElement items))
		{
			if (items.ValueKind == JsonValueKind.Array)
				AddArrayChildren(items, pointer + "/items");
			else
				AddChild(items, pointer + "/items");
		}

		AddArrayChildren(schema, "oneOf", pointer);
		AddArrayChildren(schema, "anyOf", pointer);
		AddArrayChildren(schema, "allOf", pointer);

		// Follow the reference last, after the schema's own keywords.
		if (schema.TryGetProperty("$ref", out JsonElement reference) && reference.ValueKind == JsonValueKind.String && reference.GetString() is { } text)
			AddReferenceTarget(text, pointer);
	}

	private void AddArrayChildren(JsonElement schema, string keyword, string pointer)
	{
		if (schema.TryGetProperty(keyword, out JsonElement array) && array.ValueKind == JsonValueKind.Array)
			AddArrayChildren(array, pointer + "/" + keyword);
	}

	private void AddArrayChildren(JsonElement array, string arrayPointer)
	{
		int index = 0;

		foreach (JsonElement element in array.EnumerateArray())
		{
			AddChild(element, arrayPointer + "/" + index.ToString(CultureInfo.InvariantCulture));
			index++;
		}
	}

	private void AddChild(JsonElement value, string pointer)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Object:
				_children.Add((value, pointer));
				break;

			case JsonValueKind.True:
			case JsonValueKind.False:
				// A boolean schema holds no property, constant, or nested schema.
				break;

			default:
				_warnings.Add($"The value at '#{pointer}' is not a schema; it was skipped.");
				break;
		}
	}

	private void AddReferenceTarget(string reference, string pointer)
	{
		if (!TryResolve(_root, reference, out JsonElement target, out string targetPointer))
		{
			_warnings.Add($"The reference '{reference}' at '#{pointer}' does not resolve to a location in the schema document; it was skipped.");
			return;
		}

		AddChild(target, targetPointer);
	}

	/// <summary>Resolves a same-document reference and returns the canonical JSON Pointer of its target.</summary>
	internal static bool TryResolve(JsonElement root, string reference, out JsonElement target, out string pointer)
	{
		target = root;
		pointer = string.Empty;

		if (reference.Length == 0 || reference[0] != '#')
			return false;

		if (reference.Length == 1)
			return true;

		// A fragment that does not start with '/' names an anchor, which this walk does not resolve.
		if (reference[1] != '/')
			return false;

		// The fragment is percent-decoded first, and the JSON Pointer is read from the decoded text.
		string[] tokens = Uri.UnescapeDataString(reference[2..]).Split('/');
		var canonical = new StringBuilder();

		foreach (string token in tokens)
		{
			string name = UnescapeSegment(token);

			if (!TryStep(target, name, out JsonElement next))
				return false;

			target = next;
			canonical.Append('/').Append(EscapeSegment(name));
		}

		pointer = canonical.ToString();

		return true;
	}
}
