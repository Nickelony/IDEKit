using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SignatureHelpResponseParserTests
{
	[TestMethod]
	public void Parse_UsesParameterLabelOffsetsAndActiveParameter()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 1,

					signatures = new[]
					{
						new
						{
							label = "spawn(room, objectName)",
							documentation = new { kind = "markdown", value = "Spawns an object." },

							parameters = new object[]
							{
								new { label = new[] { 6, 10 }, documentation = "Room id." },
								new { label = new[] { 12, 22 }, documentation = "Object name." },
							},
						},
					},
				}
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.HasCount(1, signatureInfo.Signatures);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room, objectName)", signatureInfo.ActiveSignature.Label);
		Assert.AreEqual("Spawns an object.", signatureInfo.ActiveSignature.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, signatureInfo.ActiveSignature.Documentation?.Kind);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.HasCount(2, signatureInfo.ActiveSignature.Parameters);
		Assert.AreEqual("objectName", signatureInfo.ActiveSignature.Parameters[1].Label);
		Assert.AreEqual("Object name.", signatureInfo.ActiveSignature.Parameters[1].Documentation?.Value);

		// The offset form carries the exact range of each parameter so a host can highlight the active one
		// without searching for a label that may repeat.
		Assert.AreEqual(TextRange.FromBounds(6, 10), signatureInfo.ActiveSignature.Parameters[0].Range);
		Assert.AreEqual(TextRange.FromBounds(12, 22), signatureInfo.ActiveSignature.Parameters[1].Range);
	}

	[TestMethod]
	public void Parse_RepeatingParameterLabel_CarriesTheDistinctRangeOfEachParameter()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 1,

					signatures = new[]
					{
						new { label = "f(a, a)", parameters = new object[] { new { label = new[] { 2, 3 } }, new { label = new[] { 5, 6 } } } },
					},
				}
			)
		);

		// A repeated label cannot be located by search; the range disambiguates the two positions.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual("a", signatureInfo.ActiveSignature.Parameters[0].Label);
		Assert.AreEqual("a", signatureInfo.ActiveSignature.Parameters[1].Label);
		Assert.AreEqual(TextRange.FromBounds(2, 3), signatureInfo.ActiveSignature.Parameters[0].Range);
		Assert.AreEqual(TextRange.FromBounds(5, 6), signatureInfo.ActiveSignature.Parameters[1].Range);
	}

	[TestMethod]
	public void Parse_MalformedMiddleSignature_DoesNotShiftTheActiveSignature()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 2,
					signatures = new object[] { new { label = "first()" }, 42, new { label = "third()" }, new { label = "fourth()" } },
				}
			)
		);

		// The malformed element keeps its array position as an unusable placeholder, so the active
		// index still selects the signature the server meant instead of the one after it.
		Assert.IsNotNull(signatureInfo);
		Assert.HasCount(3, signatureInfo.Signatures);
		Assert.AreEqual("first()", signatureInfo.Signatures[0].Label);
		Assert.AreEqual("third()", signatureInfo.Signatures[1].Label);
		Assert.AreEqual("fourth()", signatureInfo.Signatures[2].Label);
		Assert.AreEqual(1, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("third()", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_MarkdownDocumentation_KeepsItsKindAndItsFences()
	{
		const string documentation = "Summary\n```lua\nlocal value = 1\n```";

		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new { signatures = new[] { new { label = "spawn()", documentation = new { kind = "markdown", value = documentation } } } }
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(documentation, signatureInfo.ActiveSignature.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, signatureInfo.ActiveSignature.Documentation?.Kind);
	}

	[TestMethod]
	public void Parse_BareStringDocumentation_IsTrimmedPlainText()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					signatures = new[]
					{
						new
						{
							label = "spawn(room)",
							documentation = "  Spawns an object.\r\n",
							parameters = new[] { new { label = "room", documentation = " Room id. " } },
						},
					},
				}
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual("Spawns an object.", signatureInfo.ActiveSignature.Documentation?.Value);
		Assert.AreEqual("Room id.", signatureInfo.ActiveSignature.Parameters[0].Documentation?.Value);
	}

	[TestMethod]
	public void Parse_ExplicitNullActiveParameter_PreservesTheNoActiveParameterState()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = (int?)null,

					signatures = new[]
					{
						new { label = "spawn(room, objectName)", parameters = new object[] { new { label = "room" }, new { label = "objectName" } } },
					},
				}
			)
		);

		// LSP 3.18: an explicit null means no parameter is active, unlike an omitted property.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void Parse_NegativeActiveParameter_MapsToNoActiveParameter()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = -1,

					signatures = new[]
					{
						new { label = "spawn(room, objectName)", parameters = new object[] { new { label = "room" }, new { label = "objectName" } } },
					},
				}
			)
		);

		// A negative index is never valid: it follows the pre-3.18 "no active parameter" convention,
		// so no parameter is highlighted instead of the first one.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void Parse_ExplicitNullSignatureActiveParameter_KeepsPayloadFromApplying()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 1,

					signatures = new[]
					{
						new
						{
							label = "move(x, y)",
							activeParameter = (int?)null,
							parameters = new object[] { new { label = "x" }, new { label = "y" } },
						},
					},
				}
			)
		);

		// A signature-level explicit null overrides the payload-level index, per the LSP precedence rule.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void Parse_UsesSignatureLevelActiveParameterWhenResponseOmitsIt()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					signatures = new[]
					{
						new
						{
							label = "move(x, y)",
							activeParameter = 1,
							parameters = new object[] { new { label = "x" }, new { label = "y" } },
						},
					},
				}
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("y", signatureInfo.ActiveSignature.Parameters[1].Label);
	}

	[TestMethod]
	public void Parse_OutOfRangeActiveParameter_FallsBackToFirstParameter()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 9,

					signatures = new[]
					{
						new { label = "spawn(room, objectName)", parameters = new object[] { new { label = "room" }, new { label = "objectName" } } },
					},
				}
			)
		);

		// The LSP default rule selects the first parameter when the protocol value is outside the range.
		Assert.IsNotNull(signatureInfo);
		Assert.HasCount(2, signatureInfo.ActiveSignature.Parameters);
		Assert.AreEqual(0, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("room", signatureInfo.ActiveSignature.Parameters[signatureInfo.ActiveParameterIndex!.Value].Label);
	}

	[TestMethod]
	public void Parse_PreservesAllSignaturesAndActiveSelection()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new { activeSignature = 1, signatures = new[] { new { label = "spawn(room)" }, new { label = "spawn(room, objectName)" } } }
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.HasCount(2, signatureInfo.Signatures);
		Assert.AreEqual(1, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room, objectName)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_SignatureLevelActiveParameterOverridesResponseLevel()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 0,

					signatures = new[]
					{
						new
						{
							label = "move(x, y)",
							activeParameter = 1,
							parameters = new object[] { new { label = "x" }, new { label = "y" } },
						},
					},
				}
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("y", signatureInfo.ActiveSignature.Parameters[1].Label);
	}

	[TestMethod]
	public void Parse_SkipsSignaturesWithoutLabels()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(new { activeSignature = 0, signatures = new[] { new { label = "   " }, new { label = "spawn(room)" } } })
		);

		Assert.IsNotNull(signatureInfo);
		Assert.HasCount(1, signatureInfo.Signatures);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_NegativeActiveSignature_SelectsTheFirstSignature()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new { activeSignature = -3, signatures = new[] { new { label = "spawn(room)" }, new { label = "spawn(room, objectName)" } } }
			)
		);

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_OutOfRangePositiveActiveSignature_SelectsTheFirstSignature()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new { activeSignature = 99, signatures = new[] { new { label = "spawn(room)" }, new { label = "spawn(room, objectName)" } } }
			)
		);

		// LSP: an out-of-range index defaults to the first signature, not the nearest or last one.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_ActiveSignatureAtPayloadLength_SelectsTheFirstSignature()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new { activeSignature = 2, signatures = new[] { new { label = "spawn(room)" }, new { label = "spawn(room, objectName)" } } }
			)
		);

		// The first index past the payload is out of range, unlike the last usable index.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void Parse_FractionalActiveParameter_BehavesLikeTheAbsentValue()
	{
		object signature = new { label = "move(x, y)", parameters = new object[] { new { label = "x" }, new { label = "y" } } };

		SignatureHelpInformation? absent = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(new { activeSignature = 0, signatures = new[] { signature } })
		);

		SignatureHelpInformation? fractional = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 1.5,
					signatures = new[] { signature },
				}
			)
		);

		// A fractional value cannot be an index; it falls back to the not-specified sentinel like an
		// absent value.
		Assert.IsNotNull(absent);
		Assert.IsNotNull(fractional);
		Assert.AreEqual(absent.ActiveParameterIndex, fractional.ActiveParameterIndex);
	}

	[TestMethod]
	public void Parse_NullResponse_ReturnsNull()
	{
		// A missing response has no signatures to build from.
		Assert.IsNull(SignatureHelpResponseParser.Parse(null));
	}

	[TestMethod]
	public void Parse_EmptySignatureList_ReturnsNull()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(new { signatures = Array.Empty<object>() })
		);

		// An empty list has no usable signature, so the payload is absent rather than an empty model.
		Assert.IsNull(signatureInfo);
	}

	[TestMethod]
	public void Parse_OverflowingActiveParameter_FallsBackToTheFirstParameter()
	{
		SignatureHelpInformation? signatureInfo = SignatureHelpResponseParser.Parse(
			DeserializeSignatureHelpResponse(
				new
				{
					activeSignature = 0,
					activeParameter = 2147483648L,

					signatures = new[]
					{
						new { label = "spawn(room, objectName)", parameters = new object[] { new { label = "room" }, new { label = "objectName" } } },
					},
				}
			)
		);

		// A value outside Int32 is not a usable index: it maps to the not-specified state, which resolves to
		// the first parameter instead of the no-active-parameter state an explicit null or negative index uses.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(0, signatureInfo.ActiveParameterIndex);
	}
}
