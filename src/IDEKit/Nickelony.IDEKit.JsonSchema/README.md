# Nickelony.IDEKit.JsonSchema

> **Experimental optional integration.** This package is an opt-in leaf; it is
> not part of the core editor contract surface and may change outside the
> family's normal preview cadence. It is not a JSON Schema validator and not a
> context-aware completion engine.

Isolated JSON Schema vocabulary indexing for schema-driven completion, hover,
and highlighting in text editors.

## Getting started

```sh
dotnet add package Nickelony.IDEKit.JsonSchema
```

Build an index from a schema document and read the flattened vocabulary from the
result:

```csharp
using Nickelony.IDEKit.JsonSchema;

var builder = new JsonSchemaVocabularyIndexBuilder();
JsonSchemaVocabularyIndexResult result = builder.Build(new StringReader(schemaText));

if (result.Index is { } index)
{
    foreach (JsonSchemaVocabularyPropertyDescriptor property in index.Properties)
        Console.WriteLine(property.Name);
}
```

## Dependencies

The package depends only on `Newtonsoft.Json.Schema` and deliberately stays out
of `Nickelony.IDEKit.Core` and `Nickelony.IDEKit.IntelliSense`, so a host can opt into
schema support without coupling its core text primitives to the JSON Schema
framework.

## What's inside

The package builds an immutable **vocabulary index** from a JSON Schema document
(`JSchema` objects, `JToken` schema tokens, or schema text readers). The index
**flattens the reachable schema into one global vocabulary**: property names
and constant keywords are collected across the root, schemas reachable through
properties, array items, combinators (`oneOf` / `anyOf` / `allOf`), and
resolved `$ref` targets, plus the root's `definitions` / `$defs` sections, then
**deduplicated so the first deterministic occurrence wins**. Validation-only
keywords that carry nested schemas (for example `additionalProperties`,
`patternProperties`, `not`, and `if` / `then` / `else`) are not traversed. The
public result is deliberately context-free: descriptors carry a property name,
its declared JSON types, and its description only, with no schema path or
location information. The package owns cycle-safe schema traversal and the
property/constant extraction that schema-aware editor features consume, and it
returns an explicit load result with diagnostics instead of logging or touching
file paths.

- `JsonSchemaVocabularyIndexBuilder` - builds `JsonSchemaVocabularyIndex` values
  from schema objects, tokens, and readers, with optional
  `JSchemaReaderSettings` for external-reference resolution.
- `JsonSchemaVocabularyIndex` - the flattened, deduplicated global vocabulary:
  schema-wide property descriptors and constant keywords reachable from the
  root, including `definitions` and `$defs` sections.
- `JsonSchemaVocabularyIndexResult` - explicit load outcome with diagnostics;
  parse and definition failures are reported here rather than thrown or logged.
- `JsonSchemaVocabularyPropertyDescriptor` / `JsonSchemaPropertyType` -
  context-free property descriptors (name, JSON types, description, array
  classification). Duplicate property names are deduplicated and the first
  deterministic occurrence wins.

Hosts keep their file loading, resource paths, logging, and language-specific
keyword categorization (for example the collection/property/constant
classification hosts derive from the descriptors).

## License

MIT © 2026 Kewin Kupilas.
