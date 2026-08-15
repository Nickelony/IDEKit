using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.IDEKit.IntelliSense.Tests.Signatures;

/// <summary>
/// Verifies the three states and the construction guards of the active-parameter selection.
/// </summary>
[TestClass]
public sealed class TextSignatureActiveParameterSelectionTests
{
	[TestMethod]
	public void NotSpecified_IsTheDefaultState()
	{
		TextSignatureActiveParameterSelection selection = default;

		Assert.AreEqual(TextSignatureActiveParameterSelection.NotSpecified, selection);
		Assert.IsFalse(selection.IsSpecified);
		Assert.IsFalse(selection.IsNone);
		Assert.IsNull(selection.Index);
		Assert.AreEqual("NotSpecified", selection.ToString());
	}

	[TestMethod]
	public void None_ReportsNoActiveParameter()
	{
		TextSignatureActiveParameterSelection selection = TextSignatureActiveParameterSelection.None;

		Assert.IsFalse(selection.IsSpecified);
		Assert.IsTrue(selection.IsNone);
		Assert.IsNull(selection.Index);
		Assert.AreNotEqual(TextSignatureActiveParameterSelection.NotSpecified, selection);
		Assert.AreEqual("None", selection.ToString());
	}

	[TestMethod]
	public void At_ReportsTheIndex()
	{
		TextSignatureActiveParameterSelection selection = TextSignatureActiveParameterSelection.At(3);

		Assert.IsTrue(selection.IsSpecified);
		Assert.IsFalse(selection.IsNone);
		Assert.AreEqual(3, selection.Index);
		Assert.AreEqual("3", selection.ToString());
	}

	[TestMethod]
	public void At_Zero_IsDistinctFromNotSpecified()
	{
		// Index 0 is a real selection: a producer that names the first parameter must not be
		// conflated with one that does not override the selection at all.
		Assert.AreNotEqual(TextSignatureActiveParameterSelection.NotSpecified, TextSignatureActiveParameterSelection.At(0));
	}

	[TestMethod]
	public void At_NegativeIndex_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextSignatureActiveParameterSelection.At(-1));

	[TestMethod]
	public void Equality_ComparesStateAndIndex()
	{
		Assert.AreEqual(TextSignatureActiveParameterSelection.At(2), TextSignatureActiveParameterSelection.At(2));
		Assert.AreNotEqual(TextSignatureActiveParameterSelection.At(2), TextSignatureActiveParameterSelection.At(3));
		Assert.AreNotEqual(TextSignatureActiveParameterSelection.At(0), TextSignatureActiveParameterSelection.None);
		Assert.AreNotEqual(TextSignatureActiveParameterSelection.None, TextSignatureActiveParameterSelection.NotSpecified);
	}
}
