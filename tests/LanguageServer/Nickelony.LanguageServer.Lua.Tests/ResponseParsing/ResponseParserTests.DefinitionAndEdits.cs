using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.LanguageServer.Testing;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseDefinitionLocation_UsesFirstEntryFromMultiLocationResponse()
	{
		string firstPath = TestPaths.Script("first.lua");
		string secondPath = TestPaths.Script("second.lua");

		TextDefinitionLocation? location = ResponseParser.ParseDefinitionLocation(
			DeserializeDefinitionResponse(new object[]
			{
				new
				{
					uri = new Uri(firstPath).AbsoluteUri,
					range = new
					{
						start = new { line = 2, character = 4 },
						end = new { line = 2, character = 10 }
					}
				},
				new
				{
					uri = new Uri(secondPath).AbsoluteUri,
					range = new
					{
						start = new { line = 8, character = 1 },
						end = new { line = 8, character = 5 }
					}
				}
			}));

		Assert.IsNotNull(location);
		Assert.AreEqual(firstPath, location.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 10)), location.TargetRange);
		Assert.IsNull(location.SelectionRange);
	}

	[TestMethod]
	public void ParseDefinitionLocation_UsesTargetSelectionRangeFromLocationLink()
	{
		string targetPath = TestPaths.Script("linked.lua");

		TextDefinitionLocation? location = ResponseParser.ParseDefinitionLocation(
			DeserializeDefinitionResponse(new
			{
				targetUri = new Uri(targetPath).AbsoluteUri,
				targetSelectionRange = new
				{
					start = new { line = 4, character = 2 },
					end = new { line = 4, character = 9 }
				}
			}));

		Assert.IsNotNull(location);
		Assert.AreEqual(targetPath, location.DocumentId);

		// A location link without a distinct target range uses the selection range as the target.
		var expectedRange = new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 9));

		Assert.AreEqual(expectedRange, location.TargetRange);
		Assert.AreEqual(expectedRange, location.SelectionRange);
	}

	[TestMethod]
	public void ParseDefinitionLocation_KeepsDistinctTargetAndSelectionRanges()
	{
		string targetPath = TestPaths.Script("linked.lua");

		TextDefinitionLocation? location = ResponseParser.ParseDefinitionLocation(
			DeserializeDefinitionResponse(new
			{
				targetUri = new Uri(targetPath).AbsoluteUri,
				targetRange = new
				{
					start = new { line = 4, character = 0 },
					end = new { line = 8, character = 3 }
				},
				targetSelectionRange = new
				{
					start = new { line = 4, character = 2 },
					end = new { line = 4, character = 9 }
				}
			}));

		Assert.IsNotNull(location);
		Assert.AreEqual(targetPath, location.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 0), new TextPosition(8, 3)), location.TargetRange);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 9)), location.SelectionRange);
	}

	[TestMethod]
	public void ParseDefinitionLocation_ReturnsNullForNegativeTargetPosition()
	{
		string targetPath = TestPaths.Script("linked.lua");

		TextDefinitionLocation? location = ResponseParser.ParseDefinitionLocation(
			DeserializeDefinitionResponse(new
			{
				targetUri = new Uri(targetPath).AbsoluteUri,
				targetSelectionRange = new
				{
					start = new { line = -1, character = 2 },
					end = new { line = 4, character = 9 }
				}
			}));

		Assert.IsNull(location);
	}

	[TestMethod]
	public void ParseReferenceLocations_ParsesFileReferenceRanges()
	{
		string targetPath = TestPaths.Script("references.lua");

		IReadOnlyList<TextReferenceLocation> locations = ResponseParser.ParseReferenceLocations(
			DeserializeReferenceLocations(new object[]
			{
				new
				{
					uri = new Uri(targetPath).AbsoluteUri,
					range = new
					{
						start = new { line = 2, character = 4 },
						end = new { line = 2, character = 9 }
					}
				},
				new
				{
					uri = "https://example.com/not-a-file.lua",
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 1 }
					}
				}
			}));

		Assert.AreEqual(1, locations.Count);
		Assert.AreEqual(targetPath, locations[0].FilePath);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 9)), locations[0].Range);
	}

	[TestMethod]
	public void ParseReferenceLocations_IgnoresNegativeProtocolRanges()
	{
		string targetPath = TestPaths.Script("references.lua");

		IReadOnlyList<TextReferenceLocation> locations = ResponseParser.ParseReferenceLocations(
			DeserializeReferenceLocations(new object[]
			{
				new
				{
					uri = new Uri(targetPath).AbsoluteUri,
					range = new
					{
						start = new { line = -1, character = 4 },
						end = new { line = 2, character = 9 }
					}
				}
			}));

		Assert.AreEqual(0, locations.Count);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_PrefersDocumentChangesOverChangeMap()
	{
		string firstPath = TestPaths.Script("first.lua");
		string secondPath = TestPaths.Script("second.lua");

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(
			DeserializeWorkspaceEditResponse(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(firstPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "local"
						}
					]
				},
				documentChanges = new object[]
				{
					new
					{
						textDocument = new { uri = new Uri(secondPath).AbsoluteUri },
						edits = new object[]
						{
							new
							{
								range = new
								{
									start = new { line = 3, character = 1 },
									end = new { line = 3, character = 4 }
								},
								newText = "name"
							}
						}
					}
				}
			}));

		// LSP defines both representations as alternatives; when a server populates both, the structured
		// documentChanges list wins so every edit cannot be applied twice.
		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(secondPath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("name", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_FallsBackToChangesWhenDocumentChangesProduceNoEdit()
	{
		string changeMapPath = TestPaths.Script("changes-only.lua");

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(
			DeserializeWorkspaceEditResponse(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(changeMapPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "renamed"
						}
					]
				},
				documentChanges = Array.Empty<object>()
			}));

		// An empty documentChanges list must not discard a populated changes map; the rename would
		// otherwise be lost silently.
		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(changeMapPath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_FallsBackToChangesWhenDocumentChangesContainOnlySkippedEdits()
	{
		string changeMapPath = TestPaths.Script("changes-only.lua");
		string skippedPath = TestPaths.Script("skipped.lua");

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(
			DeserializeWorkspaceEditResponse(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(changeMapPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "renamed"
						}
					]
				},
				documentChanges = new object[]
				{
					new
					{
						textDocument = new { uri = new Uri(skippedPath).AbsoluteUri },
						edits = new object[]
						{
							new { range = (object?)null, newText = "no range" },
							new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } }, newText = (string?)null }
						}
					}
				}
			}));

		// documentChanges entries whose edits are all skipped must not discard a populated changes
		// map; only a yield of usable edits counts as a usable documentChanges representation.
		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(changeMapPath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_FallsBackToChangesWhenDocumentChangesCarryNullEditLists()
	{
		string changeMapPath = TestPaths.Script("changes-only.lua");
		string nullEditsPath = TestPaths.Script("null-edits.lua");

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(
			DeserializeWorkspaceEditResponse(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(changeMapPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "renamed"
						}
					]
				},
				documentChanges = new object[]
				{
					new
					{
						textDocument = new { uri = new Uri(nullEditsPath).AbsoluteUri },
						edits = (object?)null
					}
				}
			}));

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(changeMapPath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_ReturnsNullWhenDocumentChangeUriCannotBeResolved()
	{
		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					textDocument = new { uri = "https://example.com/not-a-file.lua" },
					edits = new object[]
					{
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "renamed"
						}
					}
				}
			}
		});

		using var logScope = new TestLoggerScope(LogLevel.Warning);

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(response, logScope);

		// An unresolvable target URI fails the whole rename closed so no partial rename is applied.
		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(1, logScope.Logs.Count);
		StringAssert.Contains(logScope.Logs[0], "could not be resolved to a local file path");
	}

	[TestMethod]
	public void ParseWorkspaceEdit_ReturnsNullWhenChangeMapUriCannotBeResolved()
	{
		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			changes = new Dictionary<string, object[]>
			{
				["https://example.com/not-a-file.lua"] =
				[
					new
					{
						range = new
						{
							start = new { line = 0, character = 0 },
							end = new { line = 0, character = 5 }
						},
						newText = "renamed"
					}
				]
			}
		});

		using var logScope = new TestLoggerScope(LogLevel.Warning);

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(response, logScope);

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(1, logScope.Logs.Count);
		StringAssert.Contains(logScope.Logs[0], "change-map URI could not be resolved");
	}

	[TestMethod]
	public void ParseWorkspaceEdit_UsesLocalPathCaseSensitivity()
	{
		string firstPath = Path.Combine(Path.GetTempPath(), "Scripts", "case.lua");
		string secondPath = Path.Combine(Path.GetTempPath(), "Scripts", "CASE.lua");

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(
			DeserializeWorkspaceEditResponse(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(firstPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 1 }
							},
							newText = "first"
						}
					],
					[new Uri(secondPath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 1 }
							},
							newText = "second"
						}
					]
				}
			}));

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(LanguageServerPaths.UsesCaseSensitiveLocalPaths ? 2 : 1, workspaceEdit.DocumentEdits.Count);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_ReturnsNullWhenDocumentChangesContainUnsupportedResourceOperation()
	{
		string firstPath = TestPaths.Script("first.lua");
		string secondPath = TestPaths.Script("second.lua");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					textDocument = new { uri = new Uri(firstPath).AbsoluteUri },
					edits = new object[]
					{
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "local"
						}
					}
				},
				new
				{
					kind = "rename",
					oldUri = new Uri(firstPath).AbsoluteUri,
					newUri = new Uri(secondPath).AbsoluteUri
				}
			}
		});

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(response);

		Assert.IsNull(workspaceEdit);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_LogsWarningWhenDocumentChangesContainUnsupportedResourceOperation()
	{
		string firstPath = TestPaths.Script("first.lua");
		string secondPath = TestPaths.Script("second.lua");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					kind = "rename",
					oldUri = new Uri(firstPath).AbsoluteUri,
					newUri = new Uri(secondPath).AbsoluteUri
				}
			}
		});

		using var logScope = new TestLoggerScope(LogLevel.Warning);

		TextWorkspaceEdit? workspaceEdit = ResponseParser.ParseWorkspaceEdit(response, logScope);

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(1, logScope.Logs.Count);
		StringAssert.Contains(logScope.Logs[0], "unsupported resource operation");
		StringAssert.Contains(logScope.Logs[0], "workspace edit");
		StringAssert.Contains(logScope.Logs[0], "first.lua");
		StringAssert.Contains(logScope.Logs[0], "second.lua");
	}

	[TestMethod]
	public void ParseDocumentFormattingEdits_NullResponse_YieldsNoEdits()
	{
		Assert.AreEqual(0, ResponseParser.ParseDocumentFormattingEdits(null).Count);
	}

	[TestMethod]
	public void ParseDocumentFormattingEdits_ParsesFormattingTextEdits()
	{
		IReadOnlyList<TextEdit> textEdits = ResponseParser.ParseDocumentFormattingEdits(
			DeserializeTextEdits(new object[]
			{
				new
				{
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 0 }
					},
					newText = "local value = 1\r\n"
				},
				new
				{
					range = new
					{
						start = new { line = 1, character = 0 },
						end = new { line = 1, character = 4 }
					},
					newText = "    "
				}
			}));

		Assert.AreEqual(2, textEdits.Count);
		Assert.AreEqual("local value = 1\r\n", textEdits[0].NewText);
		Assert.AreEqual(new TextPosition(0, 0), textEdits[0].Range.Start);
		Assert.AreEqual(new TextPosition(0, 0), textEdits[0].Range.End);
		Assert.AreEqual(new TextPosition(1, 0), textEdits[1].Range.Start);
		Assert.AreEqual(new TextPosition(1, 4), textEdits[1].Range.End);
	}

	[TestMethod]
	public void ParseWorkspaceEdit_ChangeMapWithNullEntry_IsTreatedAsAbsent()
	{
		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			changes = new Dictionary<string, object?>
			{
				[new Uri(TestPaths.Script("first.lua")).AbsoluteUri] = null
			}
		});

		// A null edit list adds no entries; the response then yields no usable edit at all.
		Assert.IsNull(ResponseParser.ParseWorkspaceEdit(response));
	}
}
