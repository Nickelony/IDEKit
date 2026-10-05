using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ClientHooksFactoryTests
{
	private static LanguageServerClientHooks Create(LanguageServerFeatureProviderHooks hooks, ILogger? logger = null) =>
		ClientHooksFactory.Create(hooks, TestPaths.PathComparison, logger ?? NullLogger.Instance);

	[TestMethod]
	public void Create_LeavesEveryClientHookUnsetForAnUnsetHostHook()
	{
		LanguageServerClientHooks hooks = Create(new LanguageServerFeatureProviderHooks());

		Assert.IsNull(hooks.ShowMessage);
		Assert.IsNull(hooks.ShowMessageRequest);
		Assert.IsNull(hooks.Progress);
		Assert.IsNull(hooks.ApplyWorkspaceEdit);
	}

	[TestMethod]
	public void Create_SetsOnlyTheClientHooksOfTheHostHooksThatAreSet()
	{
		LanguageServerClientHooks hooks = Create(new LanguageServerFeatureProviderHooks { Progress = static _ => { } });

		Assert.IsNull(hooks.ShowMessage);
		Assert.IsNull(hooks.ShowMessageRequest);
		Assert.IsNotNull(hooks.Progress);
		Assert.IsNull(hooks.ApplyWorkspaceEdit);
	}

	[TestMethod]
	[DataRow(MessageType.Error, WindowMessageKind.Error)]
	[DataRow(MessageType.Warning, WindowMessageKind.Warning)]
	[DataRow(MessageType.Information, WindowMessageKind.Information)]
	[DataRow(MessageType.Log, WindowMessageKind.Log)]
	[DataRow((MessageType)99, WindowMessageKind.Log)]
	public void ShowMessage_HandsTheKindAndTheTextToTheHostHook(MessageType type, WindowMessageKind expectedKind)
	{
		var received = new List<WindowMessage>();
		LanguageServerClientHooks hooks = Create(new LanguageServerFeatureProviderHooks { ShowMessage = received.Add });

		hooks.ShowMessage!(new WindowMessageParams(type, "Loading projects"));

		Assert.HasCount(1, received);
		Assert.AreEqual(expectedKind, received[0].Kind);
		Assert.AreEqual("Loading projects", received[0].Text);
		Assert.IsEmpty(received[0].Actions);
	}

	[TestMethod]
	public void ShowMessage_WithoutAType_ReadsAsALog()
	{
		var received = new List<WindowMessage>();
		LanguageServerClientHooks hooks = Create(new LanguageServerFeatureProviderHooks { ShowMessage = received.Add });

		hooks.ShowMessage!(new WindowMessageParams(Type: null, "x"));

		Assert.AreEqual(WindowMessageKind.Log, received[0].Kind);
	}

	[TestMethod]
	public async Task ShowMessageRequest_HandsTheMessageWithItsActionsToTheHostAndAnswersTheChosenOne()
	{
		WindowMessage? received = null;

		LanguageServerClientHooks hooks = Create(
			new LanguageServerFeatureProviderHooks
			{
				ShowMessageRequest = (message, _) =>
				{
					received = message;
					return Task.FromResult<string?>("No");
				},
			}
		);

		MessageActionItemPayload? answer = await hooks.ShowMessageRequest!(
			new ShowMessageRequestParams(
				MessageType.Information,
				"Reload?",
				[new MessageActionItemPayload("Yes"), new MessageActionItemPayload("No")]
			),

			CancellationToken.None
		);

		Assert.AreEqual(new MessageActionItemPayload("No"), answer);
		Assert.AreEqual(WindowMessageKind.Information, received?.Kind);
		Assert.AreSequenceEqual(["Yes", "No"], received?.Actions);
	}

	[TestMethod]
	public async Task ShowMessageRequest_AnswersNullWhenTheHostChoosesNothing()
	{
		LanguageServerClientHooks hooks = Create(
			new LanguageServerFeatureProviderHooks { ShowMessageRequest = static (_, _) => Task.FromResult<string?>(null) }
		);

		MessageActionItemPayload? answer = await hooks.ShowMessageRequest!(
			new ShowMessageRequestParams(MessageType.Information, "Reload?", [new MessageActionItemPayload("Yes")]),
			CancellationToken.None
		);

		Assert.IsNull(answer);
	}

	[TestMethod]
	[DataRow("begin", WorkDoneProgressKind.Begin)]
	[DataRow("report", WorkDoneProgressKind.Report)]
	[DataRow("end", WorkDoneProgressKind.End)]
	public void Progress_HandsTheReportToTheHostHook(string kind, WorkDoneProgressKind expectedKind)
	{
		var received = new List<WorkDoneProgress>();
		LanguageServerClientHooks hooks = Create(new LanguageServerFeatureProviderHooks { Progress = received.Add });

		hooks.Progress!(new ProgressParams("load", new WorkDoneProgressPayload(kind, Title: "Loading", Message: "a.csproj", Percentage: 40)));

		Assert.HasCount(1, received);
		Assert.AreEqual(new WorkDoneProgress(Token: "load", Kind: expectedKind, Title: "Loading", Message: "a.csproj", Percentage: 40), received[0]);
	}

	[TestMethod]
	public async Task ApplyWorkspaceEdit_HandsTheEditToTheHostAsASharedModelAndAnswersItsOutcome()
	{
		string path = TestPaths.Script("a.test");
		WorkspaceEdit? received = null;

		LanguageServerClientHooks hooks = Create(
			new LanguageServerFeatureProviderHooks
			{
				ApplyWorkspaceEdit = (edit, _) =>
				{
					received = edit;
					return Task.FromResult(true);
				},
			}
		);

		ApplyWorkspaceEditResponse response = await hooks.ApplyWorkspaceEdit!(
			new ApplyWorkspaceEditParams(
				"Fix",

				new WorkspaceEditResponse(
					changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
					{
						[new Uri(path).AbsoluteUri] =
						[
							new TextEditPayload(new RangePayload(new PositionPayload(2, 1), new PositionPayload(2, 4)), "x"),
						],
					},

					documentChanges: null
				)
			),

			CancellationToken.None
		);

		Assert.IsTrue(response.Applied);
		Assert.IsNull(response.FailureReason);
		Assert.IsNotNull(received);
		Assert.AreEqual(path, received.DocumentEdits[0].DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 1), new TextPosition(2, 4)), received.DocumentEdits[0].TextEdits[0].Range);
	}

	[TestMethod]
	public async Task ApplyWorkspaceEdit_WhenTheHostDoesNotApplyTheEdit_AnswersNotApplied()
	{
		LanguageServerClientHooks hooks = Create(
			new LanguageServerFeatureProviderHooks { ApplyWorkspaceEdit = static (_, _) => Task.FromResult(false) }
		);

		ApplyWorkspaceEditResponse response = await hooks.ApplyWorkspaceEdit!(
			new ApplyWorkspaceEditParams(
				Label: null,

				new WorkspaceEditResponse(
					changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
					{
						[new Uri(TestPaths.Script("a.test")).AbsoluteUri] =
						[
							new TextEditPayload(new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)), "x"),
						],
					},

					documentChanges: null
				)
			),

			CancellationToken.None
		);

		Assert.IsFalse(response.Applied);
		Assert.IsFalse(string.IsNullOrWhiteSpace(response.FailureReason));
	}

	[TestMethod]
	public async Task ApplyWorkspaceEdit_ForAnEditTheSharedModelCannotRepresent_AnswersNotAppliedWithoutCallingTheHost()
	{
		int calls = 0;

		LanguageServerClientHooks hooks = Create(
			new LanguageServerFeatureProviderHooks
			{
				ApplyWorkspaceEdit = (_, _) =>
				{
					calls++;
					return Task.FromResult(true);
				},
			}
		);

		WorkspaceEditResponse rename = PayloadTestHelpers
			.DeserializeCodeActionResponse(
				new object[]
				{
					new
					{
						title = "Rename file",

						edit = new
						{
							documentChanges = new object[]
							{
								new
								{
									kind = "rename",
									oldUri = new Uri(TestPaths.Script("a.test")).AbsoluteUri,
									newUri = new Uri(TestPaths.Script("b.test")).AbsoluteUri,
								},
							},
						},
					},
				}
			)!
			.CodeActions[0]
			.Edit!.Value;

		ApplyWorkspaceEditResponse response = await hooks.ApplyWorkspaceEdit!(
			new ApplyWorkspaceEditParams(Label: null, rename),
			CancellationToken.None
		);

		Assert.IsFalse(response.Applied);
		Assert.AreEqual(0, calls);
	}
}
