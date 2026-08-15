namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers <see cref="WorkspaceEditConversion"/> directly: the protocol-level precedence and fail-closed rules
/// that the shared editor model depends on.
/// </summary>
[TestClass]
public sealed class WorkspaceEditConversionTests
{
	[TestMethod]
	public void Parse_NullResponse_ReturnsNull()
		=> Assert.IsNull(WorkspaceEditConversion.Parse(null));

	[TestMethod]
	public void Parse_ResponseWithoutChangesOrDocumentChanges_ReturnsNull()
	{
		var response = new WorkspaceEditResponse(changes: null, documentChanges: null);

		Assert.IsNull(WorkspaceEditConversion.Parse(response));
	}

	[TestMethod]
	public void Parse_PrefersDocumentChangesOverChanges()
	{
		(string uri, string filePath) = CreateTarget("precedence.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(0, 0, 0, 0, "from-changes")]
			},
			documentChanges: [CreateDocumentChange(uri, CreateEdit(0, 0, 0, 0, "from-document-changes"))]);

		TextWorkspaceEdit? workspaceEdit = WorkspaceEditConversion.Parse(response);

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("from-document-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_EmptyDocumentChangesList_FallsBackToPopulatedChanges()
	{
		(string uri, string filePath) = CreateTarget("fallback-empty.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(0, 0, 0, 0, "from-changes")]
			},
			documentChanges: []);

		TextWorkspaceEdit? workspaceEdit = WorkspaceEditConversion.Parse(response);

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("from-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_DocumentChangesWithOnlySkippedEdits_FallsBackToPopulatedChanges()
	{
		(string uri, string filePath) = CreateTarget("fallback-skipped.lua");

		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				[uri] = [CreateEdit(0, 0, 0, 0, "from-changes")]
			},
			documentChanges: [CreateDocumentChange(uri, CreateEdit(-1, 0, 0, 0, "malformed-range"))]);

		TextWorkspaceEdit? workspaceEdit = WorkspaceEditConversion.Parse(response);

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("from-changes", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_UnresolvableDocumentChangeUri_FailsClosed()
	{
		var response = new WorkspaceEditResponse(
			changes: null,
			documentChanges: [CreateDocumentChange("https://example.com/remote.lua", CreateEdit(0, 0, 0, 0, "remote"))]);

		Assert.IsNull(WorkspaceEditConversion.Parse(response));
	}

	[TestMethod]
	public void Parse_UnresolvableChangeMapUri_FailsClosed()
	{
		var response = new WorkspaceEditResponse(
			changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
			{
				["untitled:Untitled-1"] = [CreateEdit(0, 0, 0, 0, "untitled")]
			},
			documentChanges: null);

		Assert.IsNull(WorkspaceEditConversion.Parse(response));
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
			documentChanges:
			[
				new WorkspaceDocumentChangePayload(
					textDocument: null, edits: null, kind: kind, uri: uri, oldUri: null, newUri: null)
			]);

		Assert.IsNull(WorkspaceEditConversion.Parse(response));
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
					new TextEditPayload(Range: CreateRange(0, 0, 0, 0), NewText: null),
					CreateEdit(1, 0, 1, 4, "kept"))
			]);

		TextWorkspaceEdit? workspaceEdit = WorkspaceEditConversion.Parse(response);

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits[0].TextEdits.Count);

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
					new TextEditPayload(Range: CreateRange(0, 0, 0, 0), NewText: null))
			]);

		Assert.IsNull(WorkspaceEditConversion.Parse(response));
	}

	[TestMethod]
	public void Parse_RepeatedDocumentChangesForOneFile_GroupIntoOneDocumentEdit()
	{
		(string uri, string filePath) = CreateTarget("grouped.lua");

		var response = new WorkspaceEditResponse(
			changes: null,
			documentChanges:
			[
				CreateDocumentChange(uri, CreateEdit(0, 0, 0, 0, "first")),
				CreateDocumentChange(uri, CreateEdit(1, 0, 1, 0, "second"))
			]);

		TextWorkspaceEdit? workspaceEdit = WorkspaceEditConversion.Parse(response);

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(filePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual(2, workspaceEdit.DocumentEdits[0].TextEdits.Count);
	}

	private static (string Uri, string FilePath) CreateTarget(string fileName)
	{
		string filePath = Path.Combine(Path.GetTempPath(), fileName);
		string uri = LanguageServerPaths.CreateFileUri(filePath);

		LanguageServerPaths.TryGetLocalPath(uri, out string normalizedPath);

		return (uri, normalizedPath);
	}

	private static TextEditPayload CreateEdit(int startLine, int startCharacter, int endLine, int endCharacter, string newText)
		=> new(CreateRange(startLine, startCharacter, endLine, endCharacter), newText);

	private static ProtocolRangePayload CreateRange(int startLine, int startCharacter, int endLine, int endCharacter)
		=> new(new ProtocolPosition(startLine, startCharacter), new ProtocolPosition(endLine, endCharacter));

	private static WorkspaceDocumentChangePayload CreateDocumentChange(string uri, params TextEditPayload[] edits)
		=> new(
			textDocument: new OptionalVersionedTextDocumentIdentifier(uri, Version: null),
			edits: edits,
			kind: null,
			uri: null,
			oldUri: null,
			newUri: null);
}
