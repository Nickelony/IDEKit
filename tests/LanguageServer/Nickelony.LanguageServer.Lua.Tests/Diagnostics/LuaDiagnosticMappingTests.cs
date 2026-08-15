namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class LuaDiagnosticMappingTests
{
	[TestMethod]
	public void Policy_UsesTheLuaFallbackMessage()
	{
		Assert.AreEqual("Unknown Lua diagnostic.", LuaDiagnosticMapping.Policy.UnknownMessage);
	}

	[TestMethod]
	[DataRow('.')]
	[DataRow(':')]
	[DataRow('\'')]
	[DataRow('"')]
	[DataRow('_')]
	[DataRow('a')]
	[DataRow('7')]
	public void Policy_WordRule_AcceptsLuaNameCharacters(char character)
	{
		// The rule extends the identifier rule with '.', ':', '\'', and '"', so member accesses and quoted
		// names resolve as one selectable run in the range anchor.
		Assert.IsTrue(LuaDiagnosticMapping.Policy.IsWordCharacter(character));
	}

	[TestMethod]
	[DataRow(' ')]
	[DataRow('\t')]
	[DataRow('(')]
	[DataRow('-')]
	public void Policy_WordRule_RejectsNonNameCharacters(char character)
	{
		Assert.IsFalse(LuaDiagnosticMapping.Policy.IsWordCharacter(character));
	}

	[TestMethod]
	public void Policy_AppliedToSharedParser_ShowsTheLuaFallbackMessage()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"   ",
						null,
						null)
				]),
			"test.lua",
			content,
			documentVersion: 1,
			policy: LuaDiagnosticMapping.Policy,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("Unknown Lua diagnostic.", publishedDiagnostics.Diagnostics[0].Message);
	}
}
