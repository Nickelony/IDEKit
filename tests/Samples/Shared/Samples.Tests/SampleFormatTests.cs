using System.IO;
using System.Text;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>
/// Covers the encoding and the line terminators of the sample tabs: what the status bar says, and what reopening, saving and converting do.
/// </summary>
[STATestClass]
public sealed class SampleFormatTests
{
	private static readonly string s_platformLabel = Environment.NewLine == "\r\n" ? "CRLF" : "LF";

	private static async Task<SamplePane> OpenBytesAsync(SampleWorkbench workbench, TemporaryDirectory directory, string name, byte[] bytes)
	{
		string path = Path.Combine(directory.Path, name);
		await File.WriteAllBytesAsync(path, bytes);

		return await workbench.OpenAsync(path);
	}

	private static byte[] Encode(string text, TextEncoding encoding, bool hasBom = false) =>
		WorkspaceTextCodec.Encode(text, new TextFileFormat(encoding, hasBom, LineTerminatorStyle.Lf));

	private static TextFileFormat FormatOf(TextEncoding encoding, bool hasBom = false) => new(encoding, hasBom, LineTerminatorStyle.None);

	[TestMethod]
	public async Task OpeningFiles_ShowsTheEncodingAndTheLineTerminatorsEachOneHas()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				await OpenBytesAsync(workbench, directory, name: "lf.txt", bytes: Encoding.UTF8.GetBytes("one\ntwo\n"));

				Assert.AreEqual("UTF-8", workbench.EncodingDescription);
				Assert.AreEqual("LF", workbench.LineTerminatorDescription);

				await OpenBytesAsync(workbench, directory, name: "bom.txt", bytes: Encode("one\r\ntwo\r\n", TextEncoding.Utf8, hasBom: true));

				Assert.AreEqual("UTF-8 with BOM", workbench.EncodingDescription);
				Assert.AreEqual("CRLF", workbench.LineTerminatorDescription);

				await OpenBytesAsync(
					workbench,
					directory,
					name: "utf16.txt",
					bytes: Encode("one\rtwo\r", TextEncoding.Utf16LittleEndian, hasBom: true)
				);

				Assert.AreEqual("UTF-16 LE", workbench.EncodingDescription);
				Assert.AreEqual("CR", workbench.LineTerminatorDescription);

				await OpenBytesAsync(workbench, directory, name: "latin.txt", bytes: Encode("café\n", TextEncoding.Windows1252));
				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);

				await OpenBytesAsync(workbench, directory, name: "mixed.txt", bytes: Encoding.UTF8.GetBytes("one\ntwo\r\n"));
				Assert.AreEqual("Mixed", workbench.LineTerminatorDescription);

				await OpenBytesAsync(workbench, directory, name: "single.txt", bytes: Encoding.UTF8.GetBytes("one line"));

				Assert.AreEqual(s_platformLabel, workbench.LineTerminatorDescription);
				Assert.Contains("no line terminator", workbench.LineTerminatorToolTip);
			}
		);
	}

	[TestMethod]
	public async Task TheStatusBar_IsEmptyWhenNoTabIsOpen()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				foreach (SamplePane pane in workbench.Panes.ToList())
					await workbench.CloseAsync(pane);

				Assert.IsNull(workbench.ActivePane);
				Assert.AreEqual(string.Empty, workbench.EncodingDescription);
				Assert.AreEqual(string.Empty, workbench.LineTerminatorDescription);
				Assert.IsFalse(workbench.UsesEncoding(TextEncoding.Utf8));
			}
		);
	}

	[TestMethod]
	public async Task ANewFile_IsUtf8WithoutALineTerminatorYet()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				await workbench.CreateFileAsync(directory.Path, "new.txt");

				Assert.AreEqual(Path.Combine(directory.Path, "new.txt"), workbench.ActivePane?.FilePath);
				Assert.AreEqual("UTF-8", workbench.EncodingDescription);
				Assert.AreEqual(s_platformLabel, workbench.LineTerminatorDescription);
				Assert.AreEqual(0, new FileInfo(workbench.ActivePane!.FilePath).Length);
			}
		);
	}

	[TestMethod]
	public async Task UseLineTerminator_ConvertsTheActivePaneAsOneUndoStep_AndTheDocumentRecordsTheStyle()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(
					workbench,
					directory,
					name: "lines.txt",
					bytes: Encoding.UTF8.GetBytes("one\r\ntwo\r\nthree\r\n")
				);

				pane.Editor.Document.UndoStack.ClearAll();
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				await workbench.UseLineTerminatorAsync(LineTerminatorStyle.Lf);

				Assert.AreEqual("one\ntwo\nthree\n", pane.Editor.Text);
				Assert.AreEqual("LF", workbench.LineTerminatorDescription);
				Assert.IsTrue(workbench.UsesLineTerminator(LineTerminatorStyle.Lf));
				Assert.IsFalse(workbench.UsesLineTerminator(LineTerminatorStyle.CrLf));
				Assert.IsTrue(pane.View.IsDirty);
				Assert.Contains("Converted the line terminators to LF; one undo step restores them", messages);

				pane.Editor.Document.UndoStack.Undo();

				Assert.AreEqual("one\r\ntwo\r\nthree\r\n", pane.Editor.Text);
				Assert.IsFalse(pane.Editor.Document.UndoStack.CanUndo);

				// The view publishes the restored text with its style, so the document is clean and says CRLF again.
				pane.View.PublishPendingEdits();

				await SampleTestHost.WaitUntilAsync(
					() => workbench.LineTerminatorDescription == "CRLF",
					"The document did not take the style of the restored text."
				);

				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task UseLineTerminator_OfATextThatHasTheStyle_ChangesNothing()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "lines.txt", bytes: Encoding.UTF8.GetBytes("one\ntwo\n"));
				pane.Editor.Document.UndoStack.ClearAll();
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				await workbench.UseLineTerminatorAsync(LineTerminatorStyle.Lf);

				Assert.Contains("Every line terminator is LF already", messages);
				Assert.IsFalse(pane.Editor.Document.UndoStack.CanUndo);
				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task UseLineTerminator_OfATextWithoutALineTerminator_SaysWhatTheFirstNewLineGets()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "single.txt", bytes: Encoding.UTF8.GetBytes("one line"));
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				await workbench.UseLineTerminatorAsync(Environment.NewLine == "\n" ? LineTerminatorStyle.CrLf : LineTerminatorStyle.Lf);

				Assert.Contains($"The text has no line terminator yet; the editor inserts {s_platformLabel} for the first new line", messages);
				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task SavingAfterAConversion_WritesTheNewLineTerminators()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "lines.txt", bytes: Encoding.UTF8.GetBytes("one\r\ntwo\r\n"));

				await workbench.UseLineTerminatorAsync(LineTerminatorStyle.Lf);
				Assert.IsTrue(await workbench.SaveAsync(pane));

				Assert.AreSequenceEqual("one\ntwo\n"u8.ToArray(), await File.ReadAllBytesAsync(pane.FilePath));
				Assert.AreEqual("LF", workbench.LineTerminatorDescription);
				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task TypingALineTerminatorOfAnotherStyle_MakesTheDocumentMixedOnceTheViewPublishes()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "lines.txt", bytes: Encoding.UTF8.GetBytes("one\ntwo\n"));
				int raised = 0;

				workbench.FormatChanged += (_, _) => raised++;

				pane.Editor.Text = "one\ntwo\r\nthree\n";
				pane.View.PublishPendingEdits();

				await SampleTestHost.WaitUntilAsync(
					() => workbench.LineTerminatorDescription == "Mixed",
					"The status bar did not follow the published text."
				);

				Assert.IsGreaterThan(0, raised);
				Assert.Contains("differ", workbench.LineTerminatorToolTip);
			}
		);
	}

	[TestMethod]
	public async Task SaveWithEncoding_WritesTheFileInThatEncoding_AndTheStatusBarFollows()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "text.txt", bytes: Encoding.UTF8.GetBytes("café\n"));
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				(TextFileFormat Format, byte[] Expected, string Label)[] cases =
				[
					(
						FormatOf(TextEncoding.Utf16LittleEndian, hasBom: true),
						[0xFF, 0xFE, 0x63, 0x00, 0x61, 0x00, 0x66, 0x00, 0xE9, 0x00, 0x0A, 0x00],
						"UTF-16 LE"
					),

					(FormatOf(TextEncoding.Windows1252), [0x63, 0x61, 0x66, 0xE9, 0x0A], "Windows-1252"),
					(FormatOf(TextEncoding.Utf8, hasBom: true), [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x0A], "UTF-8 with BOM"),
					(FormatOf(TextEncoding.Utf8), [0x63, 0x61, 0x66, 0xC3, 0xA9, 0x0A], "UTF-8"),
				];

				foreach ((TextFileFormat format, byte[] expected, string label) in cases)
				{
					await workbench.SaveWithEncodingAsync(format);

					Assert.AreSequenceEqual(expected, await File.ReadAllBytesAsync(pane.FilePath), label);
					Assert.AreEqual(label, workbench.EncodingDescription, label);
					Assert.IsTrue(workbench.UsesFormat(format), label);
					Assert.AreEqual("café\n", pane.Editor.Text, label);
					Assert.IsFalse(pane.View.IsDirty, label);
					Assert.Contains($"Saved text.txt as {label}", messages, label);
				}
			}
		);
	}

	[TestMethod]
	public async Task SaveWithEncoding_OfTextThatTheEncodingCannotHold_ReportsTheFailureCode_AndKeepsTheFileAndTheEncoding()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				byte[] original = Encoding.UTF8.GetBytes("日本\n");
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "text.txt", bytes: original);

				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				await workbench.SaveWithEncodingAsync(FormatOf(TextEncoding.Windows1252));

				Assert.Contains("Could not save text.txt as Windows-1252: WriteFailed (InvalidEncoding)", messages);
				Assert.AreSequenceEqual(original, await File.ReadAllBytesAsync(pane.FilePath));
				Assert.AreEqual("UTF-8", workbench.EncodingDescription);
				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task ReopenWithEncoding_ReadsTheFileAgainWithThatEncoding()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				// The Czech letters U+0159 U+011B U+0161 in Windows-1250, which are not valid UTF-8 and so open as Windows-1252.
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "czech.txt", bytes: [0xF8, 0xEC, 0x9A]);
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);

				TextFileFormat windows1250 = SampleWorkbench.EncodingFormats.Single(format => format.Encoding == TextEncoding.FromCodePage(1250));

				await workbench.ReopenWithEncodingAsync(windows1250);

				Assert.AreEqual("řěš", pane.Editor.Text);
				Assert.AreEqual(windows1250.Encoding.DisplayName, workbench.EncodingDescription);
				Assert.IsTrue(workbench.UsesEncoding(windows1250.Encoding));
				Assert.IsFalse(pane.View.IsDirty);
				Assert.Contains($"Reopened czech.txt with {windows1250.Encoding.DisplayName}", messages);

				await workbench.ReopenWithEncodingAsync(FormatOf(TextEncoding.Windows1252));

				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);
				Assert.AreNotEqual("řěš", pane.Editor.Text);
			}
		);
	}

	[TestMethod]
	public async Task ReopenWithEncoding_ThatReadsTheSameText_SaysSo()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "ascii.txt", bytes: "plain\n"u8.ToArray());
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				// The text and the encoding stay, so nothing is read again.
				await workbench.ReopenWithEncodingAsync(FormatOf(TextEncoding.Utf8));

				Assert.Contains("ascii.txt reads the same with UTF-8", messages);

				// The text stays, but the document is read with another encoding from now on.
				await workbench.ReopenWithEncodingAsync(FormatOf(TextEncoding.Windows1252));

				Assert.Contains("Reopened ascii.txt with Windows-1252", messages);
				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);
				Assert.AreEqual("plain\n", pane.Editor.Text);
				Assert.IsFalse(pane.View.IsDirty);
			}
		);
	}

	[TestMethod]
	public async Task ReopenWithEncoding_ThatCannotDecodeTheFile_ReportsTheFailureCode_AndKeepsTheText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				// 0xE9 0x20 is not a Shift-JIS sequence, and it is not valid UTF-8, so the file opens as Windows-1252.
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "bad.txt", bytes: [0xE9, 0x20, 0x41]);
				string text = pane.Editor.Text;

				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);

				await workbench.ReopenWithEncodingAsync(FormatOf(TextEncoding.FromCodePage(932)));

				Assert.IsTrue(
					messages.Exists(message =>
						message.StartsWith("Reopen with ", StringComparison.Ordinal) && message.Contains("failed: ReadFailed (InvalidEncoding)")
					)
				);

				Assert.AreEqual(text, pane.Editor.Text);
				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);
			}
		);
	}

	[TestMethod]
	public async Task ReopenWithEncoding_OfADocumentWithUnsavedChanges_ReportsUnsavedChangesAndAsksWhatToDo()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "czech.txt", bytes: [0xF8, 0xEC, 0x9A]);
				TextFileFormat windows1250 = FormatOf(TextEncoding.FromCodePage(1250));

				var messages = new List<string>();
				var asked = new List<string>();

				SampleSaveDecision decision = SampleSaveDecision.Cancel;
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				workbench.DecideSaveAsync = askedPane =>
				{
					asked.Add(askedPane.Title);
					return Task.FromResult(decision);
				};

				pane.Editor.Text += "!";

				// Cancel keeps the unsaved text and the encoding.
				await workbench.ReopenWithEncodingAsync(windows1250);

				Assert.HasCount(1, asked);
				Assert.IsTrue(messages.Exists(message => message.Contains("(UnsavedChanges)")));
				Assert.Contains($"Reopen with {windows1250.Encoding.DisplayName} canceled: czech.txt keeps its unsaved changes", messages);
				Assert.EndsWith("!", pane.Editor.Text);
				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);

				// Discard drops the text and reopens the file.
				decision = SampleSaveDecision.Discard;
				await workbench.ReopenWithEncodingAsync(windows1250);

				Assert.AreEqual("řěš", pane.Editor.Text);
				Assert.AreEqual(windows1250.Encoding.DisplayName, workbench.EncodingDescription);

				// Save writes the typed text in the encoding it has, then reopens the file.
				pane.Editor.Text += "!";
				decision = SampleSaveDecision.Save;
				await workbench.ReopenWithEncodingAsync(FormatOf(TextEncoding.Windows1252));

				Assert.AreSequenceEqual(new byte[] { 0xF8, 0xEC, 0x9A, 0x21 }, await File.ReadAllBytesAsync(pane.FilePath));
				Assert.AreEqual("Windows-1252", workbench.EncodingDescription);
				Assert.HasCount(3, asked);
			}
		);
	}

	[TestMethod]
	public async Task EncodingFormats_AreTheLabelsTheStatusBarOffers()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				string[] labels = [.. SampleWorkbench.EncodingFormats.Select(format => format.EncodingDisplayName)];

				Assert.AreSequenceEqual(
					[
						"UTF-8",
						"UTF-8 with BOM",
						"UTF-16 LE",
						"UTF-16 BE",
						"Windows-1252",
						TextEncoding.FromCodePage(1250).DisplayName,
						TextEncoding.FromCodePage(1251).DisplayName,
						TextEncoding.FromCodePage(932).DisplayName,
					],

					labels
				);

				Assert.AreSequenceEqual([LineTerminatorStyle.Lf, LineTerminatorStyle.CrLf], [.. SampleWorkbench.LineTerminatorStyles]);

				return Task.CompletedTask;
			}
		);
	}

	[TestMethod]
	public async Task APlainSaveThatFails_NamesTheFailureCode()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				byte[] original = Encode("caf\u00E9\n", TextEncoding.Windows1252);
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "latin.txt", bytes: original);

				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				// U+0159 has no Windows-1252 byte, so the encoding the document records cannot write the text.
				pane.Editor.Text += "\u0159";

				Assert.IsFalse(await workbench.SaveAsync(pane));

				Assert.Contains("Could not save latin.txt: WriteFailed (InvalidEncoding)", messages);
				Assert.AreSequenceEqual(original, await File.ReadAllBytesAsync(pane.FilePath));
				Assert.IsTrue(pane.View.IsDirty);
			}
		);
	}

	// The Czech words U+0158 ... in Windows-1250: the byte 0x9D (U+0165) is undefined in Windows-1252 and the bytes are not UTF-8, so no candidate decodes them.
	private static readonly byte[] s_undecodable =
	[
		0x50,
		0xF8,
		0xED,
		0x6C,
		0x69,
		0x9A,
		0x20,
		0x9E,
		0x6C,
		0x75,
		0x9D,
		0x6F,
		0x75,
		0xE8,
		0x6B,
		0xFD,
		0x20,
		0x6B,
		0xF9,
		0xF2,
	];

	[TestMethod]
	public async Task OpeningAFileFromTheExplorerThatNoCandidateDecodes_NamesTheFailureCode_AndOpeningItWithAnEncodingShowsIt()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				string path = Path.Combine(directory.Path, "cp1250.txt");
				await File.WriteAllBytesAsync(path, s_undecodable);
				var messages = new List<string>();
				workbench.StatusChanged += (_, e) => messages.Add(e.Message);

				await workbench.OpenFromExplorerAsync(path);

				Assert.IsTrue(messages.Exists(message => message.StartsWith("Failed: LoadFailed (InvalidEncoding): ", StringComparison.Ordinal)));
				Assert.DoesNotContain(pane => pane.FilePath == path, workbench.Panes);

				var windows1250 = TextEncoding.FromCodePage(1250);

				await workbench.OpenWithEncodingAsync(path, windows1250);

				SamplePane opened = workbench.Panes.Single(pane => pane.FilePath == path);

				Assert.AreEqual("P\u0159\u00EDli\u0161 \u017Elu\u0165ou\u010D\u006B\u00FD k\u016F\u0148", opened.Editor.Text);
				Assert.AreEqual(windows1250.DisplayName, workbench.EncodingDescription);
				Assert.Contains($"Opened cp1250.txt with {windows1250.DisplayName}", messages);
			}
		);
	}

	[TestMethod]
	public async Task OpeningAFileWithAnEncoding_WhenATabShowsItAlready_ReadsItAgainWithThatEncoding()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane pane = await OpenBytesAsync(workbench, directory, name: "czech.txt", bytes: [0xF8, 0xEC, 0x9A]);
				var windows1250 = TextEncoding.FromCodePage(1250);

				await workbench.OpenWithEncodingAsync(pane.FilePath, windows1250);

				Assert.AreEqual("\u0159\u011B\u0161", pane.Editor.Text);
				Assert.AreEqual(windows1250.DisplayName, workbench.EncodingDescription);
				Assert.HasCount(3, workbench.Panes);
			}
		);
	}

	[TestMethod]
	public async Task ConvertingTheIndentationInOneViewOfASplit_KeepsTheCaretAndTheSelectionOfTheOtherViewOnTheirText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = await OpenBytesAsync(
					workbench,
					directory,
					name: "two.lua",
					bytes: Encoding.UTF8.GetBytes("function f()\n  if x then\n    print(y)\n  end\nend\n")
				);

				SamplePane split = (await workbench.SplitAsync(first))!;

				// In the first view the caret sits before the y of print(y), and print(y) is selected.
				int y = first.Editor.Text.IndexOf("y)", StringComparison.Ordinal);
				first.Editor.Select(y, 1);
				workbench.ActivePane = split;

				workbench.UseIndentation(insertSpaces: false, size: 2);
				workbench.Commands.First(command => command.Id == SampleCommandIds.ConvertIndentationToTabs).Execute();

				Assert.AreEqual("function f()\n\tif x then\n\t\tprint(y)\n\tend\nend\n", split.Editor.Text);

				await SampleTestHost.WaitUntilAsync(() => first.Editor.Text == split.Editor.Text, "The first view did not follow the conversion.");

				Assert.AreEqual("y", first.Editor.SelectedText);

				first.Editor.CaretOffset = first.Editor.Text.IndexOf("y)", StringComparison.Ordinal);
				workbench.ActivePane = first;
				workbench.Commands.First(command => command.Id == SampleCommandIds.ConvertIndentationToSpaces).Execute();

				await SampleTestHost.WaitUntilAsync(() => first.Editor.Text == split.Editor.Text, "The split view did not follow the conversion.");

				Assert.AreEqual("function f()\n  if x then\n    print(y)\n  end\nend\n", split.Editor.Text);
				Assert.AreEqual("y)", split.Editor.Text.Substring(split.Editor.Text.IndexOf("y)", StringComparison.Ordinal), 2));
				Assert.AreEqual("y", first.Editor.Text.Substring(first.Editor.CaretOffset, 1));

				// Both views published the same text, so the store holds it once and neither view reports a conflict.
				await SampleTestHost.WaitUntilAsync(() => !first.View.HasPendingEdits && !split.View.HasPendingEdits, "The views did not settle.");

				Assert.IsFalse(first.View.HasConflict);
				Assert.IsFalse(split.View.HasConflict);
			}
		);
	}

	[TestMethod]
	public async Task ConvertingTheLineTerminatorsInOneViewOfASplit_KeepsTheCaretAndTheSelectionOfTheOtherViewOnTheirText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				SamplePane first = await OpenBytesAsync(
					workbench,
					directory,
					name: "lines.txt",
					bytes: Encoding.UTF8.GetBytes("alpha\r\nbeta\r\ngamma\r\ndelta\r\n")
				);

				SamplePane split = (await workbench.SplitAsync(first))!;

				// In the first view the caret sits inside gamma, and beta is selected in the split.
				first.Editor.CaretOffset = first.Editor.Text.IndexOf("mma", StringComparison.Ordinal);
				int beta = split.Editor.Text.IndexOf("beta", StringComparison.Ordinal);
				split.Editor.Select(beta, 4);
				workbench.ActivePane = split;

				await workbench.UseLineTerminatorAsync(LineTerminatorStyle.Lf);

				Assert.AreEqual("alpha\nbeta\ngamma\ndelta\n", first.Editor.Text);
				Assert.AreEqual("mma", first.Editor.Text.Substring(first.Editor.CaretOffset, 3));
				Assert.AreEqual("beta", split.Editor.SelectedText);

				await SampleTestHost.WaitUntilAsync(() => !first.View.HasPendingEdits && !split.View.HasPendingEdits, "The views did not settle.");

				Assert.IsFalse(first.View.HasConflict);
				Assert.IsFalse(split.View.HasConflict);
				Assert.AreEqual("LF", workbench.LineTerminatorDescription);
			}
		);
	}

	[TestMethod]
	public async Task UndoingAConversionInOneViewOfASplit_KeepsTheCaretAndTheSelectionOfTheOtherViewOnTheirText()
	{
		using var directory = new TemporaryDirectory("sample-");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			async workbench =>
			{
				const string spaces = "function f()\n  if x then\n    print(y)\n  end\nend\n";

				SamplePane first = await OpenBytesAsync(workbench, directory, name: "two.lua", bytes: Encoding.UTF8.GetBytes(spaces));
				SamplePane split = (await workbench.SplitAsync(first))!;

				workbench.ActivePane = split;
				split.Editor.Document.UndoStack.ClearAll();
				workbench.Commands.First(command => command.Id == SampleCommandIds.ConvertIndentationToTabs).Execute();

				await SampleTestHost.WaitUntilAsync(() => first.Editor.Text == split.Editor.Text, "The first view did not follow the conversion.");

				// The undo happens in the split, and print is selected in the first view.
				first.Editor.Select(first.Editor.Text.IndexOf("print", StringComparison.Ordinal), 5);

				split.Editor.Document.UndoStack.Undo();
				await SampleTestHost.WaitUntilAsync(() => first.Editor.Text == spaces, "The first view did not follow the undo.");

				Assert.AreEqual("print", first.Editor.SelectedText);

				// The caret of the split sits before the y while the first view converts and undoes.
				split.Editor.CaretOffset = split.Editor.Text.IndexOf("y)", StringComparison.Ordinal);
				workbench.ActivePane = first;
				first.Editor.Document.UndoStack.ClearAll();
				workbench.Commands.First(command => command.Id == SampleCommandIds.ConvertIndentationToTabs).Execute();

				await SampleTestHost.WaitUntilAsync(
					() => split.Editor.Text.Contains('\t', StringComparison.Ordinal),
					"The split did not follow the conversion."
				);

				first.Editor.Document.UndoStack.Undo();

				await SampleTestHost.WaitUntilAsync(
					() => split.Editor.Text == spaces && !first.View.HasPendingEdits && !split.View.HasPendingEdits,
					"The views did not settle."
				);

				Assert.AreEqual("y", split.Editor.Text.Substring(split.Editor.CaretOffset, 1));
			}
		);
	}
}
