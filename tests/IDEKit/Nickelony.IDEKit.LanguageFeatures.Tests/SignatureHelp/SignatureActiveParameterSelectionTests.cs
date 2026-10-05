using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

/// <summary>Verifies the state handling and the construction guards of the active-parameter selection.</summary>
[TestClass]
public sealed class SignatureActiveParameterSelectionTests
{
	[TestMethod]
	public void NotSpecified_IsTheDefaultState()
	{
		SignatureActiveParameterSelection selection = default;

		Assert.AreEqual(SignatureActiveParameterSelection.NotSpecified, selection);
		Assert.IsFalse(selection.IsSpecified);
		Assert.IsFalse(selection.IsNone);
		Assert.IsNull(selection.Index);
		Assert.AreEqual("NotSpecified", selection.ToString());
	}

	[TestMethod]
	public void None_ReportsNoActiveParameter()
	{
		SignatureActiveParameterSelection selection = SignatureActiveParameterSelection.None;

		Assert.IsFalse(selection.IsSpecified);
		Assert.IsTrue(selection.IsNone);
		Assert.IsNull(selection.Index);
		Assert.AreNotEqual(SignatureActiveParameterSelection.NotSpecified, selection);
		Assert.AreEqual("None", selection.ToString());
	}

	[TestMethod]
	public void At_ReportsTheIndex()
	{
		var selection = SignatureActiveParameterSelection.At(3);

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
		Assert.AreNotEqual(SignatureActiveParameterSelection.NotSpecified, SignatureActiveParameterSelection.At(0));
	}

	[TestMethod]
	public void At_NegativeIndex_Throws() => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SignatureActiveParameterSelection.At(-1));

	[TestMethod]
	public void Equality_ComparesStateAndIndex()
	{
		Assert.AreEqual(SignatureActiveParameterSelection.At(2), SignatureActiveParameterSelection.At(2));
		Assert.AreNotEqual(SignatureActiveParameterSelection.At(2), SignatureActiveParameterSelection.At(3));
		Assert.AreNotEqual(SignatureActiveParameterSelection.At(0), SignatureActiveParameterSelection.None);
		Assert.AreNotEqual(SignatureActiveParameterSelection.None, SignatureActiveParameterSelection.NotSpecified);
	}
}
