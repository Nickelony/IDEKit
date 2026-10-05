using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the in-process Lua providers of the sample on their own: the unknown-function diagnostics and the hover text.</summary>
[TestClass]
public sealed class SampleLuaProviderTests
{
	[TestMethod]
	public async Task Diagnostics_ReportACallOfAnUnknownFunctionAsAnError_ButNotACallInAComment()
	{
		IReadOnlyList<Diagnostic> diagnostics = await new LuaDiagnosticsProvider().GetDiagnosticsAsync(
			new DiagnosticsRequest("test.lua", "local function greet(name) end\ngreet(1)\nprnt(2) -- not(3)\nprint(\"call(4)\")")
		);

		Diagnostic unknown = diagnostics.Single(diagnostic => diagnostic.Code == "unknown-function");

		Assert.AreEqual(DiagnosticSeverity.Error, unknown.Severity);
		Assert.AreEqual("Unknown function 'prnt'", unknown.Message);
	}

	[TestMethod]
	public async Task Diagnostics_DoNotReportRequire_OrAFieldOfADeclaredTable()
	{
		IReadOnlyList<Diagnostic> diagnostics = await new LuaDiagnosticsProvider().GetDiagnosticsAsync(
			new DiagnosticsRequest("test.lua", "local util = require(\"lib.util\")\nprint(util.shout(\"done\"))\nprint(other.shout(1))")
		);

		Assert.AreEqual("Unknown function 'other.shout'", diagnostics.Single(diagnostic => diagnostic.Code == "unknown-function").Message);
	}

	[TestMethod]
	public async Task Hover_OfADeclaredFunction_ShowsItsHeaderAndItsDocumentationComment()
	{
		const string script = "--- Greets a name.\n---@param name string The name.\nlocal function greet(name)\nend\ngreet(1)";

		HoverInformation? hover = await new KeywordHoverProvider().GetHoverAsync(
			new HoverRequest("test.lua", script, script.LastIndexOf("greet", StringComparison.Ordinal) + 1)
		);

		Assert.IsNotNull(hover);
		Assert.AreEqual("```lua\nlocal function greet(name)\n```\n\nGreets a name.\n\n_@param_ `name` string The name.", hover.Content.Value);
	}

	[TestMethod]
	public async Task Hover_OfAParameter_NamesTheFunctionItBelongsTo()
	{
		const string script = "local function greet(name)\n    return name\nend";

		HoverInformation? hover = await new KeywordHoverProvider().GetHoverAsync(
			new HoverRequest("test.lua", script, script.LastIndexOf("name", StringComparison.Ordinal) + 1)
		);

		Assert.IsNotNull(hover);
		Assert.AreEqual("```lua\n(parameter) name\n```\n\nA parameter of `greet`.", hover.Content.Value);
	}
}
