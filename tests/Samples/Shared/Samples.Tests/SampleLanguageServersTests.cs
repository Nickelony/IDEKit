using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.LanguageServer.Provider;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the hooks that show the messages, the progress and the edits of a language server in the sample.</summary>
[TestClass]
public sealed class SampleLanguageServersTests
{
	private static readonly WorkspaceEdit s_edit = new([
		new TextDocumentEdit("a.cs", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 0)), "using System;\n")]),
	]);

	[TestMethod]
	public void DescribeProgress_FollowsAUnitOfWorkFromItsBeginToItsEnd()
	{
		var titles = new ConcurrentDictionary<string, string>();

		string[] lines =
		[
			SampleLanguageServers.DescribeProgress(
				"Roslyn",
				new WorkDoneProgress(Token: "load", Kind: WorkDoneProgressKind.Begin, Title: "Loading projects", Message: null, Percentage: 0),
				titles
			),

			SampleLanguageServers.DescribeProgress(
				"Roslyn",
				new WorkDoneProgress(Token: "load", Kind: WorkDoneProgressKind.Report, Title: null, Message: "App.csproj", Percentage: 40),
				titles
			),

			SampleLanguageServers.DescribeProgress(
				"Roslyn",
				new WorkDoneProgress(Token: "load", Kind: WorkDoneProgressKind.End, Title: null, Message: null, Percentage: null),
				titles
			),
		];

		Assert.AreSequenceEqual(
			["Roslyn: Loading projects (0%)", "Roslyn: Loading projects - App.csproj (40%)", "Roslyn: Loading projects finished"],
			lines
		);

		Assert.IsEmpty(titles);
	}

	[TestMethod]
	public void DescribeProgress_KeepsTheTitlesOfConcurrentUnitsOfWorkApart()
	{
		var titles = new ConcurrentDictionary<string, string>();

		SampleLanguageServers.DescribeProgress(
			"Lua",
			new WorkDoneProgress(Token: "a", Kind: WorkDoneProgressKind.Begin, Title: "Indexing", Message: null, Percentage: null),
			titles
		);

		SampleLanguageServers.DescribeProgress(
			"Lua",
			new WorkDoneProgress(Token: "b", Kind: WorkDoneProgressKind.Begin, Title: "Diagnosing", Message: null, Percentage: null),
			titles
		);

		Assert.AreEqual(
			"Lua: Indexing - 3/9",

			SampleLanguageServers.DescribeProgress(
				"Lua",
				new WorkDoneProgress(Token: "a", Kind: WorkDoneProgressKind.Report, Title: null, Message: "3/9", Percentage: null),
				titles
			)
		);

		Assert.AreEqual(
			"Lua: Diagnosing finished",

			SampleLanguageServers.DescribeProgress(
				"Lua",
				new WorkDoneProgress(Token: "b", Kind: WorkDoneProgressKind.End, Title: null, Message: null, Percentage: null),
				titles
			)
		);
	}

	[TestMethod]
	public void DescribeProgress_ForAnEndWithoutABegin_NamesTheWorkGenerically()
	{
		Assert.AreEqual(
			"Lua: Work finished",

			SampleLanguageServers.DescribeProgress(
				"Lua",
				new WorkDoneProgress(Token: "x", Kind: WorkDoneProgressKind.End, Title: null, Message: null, Percentage: null),
				new ConcurrentDictionary<string, string>()
			)
		);
	}

	[TestMethod]
	public void DescribeMessage_NamesAnErrorAsOne()
	{
		Assert.AreEqual(
			"Lua error: Out of memory",
			SampleLanguageServers.DescribeMessage("Lua", new WindowMessage(WindowMessageKind.Error, "Out of memory"))
		);

		Assert.AreEqual("Lua: Reloaded", SampleLanguageServers.DescribeMessage("Lua", new WindowMessage(WindowMessageKind.Information, "Reloaded")));
	}

	[TestMethod]
	public async Task CreateHooks_ShowTheMessagesAndTheProgressAndForwardTheEdits()
	{
		var lines = new List<string>();
		var edits = new List<WorkspaceEdit>();

		LanguageServerFeatureProviderHooks hooks = SampleLanguageServers.CreateHooks(
			"Roslyn",
			lines.Add,

			edit =>
			{
				edits.Add(edit);
				return Task.FromResult(true);
			}
		);

		hooks.ShowMessage!(new WindowMessage(WindowMessageKind.Warning, "The project is restoring"));

		hooks.Progress!(
			new WorkDoneProgress(Token: "load", Kind: WorkDoneProgressKind.Begin, Title: "Loading projects", Message: null, Percentage: null)
		);

		Assert.IsTrue(await hooks.ApplyWorkspaceEdit!(s_edit, CancellationToken.None));

		Assert.AreSequenceEqual(["Roslyn: The project is restoring", "Roslyn: Loading projects"], lines);
		Assert.AreSame(s_edit, edits.Single());
		Assert.IsNull(hooks.ShowMessageRequest);
	}

	[TestMethod]
	public void LuaServerArchive_ExtractsOnce_AndReturnsTheExecutable()
	{
		using var directory = new TemporaryDirectory("sample-");

		string archive = Path.Combine(directory.Path, "LuaLS.zip");
		string executableName = OperatingSystem.IsWindows() ? "lua-language-server.exe" : "lua-language-server";

		using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
		{
			using StreamWriter writer = new(zip.CreateEntry($"bin/{executableName}").Open());
			writer.Write("not a real server");
		}

		string extractionRoot = Path.Combine(directory.Path, "extracted");
		string executable = LuaServerArchive.Resolve(archive, extractionRoot);

		Assert.AreEqual(Path.Combine(extractionRoot, "bin", executableName), executable);

		// An unchanged archive is not extracted again, so a file the server wrote beside it survives.
		string marker = Path.Combine(extractionRoot, "marker.txt");
		File.WriteAllText(marker, "kept");
		LuaServerArchive.Resolve(archive, extractionRoot);
		Assert.IsTrue(File.Exists(marker));

		Assert.AreEqual("server.exe", LuaServerArchive.Resolve("server.exe", extractionRoot));
	}
}
