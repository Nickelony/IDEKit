using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers <see cref="WorkspaceEditParser"/> precedence and fail-closed rules.</summary>
[TestClass]
public sealed class WorkspaceEditParserTests
{
	[TestMethod]
	public void Parse_NullResponse_ReturnsNull() => Assert.IsNull(WorkspaceEditParser.Parse(null, TestPaths.PathComparison));

	[TestMethod]
	public void Parse_ResponseWithoutChangesOrDocumentChanges_ReturnsNull()
	{
		var response = new WorkspaceEditResponse(changes: null, documentChanges: null);
		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison));
	}

	[TestMethod]
	public void Parse_PrefersDocumentChangesOverChanges()
	{
		(string uri, string filePath) = CreateTarget("precedence.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "from-changes")],
			},

			documentChanges:
			[
				CreateDocumentChange(uri, CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "from-document-changes")),
			]
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.AreEqual("from-document-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_EmptyDocumentChangesList_FallsBackToPopulatedChanges()
	{
		(string uri, string filePath) = CreateTarget("fallback-empty.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "from-changes")],
			},

			documentChanges: []
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.AreEqual("from-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_DocumentChangesWithOnlySkippedEdits_FallsBackToPopulatedChanges()
	{
		(string uri, string filePath) = CreateTarget("fallback-skipped.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "from-changes")],
			},

			documentChanges:
			[
				CreateDocumentChange(uri, CreateEdit(startLine: -1, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "malformed-range")),
			]
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.AreEqual("from-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_DocumentChangeWithNullEditList_FallsBackToPopulatedChanges()
	{
		(string changesUri, string changesPath) = CreateTarget("fallback-changes.lua");
		(string documentUri, _) = CreateTarget("fallback-null-edits.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[changesUri] = [CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5, newText: "renamed")],
			},

			documentChanges:
			[
				new WorkspaceDocumentChangePayload(
					textDocument: new OptionalVersionedTextDocumentIdentifier(documentUri, Version: null),
					edits: null,
					kind: null,
					uri: null,
					oldUri: null,
					newUri: null
				),
			]
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(changesPath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_UnresolvableDocumentChangeUri_FailsClosed()
	{
		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				CreateDocumentChange(
					"https://example.com/remote.lua",
					CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "remote")
				),
			]
		);

		using var logger = new RecordingLogger(LogLevel.Warning);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison, logger));
		Assert.HasCount(1, logger.Logs);
		Assert.Contains("could not be resolved to a local file path", logger.Logs[0]);
	}

	[TestMethod]
	public void Parse_UnresolvableChangeMapUri_FailsClosed()
	{
		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				["untitled:Untitled-1"] = [CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "untitled")],
			},

			documentChanges: null
		);

		using var logger = new RecordingLogger(LogLevel.Warning);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison, logger));
		Assert.HasCount(1, logger.Logs);
		Assert.Contains("change-map URI could not be resolved", logger.Logs[0]);
	}

	[TestMethod]
	public void Parse_ChangeMapEntryWithNullEditList_IsTreatedAsAbsent()
	{
		(string uri, _) = CreateTarget("null-entry.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?> { [uri] = null },
			documentChanges: null
		);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison));
	}

	[TestMethod]
	[DataRow(false, 2)]
	[DataRow(true, 1)]
	public void Parse_ChangeMapKeysThatDifferOnlyByCase_FollowThePathComparison(bool ignoreCase, int expectedDocumentCount)
	{
		string firstPath = Path.Combine(Path.GetTempPath(), "Scripts", "case.lua");
		string secondPath = Path.Combine(Path.GetTempPath(), "Scripts", "CASE.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[DocumentUriConversion.CreateFileUri(firstPath)] =
				[
					CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 1, newText: "first"),
				],

				[DocumentUriConversion.CreateFileUri(secondPath)] =
				[
					CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 1, newText: "second"),
				],
			},

			documentChanges: null
		);

		LocalPathComparisonPolicy pathComparison = ignoreCase ? LocalPathComparisonPolicy.CaseInsensitive : LocalPathComparisonPolicy.CaseSensitive;

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, pathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(expectedDocumentCount, workspaceEdit.DocumentEdits);
	}

	[TestMethod]
	[DataRow("create")]
	[DataRow("rename")]
	[DataRow("delete")]
	public void Parse_UnsupportedResourceOperation_FailsClosed(string kind)
	{
		(string uri, _) = CreateTarget("resource.lua");

		var response = new WorkspaceEditResponse(
			changes: null,
			documentChanges: [new WorkspaceDocumentChangePayload(textDocument: null, edits: null, kind, uri, oldUri: null, newUri: null)]
		);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison));
	}

	[TestMethod]
	public void Parse_ResourceOperationAfterValidEdit_FailsTheWholeEdit()
	{
		(string firstUri, _) = CreateTarget("first.lua");
		(string secondUri, _) = CreateTarget("second.lua");

		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				CreateDocumentChange(firstUri, CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5, newText: "local")),
				new WorkspaceDocumentChangePayload(textDocument: null, edits: null, kind: "rename", uri: null, oldUri: firstUri, newUri: secondUri),
			]
		);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison));
	}

	[TestMethod]
	public void Parse_UnsupportedResourceOperation_LogsAWarningThatNamesBothUris()
	{
		(string firstUri, _) = CreateTarget("first.lua");
		(string secondUri, _) = CreateTarget("second.lua");

		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				new WorkspaceDocumentChangePayload(textDocument: null, edits: null, kind: "rename", uri: null, oldUri: firstUri, newUri: secondUri),
			]
		);

		using var logger = new RecordingLogger(LogLevel.Warning);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison, logger));
		Assert.HasCount(1, logger.Logs);
		Assert.Contains("unsupported resource operation", logger.Logs[0]);
		Assert.Contains("workspace edit", logger.Logs[0]);
		Assert.Contains("first.lua", logger.Logs[0]);
		Assert.Contains("second.lua", logger.Logs[0]);
	}

	[TestMethod]
	public void Parse_SkipsMalformedEditsButKeepsValidOnes()
	{
		(string uri, string filePath) = CreateTarget("mixed-edits.lua");

		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				CreateDocumentChange(
					uri,
					new TextEditPayload(Range: null, NewText: "missing-range"),
					new TextEditPayload(Range: CreateRange(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0), NewText: null),
					CreateEdit(startLine: 1, startCharacter: 0, endLine: 1, endCharacter: 4, newText: "kept")
				),
			]
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.HasCount(1, workspaceEdit.DocumentEdits[0].TextEdits);

		TextEdit textEdit = workspaceEdit.DocumentEdits[0].TextEdits[0];

		Assert.AreEqual("kept", textEdit.NewText);
		Assert.AreEqual(new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 4)), textEdit.Range);
	}

	[TestMethod]
	public void Parse_AllMalformedEdits_ReturnsNull()
	{
		(string uri, _) = CreateTarget("all-malformed.lua");

		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				CreateDocumentChange(
					uri,
					new TextEditPayload(Range: null, NewText: "missing-range"),
					new TextEditPayload(Range: CreateRange(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0), NewText: null)
				),
			]
		);

		Assert.IsNull(WorkspaceEditParser.Parse(response, TestPaths.PathComparison));
	}

	[TestMethod]
	public void Parse_RepeatedDocumentChangesForOneFile_GroupIntoOneDocumentEdit()
	{
		(string uri, string filePath) = CreateTarget("grouped.lua");

		var response = new WorkspaceEditResponse(
			changes: null,

			documentChanges:
			[
				CreateDocumentChange(uri, CreateEdit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 0, newText: "first")),
				CreateDocumentChange(uri, CreateEdit(startLine: 1, startCharacter: 0, endLine: 1, endCharacter: 0, newText: "second")),
			]
		);

		WorkspaceEdit? workspaceEdit = WorkspaceEditParser.Parse(response, TestPaths.PathComparison);

		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.HasCount(2, workspaceEdit.DocumentEdits[0].TextEdits);
	}

	private static (string Uri, string FilePath) CreateTarget(string fileName)
	{
		string filePath = Path.Combine(Path.GetTempPath(), fileName);
		string uri = DocumentUriConversion.CreateFileUri(filePath);

		DocumentUriConversion.TryGetLocalPath(uri, out string normalizedPath);

		return (uri, normalizedPath);
	}

	private static TextEditPayload CreateEdit(int startLine, int startCharacter, int endLine, int endCharacter, string newText) =>
		new(CreateRange(startLine, startCharacter, endLine, endCharacter), newText);

	private static RangePayload CreateRange(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new(new PositionPayload(startLine, startCharacter), new PositionPayload(endLine, endCharacter));

	[TestMethod]
	public void ParseTextEdits_NullEdits_YieldsNoEdits() => Assert.IsEmpty(WorkspaceEditParser.ParseTextEdits(null));

	[TestMethod]
	public void ParseTextEdits_ParsesFormattingTextEdits()
	{
		IReadOnlyList<TextEdit> textEdits = WorkspaceEditParser.ParseTextEdits(
			PayloadTestHelpers.DeserializeTextEdits(
				new object[]
				{
					new
					{
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
						newText = "local value = 1\r\n",
					},

					new { range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 4 } }, newText = "    " },
				}
			)
		);

		Assert.HasCount(2, textEdits);
		Assert.AreEqual("local value = 1\r\n", textEdits[0].NewText);
		Assert.AreEqual(new TextPosition(0, 0), textEdits[0].Range.Start);
		Assert.AreEqual(new TextPosition(0, 0), textEdits[0].Range.End);
		Assert.AreEqual(new TextPosition(1, 0), textEdits[1].Range.Start);
		Assert.AreEqual(new TextPosition(1, 4), textEdits[1].Range.End);
	}

	private static WorkspaceDocumentChangePayload CreateDocumentChange(string uri, params TextEditPayload[] edits)
	{
		return new(
			textDocument: new OptionalVersionedTextDocumentIdentifier(uri, Version: null),
			edits,
			kind: null,
			uri: null,
			oldUri: null,
			newUri: null
		);
	}
}
