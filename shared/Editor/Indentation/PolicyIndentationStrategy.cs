using Nickelony.IDEKit.Core.Indentation;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Indentation;

#else

namespace Nickelony.IDEKit.AvalonEdit.Indentation;

#endif

/// <summary>Applies indentation computed by an <see cref="IIndentationPolicy"/> to editor lines.</summary>
/// <remarks>
/// <para>
/// The first line keeps its existing leading whitespace because it has no previous line.
/// </para>
/// <para>
/// Indentation writes through the document directly, because the editor's indentation strategy has no hook for the optional edit target
/// the other editing helpers accept. A host that owns document authority must observe <see cref="TextDocument.Changed"/> or reapply its own tracking
/// after an Enter or Tab triggers this strategy. Replacements use <see cref="OffsetChangeMappingType.RemoveAndInsert"/>, the mapping the editor's
/// own strategies use, so the caret ends up behind the applied indentation.
/// </para>
/// </remarks>
public sealed class PolicyIndentationStrategy : IIndentationStrategy
{
	private readonly TextEditorOptions _options;
	private readonly IIndentationPolicy _policy;
	private readonly Func<TextDocument, DocumentLine, bool>? _shouldUseSmartIndent;

	/// <param name="options">The editor options that provide the indentation unit.</param>
	/// <param name="policy">The policy that computes each line's desired indentation.</param>
	/// <param name="shouldUseSmartIndent">
	/// An optional predicate that receives the document and the line immediately before the line being indented and determines
	/// whether smart indentation applies to the line being indented. Return <see langword="false"/> to indent the line from its existing
	/// leading whitespace instead. If omitted, smart indentation is enabled.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The <see cref="TextEditorOptions"/> instance is captured at construction and later replacements are not observed,
	/// so pass the instance installed on the editor and recreate the strategy when the editor's options instance is replaced. The captured instance
	/// is read live, so mutating a setting that affects the indentation string (for example <c>IndentationSize</c>) is observed on the next call;
	/// within <see cref="IndentLines"/> the unit is read once for the whole batch.
	/// </remarks>
	public PolicyIndentationStrategy(
		TextEditorOptions options,
		IIndentationPolicy policy,
		Func<TextDocument, DocumentLine, bool>? shouldUseSmartIndent = null
	)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(policy);

		_options = options;
		_policy = policy;
		_shouldUseSmartIndent = shouldUseSmartIndent;
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="line"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The line was deleted or belongs to another document; the line is resolved
	/// through <see cref="TextDocumentExtensions.GetLiveLine(TextDocument, DocumentLine)"/> first.
	/// </exception>
	public void IndentLine(TextDocument document, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(line);

		IndentLine(document, document.GetLiveLine(line), GetIndentationUnit());
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// Clamps requested line numbers to the document and groups replacements in a single document update. A range whose end precedes its start
	/// still indents the clamped start line, and a start beyond the document indents the last line. The indentation unit is read once for the batch.
	/// </para>
	/// <para>
	/// Lines are indented sequentially, and each line is computed against the document as the earlier
	/// lines of the batch have already changed it. A policy that throws therefore aborts the batch with the exception escaping to the caller
	/// while the lines already processed in this call stay re-indented: the update group closes, so the partial result is a normal document state
	/// rather than a faulted one.
	/// </para>
	/// </remarks>
	public void IndentLines(TextDocument document, int beginLine, int endLine)
	{
		ArgumentNullException.ThrowIfNull(document);

		int startLine = Math.Max(1, Math.Min(beginLine, document.LineCount));
		int lastLine = Math.Max(startLine, Math.Min(endLine, document.LineCount));

		document.BeginUpdate();

		try
		{
			// The unit is read once for the batch instead of once per line; a host that changes the
			// options sees the new unit on the next call.
			string indentationUnit = GetIndentationUnit();

			for (int lineNumber = startLine; lineNumber <= lastLine; lineNumber++)
				IndentLine(document, document.GetLineByNumber(lineNumber), indentationUnit);
		}
		finally
		{
			document.EndUpdate();
		}
	}

	private void IndentLine(TextDocument document, DocumentLine line, string indentationUnit)
	{
		string lineText = document.GetText(line);
		string desiredIndentation = GetDesiredIndentation(document, line, lineText, indentationUnit);

		ReplaceLeadingWhitespace(document, line, lineText, desiredIndentation);
	}

	private string GetDesiredIndentation(TextDocument document, DocumentLine line, string lineText, string indentationUnit)
	{
		if (line.PreviousLine is null)
			return LineTextOperations.GetLeadingWhitespace(lineText);

		DocumentLine previousLine = line.PreviousLine;
		string previousLineText = document.GetText(previousLine);
		bool useSmartIndent = ShouldUseSmartIndent(document, previousLine);

		return _policy.GetDesiredIndentation(new IndentationRequest(previousLineText, CurrentLineText: lineText, indentationUnit, useSmartIndent));
	}

	private bool ShouldUseSmartIndent(TextDocument document, DocumentLine previousLine) =>
		_shouldUseSmartIndent is null || _shouldUseSmartIndent(document, previousLine);

	/// <summary>Returns the indentation unit for the current options.</summary>
	/// <remarks>
	/// The unit comes from <see cref="TextEditorOptions.GetIndentationString(int)"/>, so a host that overrides the virtual member controls the unit
	/// this strategy applies.
	/// </remarks>
	private string GetIndentationUnit() => _options.GetIndentationString(1);

	private static void ReplaceLeadingWhitespace(TextDocument document, DocumentLine line, string lineText, string desiredIndentation)
	{
		int leadingWhitespaceLength = LineTextOperations.GetLeadingWhitespaceLength(lineText);

		if (
			leadingWhitespaceLength == desiredIndentation.Length
			&& string.CompareOrdinal(strA: lineText, indexA: 0, strB: desiredIndentation, indexB: 0, length: leadingWhitespaceLength) == 0
		)
		{
			return;
		}

		document.Replace(
			offset: line.Offset,
			length: leadingWhitespaceLength,
			text: desiredIndentation,

			// RemoveAndInsert is the editor's own mapping for indentation: it keeps the caret behind the
			// applied indentation when an existing whitespace run at the caret is replaced.
			offsetChangeMappingType: OffsetChangeMappingType.RemoveAndInsert
		);
	}
}
