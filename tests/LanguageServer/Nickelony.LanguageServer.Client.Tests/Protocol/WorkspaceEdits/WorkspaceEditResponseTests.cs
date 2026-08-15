using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class WorkspaceEditResponseTests
{
	[TestMethod]
	public void Constructor_DefensivelyClonesNestedEditCollections()
	{
		IReadOnlyList<TextEditPayload> edits =
		[
			new TextEditPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(0, 0),
					new ProtocolPosition(0, 1)),
				"x")
		];

		var changes = new Dictionary<string, IReadOnlyList<TextEditPayload>?>
		{
			[new Uri(@"C:\Workspace\Scripts\first.ext").AbsoluteUri] = edits
		};

		WorkspaceDocumentChangePayload[] documentChanges =
		[
			new WorkspaceDocumentChangePayload(
				new OptionalVersionedTextDocumentIdentifier(new Uri(@"C:\Workspace\Scripts\first.ext").AbsoluteUri, Version: null),
				edits,
				kind: null,
				uri: null,
				oldUri: null,
				newUri: null)
		];

		var response = new WorkspaceEditResponse(changes, documentChanges);

		changes.Clear();

		documentChanges[0] = new WorkspaceDocumentChangePayload(
			new OptionalVersionedTextDocumentIdentifier(new Uri(@"C:\Workspace\Scripts\second.ext").AbsoluteUri, Version: null),
			edits,
			kind: "rename",
			uri: null,
			oldUri: new Uri(@"C:\Workspace\Scripts\first.ext").AbsoluteUri,
			newUri: new Uri(@"C:\Workspace\Scripts\second.ext").AbsoluteUri);

		Assert.IsNotNull(response.Changes);
		Assert.AreEqual(1, response.Changes.Count);
		Assert.IsNotNull(response.DocumentChanges);
		Assert.AreEqual(1, response.DocumentChanges.Count);
		Assert.AreEqual(new Uri(@"C:\Workspace\Scripts\first.ext").AbsoluteUri, response.DocumentChanges[0].TextDocument?.Uri);
		Assert.IsFalse(response.DocumentChanges[0].IsResourceOperation);
	}

	[TestMethod]
	public void Deserialize_PreservesResourceOperationMetadataInDocumentChanges()
	{
		string firstPath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");
		string secondPath = Path.GetFullPath(@"C:\Workspace\Scripts\second.ext");

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

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.AreEqual(2, response.Value.DocumentChanges.Count);
		Assert.AreEqual(firstPath, Path.GetFullPath(new Uri(response.Value.DocumentChanges[0].TextDocument?.Uri ?? string.Empty).LocalPath));
		Assert.AreEqual("rename", response.Value.DocumentChanges[1].Kind);
		Assert.AreEqual(new Uri(firstPath).AbsoluteUri, response.Value.DocumentChanges[1].OldUri);
		Assert.AreEqual(new Uri(secondPath).AbsoluteUri, response.Value.DocumentChanges[1].NewUri);
		Assert.IsTrue(response.Value.DocumentChanges[1].IsResourceOperation);
	}

	[TestMethod]
	public void Deserialize_PreservesTheOptionalDocumentVersionInDocumentChanges()
	{
		string filePath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					textDocument = new { uri = new Uri(filePath).AbsoluteUri, version = 7 },
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
					textDocument = new { uri = new Uri(filePath).AbsoluteUri, version = (int?)null },
					edits = Array.Empty<object>()
				}
			}
		});

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);
		Assert.AreEqual(2, response.Value.DocumentChanges.Count);

		// A server-sent version round-trips so a consumer can reject edits computed against a stale document.
		Assert.AreEqual(7, response.Value.DocumentChanges[0].TextDocument?.Version);

		// A null version explicitly means "apply to the current version".
		Assert.IsNotNull(response.Value.DocumentChanges[1].TextDocument);
		Assert.IsNull(response.Value.DocumentChanges[1].TextDocument!.Value.Version);
	}

	[TestMethod]
	public void Deserialize_ClonesNestedCollectionsIntoDetachedSnapshots()
	{
		string filePath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			changes = new Dictionary<string, object[]>
			{
				[new Uri(filePath).AbsoluteUri] =
				[
					new
					{
						range = new
						{
							start = new { line = 0, character = 0 },
							end = new { line = 0, character = 1 }
						},
						newText = "x"
					}
				]
			}
		});

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.Changes);

		IReadOnlyList<TextEditPayload>? edits = response.Value.Changes[new Uri(filePath).AbsoluteUri];

		Assert.IsNotNull(edits);
		Assert.AreEqual(1, edits.Count);
		Assert.AreEqual("x", edits[0].NewText);
	}

	[TestMethod]
	public void Deserialize_PayloadWithoutCollections_LeavesTheCollectionsNull()
	{
		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new { });

		Assert.IsNotNull(response);
		Assert.IsNull(response.Value.Changes);
		Assert.IsNull(response.Value.DocumentChanges);
		Assert.IsNull(response.Value.ChangeAnnotations);
	}

	[TestMethod]
	public void Deserialize_ChangeAnnotations_ParsesTheMap()
	{
		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			changeAnnotations = new Dictionary<string, object>
			{
				["rename-1"] = new
				{
					label = "Rename symbol",
					needsConfirmation = true,
					description = "Applies to every reference."
				}
			}
		});

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.ChangeAnnotations);
		Assert.AreEqual(1, response.Value.ChangeAnnotations.Count);

		WorkspaceEditChangeAnnotationPayload annotation = response.Value.ChangeAnnotations["rename-1"];

		Assert.AreEqual("Rename symbol", annotation.Label);
		Assert.IsTrue(annotation.NeedsConfirmation);
		Assert.AreEqual("Applies to every reference.", annotation.Description);
	}

	[TestMethod]
	public void Constructor_DefensivelyClonesChangeAnnotations()
	{
		var source = new Dictionary<string, WorkspaceEditChangeAnnotationPayload>
		{
			["rename-1"] = new("Rename symbol", NeedsConfirmation: true, Description: "Applies to every reference.")
		};

		var response = new WorkspaceEditResponse(null, null, source);

		source["delete-1"] = new("Delete file", NeedsConfirmation: null, Description: null);
		source["rename-1"] = new("Replaced", NeedsConfirmation: null, Description: null);

		Assert.IsNotNull(response.ChangeAnnotations);
		Assert.AreEqual(1, response.ChangeAnnotations.Count);
		Assert.AreEqual("Rename symbol", response.ChangeAnnotations["rename-1"].Label);
	}

	[TestMethod]
	public void Deserialize_PreservesAnnotatedEditAnnotationId()
	{
		string filePath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					textDocument = new { uri = new Uri(filePath).AbsoluteUri },
					edits = new object[]
					{
						new
						{
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 5 }
							},
							newText = "local",
							annotationId = "rename-1"
						}
					}
				}
			}
		});

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);

		TextEditPayload edit = response.Value.DocumentChanges![0].Edits![0];

		Assert.AreEqual("rename-1", edit.AnnotationId);

		string serializedEdit = JsonSerializer.Serialize(edit);

		StringAssert.Contains(serializedEdit, "\"annotationId\":\"rename-1\"");
	}

	[TestMethod]
	public void Deserialize_PreservesResourceOperationOptions()
	{
		string firstPath = Path.GetFullPath(@"C:\Workspace\Scripts\first.ext");
		string secondPath = Path.GetFullPath(@"C:\Workspace\Scripts\second.ext");

		WorkspaceEditResponse? response = DeserializeWorkspaceEditResponse(new
		{
			documentChanges = new object[]
			{
				new
				{
					kind = "rename",
					oldUri = new Uri(firstPath).AbsoluteUri,
					newUri = new Uri(secondPath).AbsoluteUri,
					options = new { overwrite = true, ignoreIfExists = false }
				},
				new
				{
					kind = "delete",
					uri = new Uri(firstPath).AbsoluteUri,
					options = new { recursive = true, ignoreIfNotExists = true }
				}
			}
		});

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Value.DocumentChanges);

		WorkspaceResourceOperationOptionsPayload renameOptions = response.Value.DocumentChanges![0].Options!.Value;

		Assert.IsTrue(renameOptions.Overwrite);
		Assert.IsFalse(renameOptions.IgnoreIfExists);
		Assert.IsNull(renameOptions.Recursive);

		WorkspaceResourceOperationOptionsPayload deleteOptions = response.Value.DocumentChanges[1].Options!.Value;

		Assert.IsTrue(deleteOptions.Recursive);
		Assert.IsTrue(deleteOptions.IgnoreIfNotExists);

		// The options must round-trip through serialization instead of being dropped.
		string serialized = JsonSerializer.Serialize(response.Value.DocumentChanges[1]);

		StringAssert.Contains(serialized, "\"recursive\":true");
		StringAssert.Contains(serialized, "\"ignoreIfNotExists\":true");
	}

	[TestMethod]
	public void Serialize_ResourceOperationChange_OmitsDocumentMembersAndComputedState()
	{
		string serialized = JsonSerializer.Serialize(new WorkspaceDocumentChangePayload(null, null, "create", "file:///C:/Workspace/created.ext", null, null));

		Assert.IsFalse(serialized.Contains("IsResourceOperation", StringComparison.Ordinal), serialized);
		Assert.IsFalse(serialized.Contains("isResourceOperation", StringComparison.Ordinal), serialized);

		// The protocol does not allow null values for the document members, so a resource operation
		// must omit them entirely instead of writing JSON null.
		Assert.IsFalse(serialized.Contains("\"textDocument\"", StringComparison.Ordinal), serialized);
		Assert.IsFalse(serialized.Contains("\"edits\"", StringComparison.Ordinal), serialized);
	}

	private static WorkspaceEditResponse? DeserializeWorkspaceEditResponse(object payload)
		=> JsonSerializer.Deserialize<WorkspaceEditResponse>(JsonSerializer.SerializeToElement(payload));
}
