# Nickelony.IDEKit.JsonSchema

> **Experimental optional integration.** This package is an opt-in leaf; it is
> not part of the core editor contract surface and may change outside the
> family's normal preview cadence. It is not a JSON Schema validator and not a
> context-aware completion engine.

Isolated JSON Schema vocabulary indexing for schema-driven completion, hover, and highlighting in text
editors. It references no other package and parses with `System.Text.Json`. It stays out of
`Nickelony.IDEKit.Core` and `Nickelony.IDEKit.LanguageFeatures`, so a host opts into schema support without
coupling its core text primitives to the JSON Schema framework.

## Getting started

```sh
dotnet add package Nickelony.IDEKit.JsonSchema --prerelease
```

Build an index from a schema document and read the flattened vocabulary from the
result:

```csharp
using Nickelony.IDEKit.JsonSchema;

const string schemaText = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(new StringReader(schemaText));

if (result.Index is { } index)
{
    foreach (JsonSchemaVocabularyPropertyDescriptor property in index.Properties)
        Console.WriteLine(property.Name);
}
```

## Remarks

- **One global vocabulary.** The index flattens the reachable schema into one list of property names and one list
  of string constants, taken from `const` and `enum` values.
- **Reachable schemas.** The walk covers the root, properties, array items, the `oneOf` / `anyOf` / `allOf`
  combinators, resolved `$ref` targets, and the root's `definitions` / `$defs` sections. It is cycle-safe.
- **Same-document references.** A `$ref` resolves when it is `#` or a JSON Pointer fragment into the same
  document, such as `#/$defs/name` or `#/properties/a/items`, with `~0` / `~1` escapes and percent-encoding. A
  reference to another document, to a `$anchor` name, or to a location that does not exist adds a `Warnings`
  entry and is skipped; it is not a failure.
- **A referencing property keeps its own keywords.** A property that is a `$ref` takes its `type` and
  `description` from beside the `$ref` when they are there, and from the schema the reference names when not.
- **First occurrence wins.** A property name or constant that occurs more than once keeps its first occurrence
  in traversal order.
- **Validation-only keywords are not traversed.** Nested schemas under `additionalProperties`,
  `patternProperties`, `not`, and `if` / `then` / `else` are skipped.
- **The result is context-free.** A descriptor carries a property name, its declared JSON types, its
  description, and its array classification only, with no schema path or location.
- **Failures are values.** `JsonSchemaVocabularyIndexResult` carries a `Failure` with the caught exception when
  no index was built, and `Warnings` for the values a successful build skipped; neither is thrown or logged.
- **Inputs.** `JsonSchemaVocabularyIndexBuilder.Build` accepts a schema text reader or a parsed `JsonElement`.
  The text may contain comments and trailing commas. The builder reads no files itself, so hosts keep their
  file loading, resource paths, logging, and language-specific keyword categorization.

## License

MIT © 2026 Kewin Kupilas.
