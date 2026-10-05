using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Reads the answers that the real servers gave to <c>workspace/symbol</c>: one of LuaLS and one of the Roslyn language server.
/// The entries and their order are as captured; only the folder of each document URI was rewritten to a fixed workspace folder.
/// </summary>
/// <remarks>
/// Both servers answer the flat <c>SymbolInformation</c> shape with a range, and neither advertises <c>workspaceSymbol/resolve</c>.
/// The tests of the <c>WorkspaceSymbol</c> shape with a bare <c>{ uri }</c> location, and of resolution, are synthetic: no server in the repository
/// sends that shape.
/// </remarks>
[TestClass]
public sealed class LanguageServerFeatureProviderCapturedWorkspaceSymbolTests
{
	// LuaLS, query "spawn", over main.lua and util.lua of a folder. It lists the global function spawnGlobal twice.
	private const string LuaAnswer = """
		[
		  {"kind":13,"location":{"range":{"end":{"character":30,"line":8},"start":{"character":9,"line":8}},"uri":"file:///c%3A/Workspace/luaws/main.lua"},"name":"GlobalTable.spawnBoss"},
		  {"kind":13,"location":{"range":{"end":{"character":20,"line":5},"start":{"character":9,"line":5}},"uri":"file:///c%3A/Workspace/luaws/util.lua"},"name":"spawnGlobal"},
		  {"kind":13,"location":{"range":{"end":{"character":25,"line":0},"start":{"character":15,"line":0}},"uri":"file:///c%3A/Workspace/luaws/main.lua"},"name":"spawnEnemy"},
		  {"kind":8,"location":{"range":{"end":{"character":30,"line":8},"start":{"character":21,"line":8}},"uri":"file:///c%3A/Workspace/luaws/main.lua"},"name":"spawnBoss"},
		  {"kind":8,"location":{"range":{"end":{"character":22,"line":1},"start":{"character":11,"line":1}},"uri":"file:///c%3A/Workspace/luaws/util.lua"},"name":"spawnMinion"},
		  {"kind":13,"location":{"range":{"end":{"character":20,"line":5},"start":{"character":9,"line":5}},"uri":"file:///c%3A/Workspace/luaws/util.lua"},"name":"spawnGlobal"}
		]
		""";

	// Roslyn, query "Spawn", over Program.cs and Widget.cs of a project. The container is a description and not a symbol name.
	private const string RoslynAnswer = """
		[
		  {"name":"SpawnWidget","kind":6,"location":{"uri":"file:///C:/Workspace/csws/Widget.cs","range":{"start":{"line":6,"character":16},"end":{"line":6,"character":27}}},"containerName":"in Widget (project Probe (net8.0))"},
		  {"name":"SpawnEnemy","kind":6,"location":{"uri":"file:///C:/Workspace/csws/Program.cs","range":{"start":{"line":6,"character":16},"end":{"line":6,"character":26}}},"containerName":"in Program (project Probe (net8.0))"},
		  {"name":"SpawnOld","kind":6,"location":{"uri":"file:///C:/Workspace/csws/Program.cs","range":{"start":{"line":8,"character":34},"end":{"line":8,"character":42}}},"containerName":"in Program (project Probe (net8.0))"},
		  {"name":"SpawnPart","kind":6,"location":{"uri":"file:///C:/Workspace/csws/Widget.cs","range":{"start":{"line":8,"character":42},"end":{"line":8,"character":51}}},"containerName":"in Widget (project Probe (net8.0))"}
		]
		""";

	private static string PathOf(string folder, string name) => Path.GetFullPath(Path.Combine("C:\\Workspace", folder, name));

	private async Task<IReadOnlyList<WorkspaceSymbol>> ListAsync(string answer)
	{
		using var client = new FakeStatefulLanguageServerClient { WorkspaceSymbolResponse = JsonDocument.Parse(answer).RootElement.Clone() };
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		return await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("spawn"), TestContext.CancellationToken);
	}

	[TestMethod]
	public async Task LuaLs_Answer_IsReadAsItWasGivenIncludingTheEntryItListsTwice()
	{
		IReadOnlyList<WorkspaceSymbol> symbols = await ListAsync(LuaAnswer);

		Assert.AreSequenceEqual(
			["GlobalTable.spawnBoss", "spawnGlobal", "spawnEnemy", "spawnBoss", "spawnMinion", "spawnGlobal"],
			[.. symbols.Select(symbol => symbol.Name)]
		);

		Assert.AreSequenceEqual(
			[
				DocumentSymbolKind.Variable,
				DocumentSymbolKind.Variable,
				DocumentSymbolKind.Variable,
				DocumentSymbolKind.Field,
				DocumentSymbolKind.Field,
				DocumentSymbolKind.Variable,
			],

			[.. symbols.Select(symbol => symbol.Kind)]
		);

		Assert.IsTrue(symbols.All(symbol => symbol.ContainerName is null && symbol.Location.Range is not null));
		Assert.AreEqual(PathOf("luaws", "main.lua"), symbols[0].Location.DocumentId, ignoreCase: true);
		Assert.AreEqual(new TextPositionRange(new TextPosition(8, 9), new TextPosition(8, 30)), symbols[0].Location.Range);
		Assert.AreEqual(new TextPositionRange(new TextPosition(5, 9), new TextPosition(5, 20)), symbols[1].Location.Range);
		Assert.AreEqual(symbols[1].Location.Range, symbols[5].Location.Range);
		Assert.AreEqual(symbols[1].Location.DocumentId, symbols[5].Location.DocumentId);
	}

	[TestMethod]
	public async Task LuaLs_ANullResult_IsAnEmptyList()
	{
		// LuaLS answers null to a query of several words.
		IReadOnlyList<WorkspaceSymbol> symbols = await ListAsync("null");
		Assert.IsEmpty(symbols);
	}

	[TestMethod]
	public async Task Roslyn_Answer_IsReadWithTheContainerDescriptionAndTheRanges()
	{
		IReadOnlyList<WorkspaceSymbol> symbols = await ListAsync(RoslynAnswer);

		Assert.AreSequenceEqual(["SpawnWidget", "SpawnEnemy", "SpawnOld", "SpawnPart"], [.. symbols.Select(symbol => symbol.Name)]);
		Assert.IsTrue(symbols.All(symbol => symbol.Kind == DocumentSymbolKind.Method));
		Assert.AreEqual("in Widget (project Probe (net8.0))", symbols[0].ContainerName);
		Assert.AreEqual("in Program (project Probe (net8.0))", symbols[1].ContainerName);
		Assert.AreEqual(PathOf("csws", "Widget.cs"), symbols[0].Location.DocumentId, ignoreCase: true);
		Assert.AreEqual(new TextPositionRange(new TextPosition(6, 16), new TextPosition(6, 27)), symbols[0].Location.Range);
		Assert.AreEqual(new TextPositionRange(new TextPosition(8, 34), new TextPosition(8, 42)), symbols[2].Location.Range);
	}

	public TestContext TestContext { get; set; } = null!;
}
