using System.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class TextEncodingTests
{
	[TestMethod]
	public void Presets_CarryTheirCodePageAndAFixedDisplayName()
	{
		Assert.AreEqual(65001, TextEncoding.Utf8.CodePage);
		Assert.AreEqual(1200, TextEncoding.Utf16LittleEndian.CodePage);
		Assert.AreEqual(1201, TextEncoding.Utf16BigEndian.CodePage);
		Assert.AreEqual(1252, TextEncoding.Windows1252.CodePage);

		Assert.AreEqual("UTF-8", TextEncoding.Utf8.DisplayName);
		Assert.AreEqual("UTF-16 LE", TextEncoding.Utf16LittleEndian.DisplayName);
		Assert.AreEqual("UTF-16 BE", TextEncoding.Utf16BigEndian.DisplayName);
		Assert.AreEqual("Windows-1252", TextEncoding.Windows1252.DisplayName);
		Assert.AreEqual("UTF-8", TextEncoding.Utf8.ToString());
	}

	[TestMethod]
	public void Default_IsUtf8SoADefaultFormatIsUtf8()
	{
		Assert.AreEqual(TextEncoding.Utf8, default);
		Assert.AreEqual(TextEncoding.Utf8, default(TextFileFormat).Encoding);
		Assert.AreEqual(65001, default(TextEncoding).CodePage);
	}

	[TestMethod]
	[DataRow(65001)]
	[DataRow(1200)]
	[DataRow(1201)]
	[DataRow(1252)]
	public void FromCodePage_OfAPresetIsEqualToThePreset(int codePage)
	{
		var created = TextEncoding.FromCodePage(codePage);

		Assert.AreEqual(codePage, created.CodePage);
		Assert.IsTrue(created == PresetOf(codePage));
		Assert.AreEqual(PresetOf(codePage).DisplayName, created.DisplayName);
	}

	[TestMethod]
	public void Equality_IsByCodePageAndIsConsistentAcrossEveryForm()
	{
		var shiftJis = TextEncoding.FromCodePage(932);
		var sameShiftJis = new TextEncoding(932);

		Assert.IsTrue(shiftJis.Equals(sameShiftJis));
		Assert.IsTrue(shiftJis.Equals((object)sameShiftJis));
		Assert.IsTrue(shiftJis == sameShiftJis);
		Assert.IsFalse(shiftJis != sameShiftJis);
		Assert.AreEqual(shiftJis.GetHashCode(), sameShiftJis.GetHashCode());

		Assert.IsFalse(shiftJis.Equals(TextEncoding.Utf8));
		Assert.IsFalse(shiftJis.Equals("shift_jis"));
		Assert.IsFalse(shiftJis == TextEncoding.Utf8);
		Assert.IsTrue(shiftJis != TextEncoding.Utf8);
	}

	[TestMethod]
	public void DisplayName_OfAnotherCodePageIsTheDescriptionTheRuntimeGivesIt()
	{
		var shiftJis = TextEncoding.FromCodePage(932);

		Assert.AreEqual(Encoding.GetEncoding(932).EncodingName, shiftJis.DisplayName);
		Assert.AreEqual(shiftJis.DisplayName, shiftJis.ToString());
	}

	[TestMethod]
	[DataRow(874)]
	[DataRow(1250)]
	[DataRow(1251)]
	[DataRow(1252)]
	[DataRow(1253)]
	[DataRow(1254)]
	[DataRow(1255)]
	[DataRow(1256)]
	[DataRow(1257)]
	[DataRow(1258)]
	public void DisplayName_OfAWindowsSingleBytePage_IsWindowsAndTheCodePage(int codePage) =>
		Assert.AreEqual($"Windows-{codePage}", TextEncoding.FromCodePage(codePage).DisplayName);

	[TestMethod]
	[DataRow(437)]
	[DataRow(932)]
	[DataRow(936)]
	[DataRow(28591)]
	public void DisplayName_OfAnyOtherPage_IsTheDescriptionTheRuntimeGivesIt(int codePage) =>
		Assert.AreEqual(Encoding.GetEncoding(codePage).EncodingName, TextEncoding.FromCodePage(codePage).DisplayName);

	[TestMethod]
	public void DisplayName_OfAWindowsPageTheProcessCannotResolve_IsStillWindowsAndTheCodePage() =>
		Assert.AreEqual("Windows-1252", new TextEncoding(1252).DisplayName);

	[TestMethod]
	[DataRow(12000)]
	[DataRow(12001)]
	[DataRow(65000)]
	public void FromCodePage_RejectsUtf32AndUtf7(int codePage)
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => TextEncoding.FromCodePage(codePage));
		Assert.AreEqual("codePage", exception.ParamName);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[DataRow(65536)]
	public void FromCodePage_RejectsZeroNegativeAndAboveTheCodePageRange(int codePage)
	{
		// Zero would select the code page of the machine, which differs between machines.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextEncoding.FromCodePage(codePage));
	}

	[TestMethod]
	public void FromCodePage_OfACodePageNothingServes_ThrowsAtTheFactoryAndNamesTheCodePagesEncodingProvider()
	{
		NotSupportedException exception = Assert.Throws<NotSupportedException>(() => TextEncoding.FromCodePage(4242));

		Assert.Contains("CodePagesEncodingProvider", exception.Message);
		Assert.Contains("4242", exception.Message);
	}

	private static TextEncoding PresetOf(int codePage)
	{
		return codePage switch
		{
			65001 => TextEncoding.Utf8,
			1200 => TextEncoding.Utf16LittleEndian,
			1201 => TextEncoding.Utf16BigEndian,
			_ => TextEncoding.Windows1252,
		};
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[DataRow(3)]
	[DataRow(42)]
	public void FromCodePage_OfANumberTheRuntimeRefuses_ThrowsNotSupportedAndNamesTheCodePagesEncodingProvider(int codePage)
	{
		// The runtime throws an ArgumentException for these numbers, and the factory reports them as it reports any code page nothing serves.
		NotSupportedException exception = Assert.Throws<NotSupportedException>(() => TextEncoding.FromCodePage(codePage));
		Assert.Contains("CodePagesEncodingProvider", exception.Message);
	}
}
