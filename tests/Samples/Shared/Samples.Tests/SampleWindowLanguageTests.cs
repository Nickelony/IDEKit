using System.IO;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SampleWindowLanguageTests
{
	[TestMethod]
	public async Task TypingALetterInCode_OpensTheCompletionList()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SamplePane pane = window.Workbench.LuaPane;
				SampleCompletion? completion = pane.Session.Language.Completion;

				Assert.IsNotNull(completion);

				const string before = "local name = pri";
				pane.Editor.Text = before;
				pane.Editor.CaretOffset = before.Length;
				pane.Editor.TextArea.PerformTextInput("n");

				// The letter schedules a request after the input applied, and its answer, which `print` matches, opens the list: the end state
				// holds however long the debounce and the provider take, and each await yields the dispatcher to the timer and the answer.
				// CodeScopeTrackerTests own the scopes where a letter opens nothing.
				DateTime deadline = DateTime.UtcNow.AddSeconds(5);

				while (!completion.Controller.CurrentPresentation.IsListVisible && DateTime.UtcNow < deadline)
					await Task.Delay(20);

				Assert.IsTrue(completion.Controller.CurrentPresentation.IsListVisible);
			}
		);
	}

	[TestMethod]
	[DataRow(false, "C#: Roslyn, VB: highlighting only")]
	[DataRow(true, "C# and VB: Roslyn")]
	public async Task UseRoslynServer_AnswersVisualBasicOnlyWhenTheBuildCarriesIt(bool carriesVisualBasic, string description)
	{
		using var directory = new TemporaryDirectory("sample-");
		string serverFolder = Path.Combine(directory.Path, "server");
		Directory.CreateDirectory(serverFolder);

		if (carriesVisualBasic)
			File.WriteAllText(Path.Combine(serverFolder, "Microsoft.CodeAnalysis.VisualBasic.dll"), string.Empty);

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				// The provider starts the server lazily, so the missing executable fails only on the first request, which this test never sends.
				await window.Workbench.UseRoslynServerAsync(Path.Combine(serverFolder, "Microsoft.CodeAnalysis.LanguageServer.exe"));
				Assert.AreEqual(description, window.Workbench.RoslynDescription);

				await window.Workbench.UseRoslynServerAsync(null);
				Assert.AreEqual("C# and VB: highlighting only", window.Workbench.RoslynDescription);
			}
		);
	}

	[TestMethod]
	public async Task UseLuaServer_AnswersTheLuaPaneFromTheServer_AndTheInProcessProvidersAgainAfterwards()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWindowAsync(
			directory,

			async window =>
			{
				SampleWorkbench workbench = window.Workbench;
				SampleLanguageSession inProcess = workbench.LuaPane.Session.Language;

				// The provider starts the server lazily, so a missing executable fails only on the first request, which this test never sends.
				await workbench.UseLuaServerAsync(Path.Combine(directory.Path, "no-such-server.exe"));

				Assert.AreNotSame(inProcess, workbench.LuaPane.Session.Language);
				Assert.AreEqual("Lua: language server", workbench.LuaFeaturesDescription);

				await workbench.UseLuaServerAsync(null);
				workbench.LuaPane.Editor.Text = "pri";
				workbench.LuaPane.Editor.CaretOffset = 3;

				Assert.AreEqual("Lua: in-process providers", workbench.LuaFeaturesDescription);
				Assert.IsTrue(await (workbench.LuaPane.Session.Language.Completion?.RequestAsync() ?? Task.FromResult(false)));
			}
		);
	}
}
