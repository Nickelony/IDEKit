namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Canonical <see cref="CommentSyntax"/> instances shared by the comment tests.</summary>
/// <remarks>
/// The syntax shapes are constructed in one place so they cannot drift between test files; each test class aliases the instances it uses
/// under its local field names.
/// </remarks>
internal static class CommentSyntaxTestHelpers
{
	/// <summary>Line comments only, without string awareness.</summary>
	internal static readonly CommentSyntax SemicolonLine = new(";", null, StringLiteralStyles.None);

	/// <summary>Line and block comments, without string awareness.</summary>
	internal static readonly CommentSyntax SemicolonBlock = new(";", new BlockCommentSyntax("/*", "*/"), StringLiteralStyles.None);

	/// <summary>Line-only C-style syntax aware of double-quoted and triple-double-quoted strings.</summary>
	internal static readonly CommentSyntax CStyleLine = new("//", null, StringLiteralStyles.DoubleQuoted | StringLiteralStyles.TripleDoubleQuoted);

	/// <summary>Line-only C-style syntax aware of double-quoted, single-quoted, and backtick strings.</summary>
	internal static readonly CommentSyntax CStyleLineBacktick = new(
		"//",
		null,
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.SingleQuoted | StringLiteralStyles.BacktickQuoted
	);

	/// <summary>Line-only C-style syntax aware of double-quoted and single-quoted strings.</summary>
	internal static readonly CommentSyntax CStyleLineDoubleSingle = new(
		"//",
		null,
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.SingleQuoted
	);

	/// <summary>Block-aware C-style syntax aware of double-quoted and triple-double-quoted strings.</summary>
	internal static readonly CommentSyntax CStyle = new(
		"//",
		new BlockCommentSyntax("/*", "*/"),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.TripleDoubleQuoted
	);

	/// <summary>Block-aware C-style syntax without string awareness.</summary>
	internal static readonly CommentSyntax CStylePlain = new("//", new BlockCommentSyntax("/*", "*/"), StringLiteralStyles.None);

	/// <summary>Block-aware C-style syntax aware of double-quoted strings only.</summary>
	internal static readonly CommentSyntax CStyleDoubleQuoted = new("//", new BlockCommentSyntax("/*", "*/"), StringLiteralStyles.DoubleQuoted);

	/// <summary>Nesting block-aware C-style syntax aware of double-quoted and triple-double-quoted strings.</summary>
	internal static readonly CommentSyntax CStyleNested = new(
		"//",
		new BlockCommentSyntax("/*", "*/", allowNesting: true),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.TripleDoubleQuoted
	);

	/// <summary>Nesting block-aware C-style syntax aware of double-quoted strings only.</summary>
	internal static readonly CommentSyntax CStyleNestedDoubleQuoted = new(
		"//",
		new BlockCommentSyntax("/*", "*/", allowNesting: true),
		StringLiteralStyles.DoubleQuoted
	);

	/// <summary>Block-aware C-style syntax aware of double-quoted, triple-double-quoted, and verbatim double-quoted strings.</summary>
	internal static readonly CommentSyntax CStyleVerbatim = new(
		"//",
		new BlockCommentSyntax("/*", "*/"),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.TripleDoubleQuoted | StringLiteralStyles.VerbatimDoubleQuoted
	);

	/// <summary>Block-aware C# syntax aware of double-quoted and verbatim double-quoted strings.</summary>
	internal static readonly CommentSyntax CSharpVerbatim = new(
		"//",
		new BlockCommentSyntax("/*", "*/"),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.VerbatimDoubleQuoted
	);

	/// <summary>Hash line comments aware of double-quoted and single-quoted strings.</summary>
	internal static readonly CommentSyntax HashLine = new("#", null, StringLiteralStyles.DoubleQuoted | StringLiteralStyles.SingleQuoted);

	/// <summary>Lua block comments without string awareness.</summary>
	internal static readonly CommentSyntax Lua = new("--", new BlockCommentSyntax("--[[", "]]"), StringLiteralStyles.None);

	/// <summary>Line-only Lua syntax aware of long-bracket strings.</summary>
	internal static readonly CommentSyntax LuaLongBracketLine = new("--", null, StringLiteralStyles.LongBracketQuoted);

	/// <summary>Nesting block-aware Lua syntax aware of double-quoted and single-quoted strings.</summary>
	internal static readonly CommentSyntax LuaNested = new(
		"--",
		new BlockCommentSyntax("--[[", "]]", allowNesting: true),
		StringLiteralStyles.DoubleQuoted | StringLiteralStyles.SingleQuoted
	);

	/// <summary>Nesting block-aware Lua syntax aware of single-quoted and long-bracket strings.</summary>
	internal static readonly CommentSyntax LuaNestedLongBracket = new(
		"--",
		new BlockCommentSyntax("--[[", "]]", allowNesting: true),
		StringLiteralStyles.SingleQuoted | StringLiteralStyles.LongBracketQuoted
	);
}
