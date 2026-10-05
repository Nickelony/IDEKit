using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.Samples;

/// <summary>
/// Marks the TODO and FIXME markers of every Markdown and text file of the sample folder as information diagnostics, in each editor that shows
/// such a file.
/// </summary>
/// <remarks>
/// No language server covers those files, so this source competes with none. The check reads the text of an open editor and the file
/// on disk otherwise, and the workbench reports each change of an editor or of the folder.
/// </remarks>
internal sealed partial class SampleProjectDiagnostics : IDisposable
{
	// A file this long is not a note, so the check skips it instead of reading it on every change.
	private const long MaximumFileLength = 1_000_000;

	// The check waits this long after the last change, so typing runs it once.
	private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(300.0);

	private readonly string _root;
	private readonly Func<IReadOnlyDictionary<string, string>> _captureEditorTexts;
	private readonly ProjectDiagnosticsSource _source;

	/// <param name="root">The sample folder, whose Markdown and text files the check reads.</param>
	/// <param name="captureEditorTexts">
	/// Returns the text of each open editor of a covered file, keyed by path; it runs on the UI thread, which an editor requires.
	/// </param>
	/// <param name="dispatcher">The dispatcher of the UI thread.</param>
	/// <param name="logger">Receives the failures of the source, or <see langword="null"/> to log nothing.</param>
	internal SampleProjectDiagnostics(
		string root,
		Func<IReadOnlyDictionary<string, string>> captureEditorTexts,
		IUiDispatcher dispatcher,
		ILogger? logger = null
	)
	{
		_root = root;
		_captureEditorTexts = captureEditorTexts;

		_source = new ProjectDiagnosticsSource(
			hooks: new ProjectDiagnosticsSourceHooks(AnalyzeAsync),
			dispatcher,

			options: ProjectDiagnosticsSourceOptions.Default with
			{
				SettleDelay = s_settleDelay,
				DocumentIdComparer = SampleDocuments.PathComparer,
			},

			logger
		);
	}

	/// <summary>The source that the editors of covered files take their diagnostics from.</summary>
	internal IDiagnosticsSource Source => _source;

	/// <summary>Whether the check covers a file: a Markdown file or a text file.</summary>
	internal static bool Covers(string path) =>
		SampleFiles.KindOf(path) == SampleDocumentKind.Markdown || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);

	/// <summary>Reports that an editor or the folder changed, so the check runs again once the changes settle.</summary>
	/// <remarks>
	/// Call it on the UI thread.
	/// </remarks>
	internal void HandleProjectChanged() => _source.HandleProjectChanged();

	public void Dispose() => _source.Dispose();

	// The source calls the hook on the UI thread, so the text of the editors is captured here, and the scan of the folder moves off the thread.
	private Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> AnalyzeAsync(CancellationToken cancellationToken)
	{
		IReadOnlyDictionary<string, string> editorTexts = _captureEditorTexts();
		return Task.Run(() => Scan(editorTexts, cancellationToken), cancellationToken);
	}

	private IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> Scan(
		IReadOnlyDictionary<string, string> editorTexts,
		CancellationToken cancellationToken
	)
	{
		var result = new Dictionary<string, IReadOnlyList<Diagnostic>>(SampleDocuments.PathComparer);

		foreach (string path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Where(Covers))
		{
			cancellationToken.ThrowIfCancellationRequested();

			if ((editorTexts.TryGetValue(path, out string? text) ? text : ReadFile(path)) is not { } content)
				continue;

			IReadOnlyList<Diagnostic> markers = FindMarkers(content);

			if (markers.Count > 0)
				result[path] = markers;
		}

		return result;
	}

	private static string? ReadFile(string path)
	{
		try
		{
			return new FileInfo(path).Length <= MaximumFileLength ? File.ReadAllText(path) : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A file that vanished or is locked since the folder was listed has no markers to report, and the next change scans it again.
			return null;
		}
	}

	private static List<Diagnostic> FindMarkers(string text)
	{
		var lineMap = TextLineMap.Build(text);
		List<Diagnostic> markers = [];

		foreach (Match match in MarkerPattern().Matches(text))
		{
			// The match offset becomes a line and a character, as a compiler would report it, and the conversion turns that back into a range.
			TextPosition position = lineMap.GetPosition(match.Index);

			markers.Add(
				new Diagnostic(
					DiagnosticSeverity.Information,
					$"{match.Value} marker",

					DiagnosticRangeConversion.FromLineAndCharacter(
						lineMap,
						lineIndex: position.Line,
						character: position.Character,
						length: match.Length
					)
				)
				{
					Source = "project check",
					Code = "marker",
				}
			);
		}

		return markers;
	}

	[GeneratedRegex(@"\b(TODO|FIXME)\b", RegexOptions.CultureInvariant)]
	private static partial Regex MarkerPattern();
}
