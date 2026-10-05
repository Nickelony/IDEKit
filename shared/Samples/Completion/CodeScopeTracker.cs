using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

namespace Nickelony.Samples;

/// <summary>Tells code from comments and strings before an offset of one document, scanning only the line of the offset.</summary>
/// <remarks>
/// Each line start keeps the text of the comment or string still open there, from its opening delimiter to the end of the line
/// it opened on, so a block comment or a long string that opened on an earlier line is known without a scan from the document start, and a long
/// open comment carries one line. A block comment that nests another one across lines is approximated: the line that closes the inner one ends
/// the outer one.
/// </remarks>
internal sealed class CodeScopeTracker : IDisposable
{
	private readonly TextDocument _document;
	private readonly CommentSyntax _syntax;
	private readonly DocumentLineStateCache<string?> _openText;

	/// <param name="document">The document whose scopes are told.</param>
	/// <param name="syntax">The comment and string syntax of its language.</param>
	internal CodeScopeTracker(TextDocument document, CommentSyntax syntax)
	{
		_document = document;
		_syntax = syntax;
		_openText = new DocumentLineStateCache<string?>(document, (line, openText) => OpenTextAtEnd(openText, line));
	}

	/// <summary>The scope of the text that ends at the offset.</summary>
	internal CompletionScopes ScopeAt(int offset)
	{
		DocumentLine line = _document.GetLineByOffset(offset);
		string text = _openText.GetLineStartState(line.LineNumber) + _document.GetText(line.Offset, offset - line.Offset);

		if (CommentOperations.GetCodeEnd(text, _syntax) == text.Length)
			return CompletionScopes.Code;

		CommentSpan? last = null;

		foreach (CommentSpan comment in CommentOperations.EnumerateComments(text, _syntax))
			last = comment;

		return last is { } span && span.Range.EndOffset == text.Length ? CompletionScopes.Comment : CompletionScopes.StringLiteral;
	}

	public void Dispose() => _openText.Dispose();

	// A probe character after the text is code unless a comment or a string is still open. That one starts at the first character after the last
	// code character, because a string's closing quote counts as code and a closed comment or long string before it is carried harmlessly. A
	// construct that the line start carried and that the line leaves open keeps that state, so the carried text stays one line long.
	private string? OpenTextAtEnd(string? openText, string line)
	{
		string text = openText + line + "\n";
		string probed = text + "x";

		if (CommentOperations.GetCodeEnd(probed, _syntax) == probed.Length)
			return null;

		int start = CommentOperations.GetCodeEnd(text, _syntax);

		while (start < text.Length && char.IsWhiteSpace(text[start]))
			start++;

		return openText is not null && start == 0 ? openText : text[start..];
	}
}
