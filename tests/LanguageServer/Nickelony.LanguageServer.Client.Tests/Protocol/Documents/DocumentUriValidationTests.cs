namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DocumentUriValidationTests
{
	[TestMethod]
	public void RequireText_UriWithText_ReturnsItUnchanged() =>
		Assert.AreEqual("file:///workspace/a.lua", DocumentUriValidation.RequireText("file:///workspace/a.lua"));

	[TestMethod]
	public void RequireText_NullUri_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => DocumentUriValidation.RequireText(null));

	[TestMethod]
	[DataRow("", DisplayName = "Empty URI is rejected")]
	[DataRow("   ", DisplayName = "Blank URI is rejected")]
	public void RequireText_BlankUri_ThrowsArgumentException(string uri) =>
		Assert.ThrowsExactly<ArgumentException>(() => DocumentUriValidation.RequireText(uri));
}
