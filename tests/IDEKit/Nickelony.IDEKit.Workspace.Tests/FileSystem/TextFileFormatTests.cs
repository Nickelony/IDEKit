using System.Text;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class TextFileFormatTests
{
	[TestMethod]
	public void EncodingDisplayName_OfUtf8_NamesTheByteOrderMark()
	{
		Assert.AreEqual("UTF-8", new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.Lf).EncodingDisplayName);
		Assert.AreEqual("UTF-8 with BOM", new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.Lf).EncodingDisplayName);
	}

	[TestMethod]
	public void EncodingDisplayName_OfUtf16_IsTheSameWithAndWithoutTheByteOrderMark()
	{
		foreach (bool hasBom in new[] { false, true })
		{
			Assert.AreEqual("UTF-16 LE", new TextFileFormat(TextEncoding.Utf16LittleEndian, hasBom, LineTerminatorStyle.Lf).EncodingDisplayName);
			Assert.AreEqual("UTF-16 BE", new TextFileFormat(TextEncoding.Utf16BigEndian, hasBom, LineTerminatorStyle.Lf).EncodingDisplayName);
		}
	}

	[TestMethod]
	public void EncodingDisplayName_OfWindows1252_IsItsName() =>
		Assert.AreEqual("Windows-1252", new TextFileFormat(TextEncoding.Windows1252, false, LineTerminatorStyle.Lf).EncodingDisplayName);

	[TestMethod]
	public void EncodingDisplayName_OfAnyOtherEncoding_IsTheDisplayNameOfTheEncoding()
	{
		var shiftJis = TextEncoding.FromCodePage(932);
		var windows1250 = TextEncoding.FromCodePage(1250);

		Assert.AreEqual(Encoding.GetEncoding(932).EncodingName, new TextFileFormat(shiftJis, false, LineTerminatorStyle.Lf).EncodingDisplayName);
		Assert.AreEqual(windows1250.DisplayName, new TextFileFormat(windows1250, false, LineTerminatorStyle.Lf).EncodingDisplayName);
		Assert.AreNotEqual(shiftJis.DisplayName, windows1250.DisplayName);
	}

	[TestMethod]
	public void EncodingDisplayName_OfTheDefaultFormat_IsUtf8() => Assert.AreEqual("UTF-8", default(TextFileFormat).EncodingDisplayName);

	[TestMethod]
	public void EncodingDisplayName_IsNotPartOfTheValue()
	{
		var format = new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.CrLf);

		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.CrLf), format);
		Assert.AreNotEqual(format, format with { HasBom = false });
	}
}
