using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.IntelliSense.Tests.CodeActions;

/// <summary>
/// Verifies the lazily materialized document text carrier: eager and on-demand creation, single
/// materialization under concurrent reads, and the declared-length contract.
/// </summary>
[TestClass]
public sealed class TextCodeActionDocumentTextTests
{
	[TestMethod]
	public void FromText_ReportsTheTextAndItsLength()
	{
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromText("text");

		Assert.AreEqual(4, document.TextLength);
		Assert.AreEqual("text", document.Text);
		Assert.AreEqual("text", document.ToString());
	}

	[TestMethod]
	public void FromFactory_MaterializesOnFirstReadAndCaches()
	{
		int calls = 0;
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromFactory(4, () =>
		{
			calls++;
			return "text";
		});

		// Creating the carrier must not run the factory; the first read does, and only once.
		Assert.AreEqual(4, document.TextLength);
		Assert.AreEqual(0, calls);

		Assert.AreEqual("text", document.Text);
		Assert.AreEqual(1, calls);
		Assert.AreEqual("text", document.Text);
		Assert.AreEqual("text", document.ToString());
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void Text_ConcurrentFirstRead_MaterializesOnce()
	{
		int calls = 0;
		using var gate = new ManualResetEventSlim();
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromFactory(4, () =>
		{
			// Hold the first reader inside the factory so the other readers are forced to wait on it
			// instead of racing a second factory run.
			gate.Wait(TimeSpan.FromSeconds(5));
			Interlocked.Increment(ref calls);
			return "text";
		});

		string[] results = new string[8];
		Task[] readers = [.. Enumerable.Range(0, results.Length).Select(index => Task.Run(() => results[index] = document.Text))];

		gate.Set();

		Assert.IsTrue(Task.WaitAll(readers, TimeSpan.FromSeconds(10)), "Concurrent readers did not finish.");

		foreach (string result in results)
			Assert.AreEqual("text", result);

		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void FromFactory_LengthMismatch_FailsEveryRead()
	{
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromFactory(4, () => "longer");

		// The declared length is what a context validated its offsets against, so a factory that
		// produces a different length must not publish the text; the failure is cached like the value.
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = document.Text);
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = document.Text);
	}

	[TestMethod]
	public void FromFactory_InvalidArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextCodeActionDocumentText.FromFactory(-1, () => string.Empty));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextCodeActionDocumentText.FromFactory(0, null!));
	}

	[TestMethod]
	public void FromText_NullText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextCodeActionDocumentText.FromText(null!));
	}
}
