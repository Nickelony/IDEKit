#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Pins how the completion presenter drops <see langword="null"/> entries from the items it is about to show: an owned array without them is reused.
/// </summary>
[TestClass]
public sealed class CompletionPresenterNonNullItemsTests
{
	[TestMethod]
	public void NonNullItems_ArrayWithoutNullEntries_ReturnsTheSameArray()
	{
		ICompletionData[] items = [new StubCompletionData("first"), new StubCompletionData("second")];
		Assert.AreSame(items, CompletionPresenter.NonNullItems(items));
	}

	[TestMethod]
	public void NonNullItems_EmptyArray_ReturnsTheSameArray()
	{
		ICompletionData[] items = [];
		Assert.AreSame(items, CompletionPresenter.NonNullItems(items));
	}

	[TestMethod]
	public void NonNullItems_ArrayWithNullEntries_ReturnsACopyWithoutThemAndLeavesTheArrayAlone()
	{
		ICompletionData first = new StubCompletionData("first");
		ICompletionData second = new StubCompletionData("second");
		ICompletionData third = new StubCompletionData("third");

		ICompletionData[] items = [null!, first, null!, null!, second, third, null!];

		ICompletionData[] kept = CompletionPresenter.NonNullItems(items);

		Assert.AreNotSame(items, kept);
		Assert.HasCount(3, kept);
		Assert.AreSame(first, kept[0]);
		Assert.AreSame(second, kept[1]);
		Assert.AreSame(third, kept[2]);

		Assert.HasCount(7, items);
		Assert.IsNull(items[0]);
		Assert.AreSame(first, items[1]);
	}

	[TestMethod]
	public void NonNullItems_ArrayOnlyOfNullEntries_ReturnsAnEmptyArray()
	{
		ICompletionData[] kept = CompletionPresenter.NonNullItems([null!, null!]);
		Assert.IsEmpty(kept);
	}

	[TestMethod]
	public void NonNullItems_ArrayWithoutNullEntries_AllocatesNothing()
	{
		ICompletionData[] items = [.. Enumerable.Range(0, 500).Select(index => (ICompletionData)new StubCompletionData("item" + index))];

		// Warm up the path so JIT tiering stays out of the measured window.
		for (int i = 0; i < 1_000; i++)
			_ = CompletionPresenter.NonNullItems(items);

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int i = 0; i < 10_000; i++)
			_ = CompletionPresenter.NonNullItems(items);

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// Copying the array allocates kilobytes per call, tens of megabytes over these calls; the bound leaves room for runtime noise only.
		Assert.IsLessThan(10_000, allocated, $"Expected no allocation, but {allocated} bytes were allocated.");
	}
}
