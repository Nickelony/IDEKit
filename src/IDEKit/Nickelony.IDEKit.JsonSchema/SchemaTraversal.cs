using Newtonsoft.Json.Schema;

namespace Nickelony.IDEKit.JsonSchema;

/// <summary>
/// Traverses schemas reachable from a root schema and guards against cycles.
/// </summary>
internal static class SchemaTraversal
{
	/// <summary>
	/// Returns the root schema and nested schemas reachable through properties, array items,
	/// combinators, and resolved <c>$ref</c> targets.
	/// </summary>
	internal static IReadOnlyList<JSchema> FlattenSchemas(JSchema schema)
	{
		var visited = new HashSet<JSchema>();
		var result = new List<JSchema>();

		Visit(schema);

		return result;

		void Visit(JSchema current)
		{
			if (!visited.Add(current))
				return;

			result.Add(current);

			if (current.Properties is not null)
			{
				foreach (JSchema property in current.Properties.Values)
					Visit(property);
			}

			if (current.Items is not null)
			{
				foreach (JSchema item in current.Items)
					Visit(item);
			}

			foreach (JSchema nestedSchema in current.OneOf ?? [])
				Visit(nestedSchema);

			foreach (JSchema nestedSchema in current.AnyOf ?? [])
				Visit(nestedSchema);

			foreach (JSchema nestedSchema in current.AllOf ?? [])
				Visit(nestedSchema);

			// Follow a reference when its resolved target is available.
			if (current.Ref is not null)
				Visit(current.Ref);
		}
	}
}
