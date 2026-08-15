namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxFixtures.SemicolonLine;

	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyleLine;

	// The JSON-ish URL line reused by the string-awareness tests.

	private const string UrlLine = "\"url\": \"http://example.com\",";
}
