using System.Text.Json;

namespace Nickelony.IDEKit.JsonSchema.Tests;

[TestClass]
public sealed class JsonSchemaVocabularyIndexBuilderTests
{
	[TestMethod]
	public void Build_ValidSchemaWithoutProperties_ReturnsEmptyIndex()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom("{ \"type\": \"object\" }");

		Assert.IsTrue(result.Succeeded);
		Assert.IsNotNull(result.Index);
		Assert.IsEmpty(result.Index.Properties);
		Assert.IsEmpty(result.Index.Constants);
		Assert.IsEmpty(result.Warnings);
		Assert.IsNull(result.Failure);
	}

	[TestMethod]
	public void Build_WithDefinitionsAndReferences_IncludesReferencedAndUnreferencedContent()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(DefsAndRefsFixture);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		JsonSchemaVocabularyIndex index = result.Index!;

		// The unreferenced $defs entry is included even though no property references it.
		Assert.Contains(p => p.Name == "only_in_defs", index.Properties);

		// The path schema is reached through a $ref from the root and the definitions entry.
		Assert.Contains(p => p.Name == "file", index.Properties);
		Assert.Contains(p => p.Name == "file_type", index.Properties);

		// Content under the definitions keyword is included as well.
		Assert.Contains(p => p.Name == "title_name", index.Properties);
		Assert.Contains(p => p.Name == "path", index.Properties);

		// Enum values inside a $defs definition are included.
		Assert.Contains("level", index.Constants);
		Assert.Contains("cutscene", index.Constants);
	}

	[TestMethod]
	public void Build_WithUnreferencedDefs_IncludesDefinitionProperties()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(DefsOnlyFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.Contains(p => p.Name == "root_prop", index.Properties);
		Assert.Contains(p => p.Name == "item_name", index.Properties);
	}

	[TestMethod]
	public void Build_WithDuplicatePropertiesAndConstants_DeduplicatesEntries()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(DuplicateKeywordsFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		// Duplicate property names and enum values are each included only once.
		Assert.ContainsSingle(p => p.Name == "shared", index.Properties);
		Assert.ContainsSingle(name => name == "ENGINE_1", index.Constants);
		Assert.AreEqual(2, index.Constants.Count(name => name.StartsWith("ENGINE_", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Build_WithDescriptions_IncludesPropertyDescriptions()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(DescriptionsFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.AreEqual("Human-readable level name.", index.Properties.First(p => p.Name == "name").Description);
		Assert.AreEqual("Name of an individual level.", index.Properties.First(p => p.Name == "level_name").Description);
	}

	[TestMethod]
	public void Build_RecursiveSchema_IndexesEachPropertyOnce()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(RecursiveFixture);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.ContainsSingle(p => p.Name == "value", index.Properties);
		Assert.ContainsSingle(p => p.Name == "children", index.Properties);
	}

	[TestMethod]
	public void Build_WithArrayItems_TraversesNestedSchemas()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(ItemsFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		// Array items can reference definitions or contain inline nested schemas.
		Assert.Contains(p => p.Name == "levels", index.Properties);
		Assert.Contains(p => p.Name == "title_name", index.Properties);
		Assert.Contains(p => p.Name == "inline_name", index.Properties);
	}

	[TestMethod]
	public void Build_WithCombinators_TraversesNestedSchemas()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(CombinatorsFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		// Schemas nested in combinators are still indexed.
		Assert.Contains(p => p.Name == "one_of_prop", index.Properties);
		Assert.Contains(p => p.Name == "any_of_prop", index.Properties);
		Assert.Contains(p => p.Name == "all_of_prop", index.Properties);
		Assert.Contains("ONE_OF_VALUE", index.Constants);
		Assert.Contains("ANY_OF_VALUE", index.Constants);
		Assert.Contains("ALL_OF_VALUE", index.Constants);
	}

	[TestMethod]
	public void Build_AcrossSchemaDrafts_IndexesDefinitionsAndDefs()
	{
		// Draft-04 schema using its "definitions" section.
		JsonSchemaVocabularyIndexResult draft04 = BuildFrom(Draft04Fixture);

		Assert.IsTrue(draft04.Succeeded);
		Assert.Contains(p => p.Name == "legacy_name", draft04.Index!.Properties);
		Assert.Contains("DRAFT04", draft04.Index.Constants);

		// 2020-12 schema using the modern "$defs" section.
		JsonSchemaVocabularyIndexResult draft2020 = BuildFrom(Draft2020Fixture);

		Assert.IsTrue(draft2020.Succeeded);
		Assert.Contains(p => p.Name == "modern_name", draft2020.Index!.Properties);
		Assert.Contains("DRAFT2020", draft2020.Index.Constants);
	}

	[TestMethod]
	[DataRow("https://example.com/common.json#/definitions/shared")]
	[DataRow("common.json")]
	[DataRow("#/definitions/missing")]
	[DataRow("#named_anchor")]
	[DataRow("#/x-library/01")]
	[DataRow("#/x-library/-1")]
	[DataRow("#/x-library/5")]
	[DataRow("#/type/length")]
	public void Build_UnresolvableReference_ReportsWarningAndKeepsTheRestOfTheIndex(string reference)
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			$$"""
			{
			  "x-library": [ { "properties": { "library_name": {} } } ],
			  "type": "object",
			  "properties": {
			    "name": { "type": "string" },
			    "shared": { "$ref": "{{reference}}" }
			  }
			}
			"""
		);

		// An unresolvable reference is a skipped value, not a failure.
		Assert.IsTrue(result.Succeeded);
		Assert.IsNull(result.Failure);

		string warning = result.Warnings.Single();

		Assert.Contains($"'{reference}'", warning);
		Assert.Contains("'#/properties/shared'", warning);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.Contains(p => p.Name == "name", index.Properties);
		Assert.Contains(p => p.Name == "shared", index.Properties);
		Assert.DoesNotContain(p => p.Name == "library_name", index.Properties);
	}

	[TestMethod]
	public void Build_InvalidJson_ReturnsFailureWithTheCaughtException()
	{
		using var reader = new StringReader("this is not valid json");

		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(reader);

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsEmpty(result.Warnings);
		Assert.IsNotNull(result.Failure);
		Assert.IsNotEmpty(result.Failure.Message);
		Assert.IsNotNull(result.Failure.Exception);
		Assert.AreEqual(JsonSchemaVocabularyIndexFailureCodes.ParseFailed, result.Failure.Code);
	}

	[TestMethod]
	public void Build_UnreadableText_ReturnsFailureWithTheCaughtException()
	{
		using var reader = new ThrowingReader();

		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(reader);

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsNotNull(result.Failure);
		Assert.IsInstanceOfType<InvalidOperationException>(result.Failure.Exception);
		Assert.AreEqual(JsonSchemaVocabularyIndexFailureCodes.ReadFailed, result.Failure.Code);
	}

	[TestMethod]
	public void Build_MalformedDefinitionEntry_ReportsWarning()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(MalformedDefinitionFixture);

		Assert.IsTrue(result.Succeeded);

		// The valid root property remains indexed while the build reports one warning and no failure.
		Assert.Contains(p => p.Name == "name", result.Index!.Properties);
		Assert.HasCount(1, result.Warnings);
		Assert.Contains("'#/$defs/broken'", result.Warnings[0]);
		Assert.IsNull(result.Failure);
	}

	[TestMethod]
	public void Build_SchemaElement_IndexesProperties()
	{
		using JsonDocument document = JsonDocument.Parse(DefsOnlyFixture);

		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(document.RootElement);

		Assert.IsTrue(result.Succeeded);
		Assert.Contains(p => p.Name == "item_name", result.Index!.Properties);
	}

	[TestMethod]
	[DataRow("true")]
	[DataRow("false")]
	public void Build_BooleanRootSchema_ReturnsEmptyIndex(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);

		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(document.RootElement);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Index!.Properties);
		Assert.IsEmpty(result.Index.Constants);
		Assert.IsEmpty(result.Warnings);
	}

	[TestMethod]
	[DataRow("\"not a schema\"")]
	[DataRow("3")]
	[DataRow("null")]
	[DataRow("[ { \"type\": \"object\" } ]")]
	public void Build_NonSchemaElement_ReturnsFailureWithAnExceptionThatDescribesIt(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);

		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(document.RootElement);

		Assert.IsFalse(result.Succeeded);
		Assert.IsNull(result.Index);
		Assert.IsEmpty(result.Warnings);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(JsonSchemaVocabularyIndexFailureCodes.ParseFailed, result.Failure.Code);
		Assert.IsInstanceOfType<JsonException>(result.Failure.Exception);
		Assert.Contains(result.Failure.Exception.Message, result.Failure.Message);
	}

	[TestMethod]
	public void Build_DefaultElement_ReturnsFailure()
	{
		JsonSchemaVocabularyIndexResult result = JsonSchemaVocabularyIndexBuilder.Build(default(JsonElement));

		Assert.IsFalse(result.Succeeded);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(JsonSchemaVocabularyIndexFailureCodes.ParseFailed, result.Failure.Code);
	}

	[TestMethod]
	public void Build_NullReader_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => JsonSchemaVocabularyIndexBuilder.Build((TextReader)null!));
	}

	[TestMethod]
	public void Build_DuplicatePropertyDifferentTypes_FirstOccurrenceWins()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(DuplicatePropertyFixture);

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyIndex index = result.Index!;

		// The root declaration is encountered before the same-named declaration in $defs, so its
		// type and description are retained.
		JsonSchemaVocabularyPropertyDescriptor descriptor = index.Properties.Single(p => p.Name == "shared");

		Assert.ContainsSingle(p => p.Name == "shared", index.Properties);
		Assert.AreEqual(JsonSchemaPropertyType.String, descriptor.Types.Single());
		Assert.AreEqual("Root shared property.", descriptor.Description);
	}

	[TestMethod]
	public void Build_NestedProperties_AreIndexedWithoutPathInformation()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(NestedPathFixture);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		// Nested properties are included in the flat vocabulary without their schema paths, and the
		// container property is included as its own entry.
		Assert.HasCount(3, index.Properties);
		Assert.AreEqual("Root-level property.", index.Properties.Single(p => p.Name == "root_name").Description);
		Assert.AreEqual("Nested property.", index.Properties.Single(p => p.Name == "nested_name").Description);
		Assert.Contains(p => p.Name == "child" && p.Types.Single() == JsonSchemaPropertyType.Object, index.Properties);
	}

	[TestMethod]
	public void Build_WithConstValues_IndexesStringConstantsOnly()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "type": "object",
			  "properties": {
			    "kind": { "const": "level" },
			    "count": { "const": 3 }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.Contains("level", index.Constants);
		Assert.DoesNotContain("3", index.Constants);
	}

	[TestMethod]
	public void Build_MultiTypeUnion_ReportsTypesInEnumDeclarationOrder()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "type": "object",
			  "properties": {
			    "value": { "type": [ "null", "array", "object" ] }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyPropertyDescriptor descriptor = result.Index!.Properties.Single(p => p.Name == "value");

		Assert.AreSequenceEqual([JsonSchemaPropertyType.Object, JsonSchemaPropertyType.Array, JsonSchemaPropertyType.Null], [.. descriptor.Types]);
		Assert.IsFalse(descriptor.IsArrayOnly);
	}

	[TestMethod]
	public void Build_SingleArrayType_ReportsIsArrayOnly()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "type": "object",
			  "properties": {
			    "levels": { "type": "array" }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		JsonSchemaVocabularyPropertyDescriptor descriptor = result.Index!.Properties.Single(p => p.Name == "levels");

		Assert.IsTrue(descriptor.IsArrayOnly);
	}

	[TestMethod]
	[DataRow("$defs")]
	[DataRow("definitions")]
	public void Build_DefinitionThatReferencesASibling_IndexesBothWithoutWarnings(string section)
	{
		// Neither entry is reached from the root, so each is indexed as a definition and the reference resolves against the whole document.
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			$$"""
			{
			  "type": "object",
			  "properties": { "root_prop": { "type": "string" } },
			  "{{section}}": {
			    "a": { "properties": { "x": { "$ref": "#/{{section}}/b" } } },
			    "b": { "type": "object", "properties": { "b_prop": { "type": "string" } } }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);

		JsonSchemaVocabularyIndex index = result.Index!;

		Assert.Contains(p => p.Name == "root_prop", index.Properties);
		Assert.Contains(p => p.Name == "x", index.Properties);
		Assert.Contains(p => p.Name == "b_prop", index.Properties);
	}

	[TestMethod]
	[DataRow("""{ "type":"object", "properties": { "r": { "$ref": "#/$defs/b" } }, "$defs": { "a": { "properties": { "x": { "$ref": "#/$defs/b" } } }, "b": { "type":"object", "properties": { "b_prop": { "type":"string" } } } } }""")]
	[DataRow("""{ "type":"object", "properties": { "r": { "$ref": "#/$defs/a" } }, "$defs": { "a": { "properties": { "x": { "$ref": "#/$defs/b" } } }, "b": { "type":"object", "properties": { "b_prop": { "type":"string" } } } } }""")]
	[DataRow("""{ "type":"object", "$defs": { "b": { "type":"object", "properties": { "b_prop": { "type":"string" } } }, "a": { "properties": { "x": { "$ref": "#/$defs/b" } } } } }""")]
	[DataRow("""{ "type":"object", "$defs": { "a": { "properties": { "x": { "$ref": "#/$defs/b" } } }, "b": { "type":"object", "properties": { "b_prop": { "type":"string" } } } } }""")]
	public void Build_SiblingReferenceInAnyDeclarationOrder_IndexesTheTargetWithoutWarnings(string json)
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(json);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.ContainsSingle(p => p.Name == "x", result.Index!.Properties);
		Assert.ContainsSingle(p => p.Name == "b_prop", result.Index.Properties);
	}

	[TestMethod]
	public void Build_ReferenceToTheRoot_IndexesEachPropertyOnce()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "type": "object",
			  "properties": {
			    "name": { "type": "string" },
			    "parent": { "$ref": "#" }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.HasCount(2, result.Index!.Properties);
	}

	[TestMethod]
	public void Build_MutuallyRecursiveReferences_Terminate()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "$defs": {
			    "a": { "properties": { "a_prop": { "$ref": "#/$defs/b" } } },
			    "b": { "properties": { "b_prop": { "$ref": "#/$defs/a" } } },
			    "loop": { "$ref": "#/$defs/loop" }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.HasCount(2, result.Index!.Properties);
	}

	[TestMethod]
	public void Build_ReferenceWithEscapedPointer_ResolvesTheTarget()
	{
		// The x-library keyword is not traversed, so a name below it is indexed only when a reference resolves it.
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "x-library": {
			    "a/b": { "properties": { "slash_name": {} } },
			    "c~d": { "properties": { "tilde_name": {} } },
			    "e f": { "properties": { "space_name": {} } },
			    "~1": { "properties": { "literal_name": {} } },
			    "50%": { "properties": { "percent_name": {} } }
			  },
			  "properties": {
			    "slash": { "$ref": "#/x-library/a~1b" },
			    "tilde": { "$ref": "#/x-library/c~0d" },
			    "space": { "$ref": "#/x-library/e%20f" },
			    "literal": { "$ref": "#/x-library/~01" },
			    "percent": { "$ref": "#/x-library/50%25" }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);

		string[] names = [.. result.Index!.Properties.Select(p => p.Name)];

		Assert.Contains("slash_name", names);
		Assert.Contains("tilde_name", names);
		Assert.Contains("space_name", names);
		Assert.Contains("literal_name", names);
		Assert.Contains("percent_name", names);
	}

	[TestMethod]
	public void Build_ReferenceIntoAnArray_ResolvesTheElementByIndex()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "x-library": [ { "properties": { "first_name": {} } }, { "properties": { "second_name": {} } } ],
			  "properties": { "pick": { "$ref": "#/x-library/1" } }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.Contains(p => p.Name == "second_name", result.Index!.Properties);
		Assert.DoesNotContain(p => p.Name == "first_name", result.Index.Properties);
	}

	[TestMethod]
	public void Build_ReferenceToAnInlineProperty_ResolvesTheTarget()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "properties": {
			    "copy": { "$ref": "#/properties/original" },
			    "original": { "properties": { "inner_name": {} } }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.ContainsSingle(p => p.Name == "inner_name", result.Index!.Properties);
	}

	[TestMethod]
	public void Build_ReferenceWithSiblingKeywords_KeepsItsOwnKeywordsAndTakesTheRestFromTheTarget()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "$defs": {
			    "shared": { "type": "object", "description": "Target description." },
			    "alias": { "$ref": "#/$defs/shared" },
			    "loop": { "$ref": "#/$defs/loop" }
			  },
			  "properties": {
			    "linked": { "$ref": "#/$defs/shared", "type": "string", "description": "Own description." },
			    "described": { "$ref": "#/$defs/shared", "description": "Own description." },
			    "bare": { "$ref": "#/$defs/shared" },
			    "chained": { "$ref": "#/$defs/alias" },
			    "cyclic": { "$ref": "#/$defs/loop" }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);

		JsonSchemaVocabularyPropertyDescriptor linked = result.Index!.Properties.Single(p => p.Name == "linked");
		JsonSchemaVocabularyPropertyDescriptor described = result.Index.Properties.Single(p => p.Name == "described");
		JsonSchemaVocabularyPropertyDescriptor bare = result.Index.Properties.Single(p => p.Name == "bare");
		JsonSchemaVocabularyPropertyDescriptor chained = result.Index.Properties.Single(p => p.Name == "chained");
		JsonSchemaVocabularyPropertyDescriptor cyclic = result.Index.Properties.Single(p => p.Name == "cyclic");

		Assert.AreEqual(JsonSchemaPropertyType.String, linked.Types.Single());
		Assert.AreEqual("Own description.", linked.Description);
		Assert.AreEqual(JsonSchemaPropertyType.Object, described.Types.Single());
		Assert.AreEqual("Own description.", described.Description);
		Assert.AreEqual(JsonSchemaPropertyType.Object, bare.Types.Single());
		Assert.AreEqual("Target description.", bare.Description);
		Assert.AreEqual(JsonSchemaPropertyType.Object, chained.Types.Single());
		Assert.AreEqual("Target description.", chained.Description);
		Assert.IsEmpty(cyclic.Types);
		Assert.IsNull(cyclic.Description);
	}

	[TestMethod]
	public void Build_DefinitionsSectionPrecedesDefsSection_FirstOccurrenceWins()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "$defs": { "modern": { "properties": { "shared": { "type": "integer" } } } },
			  "definitions": { "legacy": { "properties": { "shared": { "type": "string" } } } }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.AreEqual(JsonSchemaPropertyType.String, result.Index!.Properties.Single(p => p.Name == "shared").Types.Single());
	}

	[TestMethod]
	public void Build_TupleItems_TraversesEveryItem()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "properties": {
			    "pair": { "items": [ { "properties": { "left_name": {} } }, { "properties": { "right_name": {} } } ] }
			  }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.Contains(p => p.Name == "left_name", result.Index!.Properties);
		Assert.Contains(p => p.Name == "right_name", result.Index.Properties);
	}

	[TestMethod]
	public void Build_ValidationOnlyKeywords_AreNotTraversed()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  "properties": { "kept": {} },
			  "additionalProperties": { "properties": { "additional_name": {} } },
			  "patternProperties": { "^x": { "properties": { "pattern_name": {} } } },
			  "not": { "properties": { "not_name": {} } },
			  "if": { "properties": { "if_name": {} } }
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.AreSequenceEqual(["kept"], [.. result.Index!.Properties.Select(p => p.Name)]);
	}

	[TestMethod]
	public void Build_BooleanPropertySchemas_IndexTheNamesWithoutTypes()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom("""{ "properties": { "allowed": true, "forbidden": false } }""");

		Assert.IsTrue(result.Succeeded);
		Assert.IsEmpty(result.Warnings);
		Assert.HasCount(2, result.Index!.Properties);
		Assert.IsEmpty(result.Index.Properties.Single(p => p.Name == "allowed").Types);
		Assert.IsNull(result.Index.Properties.Single(p => p.Name == "forbidden").Description);
	}

	[TestMethod]
	public void Build_ValueThatIsNotASchema_ReportsWarningAndSkipsIt()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom("""{ "properties": { "valid": {}, "invalid": 5 }, "allOf": [ "text" ] }""");

		Assert.IsTrue(result.Succeeded);
		Assert.HasCount(2, result.Warnings);
		Assert.Contains("'#/properties/invalid'", result.Warnings[0]);
		Assert.Contains("'#/allOf/0'", result.Warnings[1]);
		Assert.AreSequenceEqual(["valid"], [.. result.Index!.Properties.Select(p => p.Name)]);
	}

	[TestMethod]
	public void Build_UnknownTypeName_ContributesNoType()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom("""{ "properties": { "mixed": { "type": [ "string", "bogus", 3 ] }, "unknown": { "type": "bogus" } } }""");

		Assert.IsTrue(result.Succeeded);
		Assert.AreEqual(JsonSchemaPropertyType.String, result.Index!.Properties.Single(p => p.Name == "mixed").Types.Single());
		Assert.IsEmpty(result.Index.Properties.Single(p => p.Name == "unknown").Types);
	}

	[TestMethod]
	public void Build_CommentsAndTrailingCommas_AreAccepted()
	{
		JsonSchemaVocabularyIndexResult result = BuildFrom(
			"""
			{
			  // A line comment.
			  "properties": {
			    /* A block comment. */
			    "name": { "type": "string", },
			  },
			}
			"""
		);

		Assert.IsTrue(result.Succeeded);
		Assert.Contains(p => p.Name == "name", result.Index!.Properties);
	}

	private static JsonSchemaVocabularyIndexResult BuildFrom(string schemaJson)
	{
		using var reader = new StringReader(schemaJson);

		return JsonSchemaVocabularyIndexBuilder.Build(reader);
	}

	private const string DefsAndRefsFixture = """
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

	private const string DefsOnlyFixture = """
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

	private const string DuplicateKeywordsFixture = """
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

	private const string DescriptionsFixture = """
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

	private const string RecursiveFixture = """
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

	private const string ItemsFixture = """
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

	private const string CombinatorsFixture = """
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

	private const string Draft04Fixture = """
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

	private const string Draft2020Fixture = """
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

	private const string MalformedDefinitionFixture = """
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

	private const string DuplicatePropertyFixture = """
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

	private const string NestedPathFixture = """
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

	/// <summary>A reader whose read fails, to cover the failure the builder reports when the schema text cannot be read.</summary>
	private sealed class ThrowingReader : TextReader
	{
		public override string ReadToEnd() => throw new InvalidOperationException("The reader failed.");
	}
}
