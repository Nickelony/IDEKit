using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System.Text;

namespace Nickelony.IDEKit.JsonSchema.Tests;

[TestClass]
public sealed class JsonSchemaVocabularyIndexBuilderTests
{
	private static readonly JsonSchemaVocabularyIndexBuilder s_builder = new();

	[TestMethod]
	public void Build_ValidSchemaWithoutProperties_ReturnsEmptyIndex()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse("{ \"type\": \"object\" }"));

		Assert.IsTrue(result.Succeeded);
		Assert.IsNotNull(result.Index);
		Assert.AreEqual(0, result.Index.Properties.Count);
		Assert.AreEqual(0, result.Index.Constants.Count);
		Assert.AreEqual(0, result.Diagnostics.Count);
	}

	[TestMethod]
	public void Build_WithDefinitionsAndReferences_IncludesReferencedAndUnreferencedContent()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(DefsAndRefsFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// The unreferenced $defs entry is included even though no property references it.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "only_in_defs"));

		// The path schema is reached through a $ref from the root and the legacy definition.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "file"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "file_type"));

		// Legacy definitions content is included as well.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "title_name"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "path"));

		// Enum values inside a $defs definition are included.
		Assert.IsTrue(index.Constants.Contains("level"));
		Assert.IsTrue(index.Constants.Contains("cutscene"));
	}

	[TestMethod]
	public void Build_WithUnreferencedDefs_IncludesDefinitionProperties()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(DefsOnlyFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.IsTrue(index.Properties.Any(p => p.Name == "root_prop"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "item_name"));
	}

	[TestMethod]
	public void Build_WithDuplicatePropertiesAndConstants_DeduplicatesEntries()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(DuplicateKeywordsFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// Duplicate property names and enum values are each included only once.
		Assert.AreEqual(1, index.Properties.Count(p => p.Name == "shared"));
		Assert.AreEqual(1, index.Constants.Count(name => name == "ENGINE_1"));
		Assert.AreEqual(2, index.Constants.Count(name => name.StartsWith("ENGINE_", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Build_WithDescriptions_IncludesPropertyDescriptions()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(DescriptionsFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.AreEqual("Human-readable level name.", index.Properties.First(p => p.Name == "name").Description);
		Assert.AreEqual("Name of an individual level.", index.Properties.First(p => p.Name == "level_name").Description);
	}

	[TestMethod]
	public void Build_RecursiveSchema_IndexesEachPropertyOnce()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(RecursiveFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.AreEqual(1, index.Properties.Count(p => p.Name == "value"));
		Assert.AreEqual(1, index.Properties.Count(p => p.Name == "children"));
	}

	[TestMethod]
	public void Build_WithArrayItems_TraversesNestedSchemas()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(ItemsFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// Array items can reference definitions or contain inline nested schemas.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "levels"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "title_name"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "inline_name"));
	}

	[TestMethod]
	public void Build_WithCombinators_TraversesNestedSchemas()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(CombinatorsFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// Schemas nested in combinators are still indexed.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "one_of_prop"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "any_of_prop"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "all_of_prop"));
		Assert.IsTrue(index.Constants.Contains("ONE_OF_VALUE"));
		Assert.IsTrue(index.Constants.Contains("ANY_OF_VALUE"));
		Assert.IsTrue(index.Constants.Contains("ALL_OF_VALUE"));
	}

	[TestMethod]
	public void Build_AcrossSchemaDrafts_IndexesDefinitionsAndDefs()
	{
		// Draft-04 schema using the legacy "definitions" section.
		JsonSchemaVocabularyIndexResult draft04 = s_builder.Build(JSchema.Parse(Draft04Fixture));

		Assert.IsTrue(draft04.Succeeded);
		Assert.IsTrue(draft04.Index!.Properties.Any(p => p.Name == "legacy_name"));
		Assert.IsTrue(draft04.Index.Constants.Contains("DRAFT04"));

		// 2020-12 schema using the modern "$defs" section.
		JsonSchemaVocabularyIndexResult draft2020 = s_builder.Build(JSchema.Parse(Draft2020Fixture));

		Assert.IsTrue(draft2020.Succeeded);
		Assert.IsTrue(draft2020.Index!.Properties.Any(p => p.Name == "modern_name"));
		Assert.IsTrue(draft2020.Index.Constants.Contains("DRAFT2020"));
	}

	[TestMethod]
	public void Build_ExternalReference_IndexesResolvedContent()
	{
		var resolver = new InMemorySchemaResolver(new Dictionary<Uri, string>
		{
			[new Uri("https://example.com/common.json")] = CommonSchema
		});

		var settings = new JSchemaReaderSettings
		{
			BaseUri = new Uri("https://example.com/root.json"),
			Resolver = resolver
		};

		using var reader = new StringReader(ExternalReferenceRootFixture);
		JsonSchemaVocabularyIndexResult result = s_builder.Build(reader, settings);

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// The resolver supplies the referenced schema, whose properties and enum values are indexed.
		Assert.IsTrue(index.Properties.Any(p => p.Name == "file"));
		Assert.IsTrue(index.Properties.Any(p => p.Name == "file_type"));
		Assert.IsTrue(index.Constants.Contains("level"));
		Assert.IsTrue(index.Constants.Contains("cutscene"));
	}

	[TestMethod]
	public void Build_UnresolvableExternalReference_ReturnsFailureWithDiagnostics()
	{
		using var reader = new StringReader(ExternalReferenceRootFixture);
		JsonSchemaVocabularyIndexResult result = s_builder.Build(reader);

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsTrue(result.Diagnostics.Count > 0);
	}

	[TestMethod]
	public void Build_InvalidJson_ReturnsFailureWithDiagnostics()
	{
		using var reader = new StringReader("this is not valid json");
		JsonSchemaVocabularyIndexResult result = s_builder.Build(reader);

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsTrue(result.Diagnostics.Count > 0);
	}

	[TestMethod]
	public void Build_MalformedDefinitionEntry_ReportsDiagnostic()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(MalformedDefinitionFixture));

		Assert.IsTrue(result.Succeeded);

		// The valid root property remains indexed while the build reports one diagnostic.
		Assert.IsTrue(result.Index!.Properties.Any(p => p.Name == "name"));
		Assert.AreEqual(1, result.Diagnostics.Count);
	}

	[TestMethod]
	public void Build_SchemaToken_IndexesProperties()
	{
		JToken token = JToken.Parse(DefsOnlyFixture);
		JsonSchemaVocabularyIndexResult result = s_builder.Build(token);

		Assert.IsTrue(result.Succeeded);
		Assert.IsTrue(result.Index!.Properties.Any(p => p.Name == "item_name"));
	}

	[TestMethod]
	public void Build_NonSchemaToken_ReturnsFailureWithDiagnostics()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(new JValue("not a schema"));

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsTrue(result.Diagnostics.Count > 0);
	}

	[TestMethod]
	public void Build_DuplicatePropertyDifferentTypes_FirstOccurrenceWins()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(DuplicatePropertyFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// The root declaration is encountered before the same-named declaration in $defs, so its
		// type and description are retained.
		JsonSchemaVocabularyPropertyDescriptor descriptor = index.Properties.Single(p => p.Name == "shared");

		Assert.AreEqual(1, index.Properties.Count(p => p.Name == "shared"));
		Assert.AreEqual(JsonSchemaPropertyType.String, descriptor.Types.Single());
		Assert.AreEqual("Root shared property.", descriptor.Description);
	}

	[TestMethod]
	public void Build_NestedProperties_AreIndexedWithoutPathInformation()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(NestedPathFixture));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// Nested properties are included in the flat vocabulary without their schema paths, and the
		// container property is included as its own entry.
		Assert.AreEqual(3, index.Properties.Count);
		Assert.AreEqual("Root-level property.", index.Properties.Single(p => p.Name == "root_name").Description);
		Assert.AreEqual("Nested property.", index.Properties.Single(p => p.Name == "nested_name").Description);
		Assert.IsTrue(index.Properties.Any(p => p.Name == "child" && p.Types.Single() == JsonSchemaPropertyType.Object));
	}

	[TestMethod]
	public void Build_WithConstValues_IndexesStringConstantsOnly()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(
			"""
			{
			  "type": "object",
			  "properties": {
			    "kind": { "const": "level" },
			    "count": { "const": 3 }
			  }
			}
			"""));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.IsTrue(index.Constants.Contains("level"));
		Assert.IsFalse(index.Constants.Contains("3"));
	}

	[TestMethod]
	public void Build_MultiTypeUnion_ReportsTypesInEnumDeclarationOrder()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(
			"""
			{
			  "type": "object",
			  "properties": {
			    "value": { "type": [ "null", "array", "object" ] }
			  }
			}
			"""));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyPropertyDescriptor descriptor = result.Index!.Properties.Single(p => p.Name == "value");

		CollectionAssert.AreEqual(
			new[] { JsonSchemaPropertyType.Object, JsonSchemaPropertyType.Array, JsonSchemaPropertyType.Null },
			descriptor.Types.ToArray());
		Assert.IsFalse(descriptor.IsArrayOnly);
	}

	[TestMethod]
	public void Build_SingleArrayType_ReportsIsArrayOnly()
	{
		JsonSchemaVocabularyIndexResult result = s_builder.Build(JSchema.Parse(
			"""
			{
			  "type": "object",
			  "properties": {
			    "levels": { "type": "array" }
			  }
			}
			"""));

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyPropertyDescriptor descriptor = result.Index!.Properties.Single(p => p.Name == "levels");

		Assert.IsTrue(descriptor.IsArrayOnly);
	}

	private const string DefsAndRefsFixture =
		"""
		{
		  "$defs": {
		    "path": {
		      "type": "object",
		      "properties": {
		        "file": { "type": "string" },
		        "file_type": { "enum": [ "level", "cutscene" ] }
		      }
		    },
		    "hidden": {
		      "type": "object",
		      "properties": {
		        "only_in_defs": { "type": "integer" }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "name": { "type": "string" },
		    "levels": {
		      "type": "array",
		      "items": { "$ref": "#/definitions/level" }
		    },
		    "main_script": { "$ref": "#/$defs/path" }
		  },
		  "definitions": {
		    "level": {
		      "type": "object",
		      "properties": {
		        "title_name": { "type": "string" },
		        "path": { "$ref": "#/$defs/path" }
		      }
		    }
		  }
		}
		""";

	private const string DefsOnlyFixture =
		"""
		{
		  "$defs": {
		    "item": {
		      "type": "object",
		      "properties": {
		        "item_name": { "type": "string" }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "root_prop": { "type": "string" }
		  }
		}
		""";

	private const string DuplicateKeywordsFixture =
		"""
		{
		  "$defs": {
		    "shared": {
		      "type": "object",
		      "properties": {
		        "shared": { "type": "string" },
		        "engine": { "enum": [ "ENGINE_1", "ENGINE_2" ] }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "name": { "type": "string" },
		    "shared": { "$ref": "#/$defs/shared" },
		    "engine": { "enum": [ "ENGINE_1" ] }
		  }
		}
		""";

	private const string DescriptionsFixture =
		"""
		{
		  "$defs": {
		    "level": {
		      "type": "object",
		      "properties": {
		        "level_name": { "type": "string", "description": "Name of an individual level." }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "name": { "type": "string", "description": "Human-readable level name." },
		    "levels": {
		      "type": "array",
		      "items": { "$ref": "#/$defs/level" }
		    }
		  }
		}
		""";

	private const string RecursiveFixture =
		"""
		{
		  "$defs": {
		    "node": {
		      "type": "object",
		      "properties": {
		        "value": { "type": "string" },
		        "children": {
		          "type": "array",
		          "items": { "$ref": "#/$defs/node" }
		        }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "root": { "$ref": "#/$defs/node" }
		  }
		}
		""";

	private const string ItemsFixture =
		"""
		{
		  "definitions": {
		    "level": {
		      "type": "object",
		      "properties": {
		        "title_name": { "type": "string" }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "levels": {
		      "type": "array",
		      "items": {
		        "type": "object",
		        "properties": {
		          "inline_name": { "type": "string" }
		        }
		      }
		    },
		    "cutscenes": {
		      "type": "array",
		      "items": { "$ref": "#/definitions/level" }
		    }
		  }
		}
		""";

	private const string CombinatorsFixture =
		"""
		{
		  "type": "object",
		  "properties": {
		    "choice": {
		      "oneOf": [
		        {
		          "type": "object",
		          "properties": {
		            "one_of_prop": { "type": "string" }
		          }
		        },
		        {
		          "type": "object",
		          "properties": {
		            "any_of_prop": { "enum": [ "ANY_OF_VALUE" ] }
		          }
		        }
		      ]
		    },
		    "merged": {
		      "allOf": [
		        {
		          "type": "object",
		          "properties": {
		            "all_of_prop": { "enum": [ "ALL_OF_VALUE" ] }
		          }
		        },
		        {
		          "type": "object",
		          "properties": {
		            "one_of_prop": { "type": "string" }
		          }
		        }
		      ]
		    }
		  },
		  "anyOf": [
		    {
		      "type": "object",
		      "properties": {
		        "any_of_prop": { "enum": [ "ANY_OF_VALUE" ] }
		      }
		    },
		    {
		      "type": "object",
		      "properties": {
		        "one_of_prop": { "enum": [ "ONE_OF_VALUE" ] }
		      }
		    }
		  ]
		}
		""";

	private const string Draft04Fixture =
		"""
		{
		  "$schema": "http://json-schema.org/draft-04/schema#",
		  "definitions": {
		    "legacy": {
		      "type": "object",
		      "properties": {
		        "legacy_name": { "enum": [ "DRAFT04" ] }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "legacy": { "$ref": "#/definitions/legacy" }
		  }
		}
		""";

	private const string Draft2020Fixture =
		"""
		{
		  "$schema": "https://json-schema.org/draft/2020-12/schema",
		  "$defs": {
		    "modern": {
		      "type": "object",
		      "properties": {
		        "modern_name": { "enum": [ "DRAFT2020" ] }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "modern": { "$ref": "#/$defs/modern" }
		  }
		}
		""";

	private const string ExternalReferenceRootFixture =
		"""
		{
		  "type": "object",
		  "properties": {
		    "name": { "type": "string" },
		    "shared": { "$ref": "https://example.com/common.json#/definitions/shared" }
		  }
		}
		""";

	private const string CommonSchema =
		"""
		{
		  "definitions": {
		    "shared": {
		      "type": "object",
		      "properties": {
		        "file": { "type": "string" },
		        "file_type": { "enum": [ "level", "cutscene" ] }
		      }
		    }
		  }
		}
		""";

	private const string MalformedDefinitionFixture =
		"""
		{
		  "$defs": {
		    "broken": "not a schema object"
		  },
		  "type": "object",
		  "properties": {
		    "name": { "type": "string" }
		  }
		}
		""";

	private const string DuplicatePropertyFixture =
		"""
		{
		  "$defs": {
		    "shared_def": {
		      "type": "object",
		      "properties": {
		        "shared": { "type": "array", "description": "Array shared property from $defs." }
		      }
		    }
		  },
		  "type": "object",
		  "properties": {
		    "shared": { "type": "string", "description": "Root shared property." },
		    "uses_def": { "$ref": "#/$defs/shared_def" }
		  }
		}
		""";

	private const string NestedPathFixture =
		"""
		{
		  "type": "object",
		  "properties": {
		    "root_name": { "type": "string", "description": "Root-level property." },
		    "child": {
		      "type": "object",
		      "properties": {
		        "nested_name": { "type": "integer", "description": "Nested property." }
		      }
		    }
		  }
		}
		""";

	/// <summary>
	/// Supplies external schema text from an in-memory URI-to-text map.
	/// </summary>
	private sealed class InMemorySchemaResolver : JSchemaResolver
	{
		private readonly IReadOnlyDictionary<Uri, string> _schemas;

		public InMemorySchemaResolver(IReadOnlyDictionary<Uri, string> schemas) => _schemas = schemas;

		public override Stream GetSchemaResource(ResolveSchemaContext context, SchemaReference reference)
		{
			if (reference.BaseUri is not null && _schemas.TryGetValue(Normalize(reference.BaseUri), out string? content))
				return new MemoryStream(Encoding.UTF8.GetBytes(content));

			return Stream.Null;
		}

		private static Uri Normalize(Uri uri)
		{
			if (!string.IsNullOrEmpty(uri.Fragment))
				return new Uri(uri.GetLeftPart(UriPartial.Path));

			return uri;
		}
	}
}
