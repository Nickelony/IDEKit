using Nickelony.IDEKit.LanguageFeatures.Formatting;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class FormattingRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Constructor_StoresTheRequestValues()
	{
		var options = new FormattingOptions(8, insertSpaces: true);
		var request = new FormattingRequest(DocumentId, "content", options);

		Assert.AreEqual("content", request.DocumentText);
		Assert.AreSame(options, request.Options);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		var options = new FormattingOptions(4, insertSpaces: true);

		Assert.ThrowsExactly<ArgumentNullException>(() => new FormattingRequest(DocumentId, null!, options));
		Assert.ThrowsExactly<ArgumentNullException>(() => new FormattingRequest(DocumentId, "content", null!));
	}
}
