using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
#if AVALONIAEDIT
using AvaloniaEdit;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using ICSharpCode.AvalonEdit;

namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>
/// Covers the language session of a sample editor that no window shows: the diagnostics it pulls or has pushed, and the formatting it applies.
/// </summary>
[STATestClass]
public sealed class SampleLanguageSessionTests
{
	[TestMethod]
	public async Task Diagnostics_OfTheInProcessProvider_FollowAnEditAndArriveAgainAfterItSettles()
	{
		await SampleTestHost.RunAsync(async () =>
		{
			var editor = new TextEditor { Text = "prnt(1)" };

			using var session = new SampleLanguageSession(
				editor,
				features: SampleLanguageFeatures.CreateLua(),
				documentId: "pull-test.lua",
				theme: SampleThemes.Catalog.GetTheme("Light"),
				hoverDelay: TimeSpan.FromMilliseconds(400),
				applyEditAsync: static _ => Task.CompletedTask
			);

			DiagnosticsController? diagnostics = session.Diagnostics;
			Assert.IsNotNull(diagnostics);

			await SampleTestHost.WaitUntilAsync(() => diagnostics.Current.Count == 1, "The provider's diagnostic did not arrive.");

			Diagnostic unknown = diagnostics.Current[0];
			editor.Document.Insert(0, "\n");

			// The underline moved with "prnt" at once, and the pull after the edit settles reports it at its new offset.
			Diagnostic? moved = diagnostics.FindAt(1);

			Assert.IsNotNull(moved);
			Assert.AreEqual(unknown.Message, moved.Message);
			Assert.AreEqual(new TextRange(1, 4), moved.Range);

			await SampleTestHost.WaitUntilAsync(
				() => diagnostics.Current.Count == 1 && diagnostics.Current[0].Range.Offset == 1,
				"The pull after the edit did not arrive."
			);
		});
	}

	[TestMethod]
	public async Task Diagnostics_PushedByASource_UnderlineAndFollowTheirText()
	{
		await SampleTestHost.RunAsync(async () =>
		{
			var editor = new TextEditor { Text = "prnt(1)" };
			var source = new FakeDiagnosticsSource();

			using var session = new SampleLanguageSession(
				editor,

				features: SampleLanguageFeatures.None with
				{
					DiagnosticsSource = source,
				},

				documentId: "push-test.lua",
				theme: SampleThemes.Catalog.GetTheme("Light"),
				hoverDelay: TimeSpan.FromMilliseconds(400),
				applyEditAsync: static _ => Task.CompletedTask
			);

			DiagnosticsController? diagnostics = session.Diagnostics;
			Assert.IsNotNull(diagnostics);

			source.Raise("push-test.lua", [new Diagnostic(DiagnosticSeverity.Warning, "Undefined global 'prnt'.", new TextRange(0, 4))]);

			await SampleTestHost.WaitUntilAsync(() => diagnostics.Current.Count == 1, "The pushed diagnostic did not arrive.");

			editor.Document.Insert(0, "\n");

			Assert.AreEqual(new TextRange(1, 4), diagnostics.FindAt(1)?.Range);
			Assert.IsNull(diagnostics.FindAt(0));
		});
	}

	[TestMethod]
	public async Task Session_ForFeaturesWithoutDiagnostics_CreatesNoDiagnosticsController()
	{
		await SampleTestHost.RunAsync(() =>
		{
			var editor = new TextEditor { Text = "plain text" };

			using var session = new SampleLanguageSession(
				editor,
				features: SampleLanguageFeatures.None,
				documentId: "notes.txt",
				theme: SampleThemes.Catalog.GetTheme("Light"),
				hoverDelay: TimeSpan.FromMilliseconds(400),
				applyEditAsync: static _ => Task.CompletedTask
			);

			Assert.IsNull(session.Diagnostics);
			return Task.CompletedTask;
		});
	}

	[TestMethod]
	public async Task Format_WithAFormattingProvider_AppliesItsEdits()
	{
		await SampleTestHost.RunAsync(async () =>
		{
			var editor = new TextEditor { Text = "print(1)   " };
			SampleLanguageFeatures features = SampleLanguageFeatures.CreateLua() with { Formatting = new FakeFormattingProvider() };

			using var session = new SampleLanguageSession(
				editor,
				features,
				documentId: "format-test.lua",
				theme: SampleThemes.Catalog.GetTheme("Light"),
				hoverDelay: TimeSpan.FromMilliseconds(400),
				applyEditAsync: static _ => Task.CompletedTask
			);

			Assert.IsTrue(session.Formatting.CanFormat);
			Assert.AreEqual(FormattingOutcome.Applied, await session.Formatting.FormatAsync());
			Assert.AreEqual("print(1)", editor.Text);
		});
	}
}
