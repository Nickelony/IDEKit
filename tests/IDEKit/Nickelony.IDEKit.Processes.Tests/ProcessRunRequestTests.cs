using System.Text;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>Verifies the validation, collection snapshots, equality, and defaults of <see cref="ProcessRunRequest"/>.</summary>
[TestClass]
public sealed class ProcessRunRequestTests
{
	[TestMethod]
	[DataRow(-5L, DisplayName = "A negative timeout")]
	[DataRow(2_592_000_000L, DisplayName = "A timeout beyond Int32 milliseconds")]
	public void Timeout_OutOfRange_IsRejected(long milliseconds)
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new ProcessRunRequest { FileName = "tool.exe", Timeout = TimeSpan.FromMilliseconds(milliseconds) }
		);
	}

	[TestMethod]
	[DataRow(int.MaxValue, DisplayName = "The Int32 milliseconds maximum")]
	[DataRow(-1, DisplayName = "The infinite timeout")]
	[DataRow(0, DisplayName = "Zero")]
	public void Timeout_AcceptedValue_IsKept(int milliseconds)
	{
		var timeout = TimeSpan.FromMilliseconds(milliseconds);
		var request = new ProcessRunRequest { FileName = "tool.exe", Timeout = timeout };

		Assert.AreEqual(timeout, request.Timeout);
	}

	[TestMethod]
	public void Timeout_Default_IsNull()
	{
		var request = new ProcessRunRequest { FileName = "tool.exe" };
		Assert.IsNull(request.Timeout);
	}

	[TestMethod]
	public void Collections_Default_AreEmpty()
	{
		var request = new ProcessRunRequest { FileName = "tool.exe" };

		Assert.AreEqual(string.Empty, request.RawArguments);
		Assert.IsEmpty(request.ArgumentList);
		Assert.IsEmpty(request.EnvironmentVariables);
	}

	[TestMethod]
	public void RawArguments_Null_IsTreatedAsEmpty()
	{
		var request = new ProcessRunRequest { FileName = "tool.exe", RawArguments = null! };
		Assert.AreEqual(string.Empty, request.RawArguments);
	}

	[TestMethod]
	public void ArgumentList_CopiesCallerCollection()
	{
		var source = new List<string> { "first", "second" };
		var request = new ProcessRunRequest { FileName = "tool.exe", ArgumentList = source };

		source.Add("third");
		source[0] = "changed";

		Assert.AreSequenceEqual(["first", "second"], [.. request.ArgumentList]);
	}

	[TestMethod]
	public void EnvironmentVariables_CopiesCallerDictionary()
	{
		var source = new Dictionary<string, string> { ["KEY"] = "value" };
		var request = new ProcessRunRequest { FileName = "tool.exe", EnvironmentVariables = source };

		source["KEY"] = "changed";
		source["OTHER"] = "added";

		Assert.HasCount(1, request.EnvironmentVariables);
		Assert.AreEqual("value", request.EnvironmentVariables["KEY"]);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	public void FileName_Blank_IsRejected(string fileName) =>
		Assert.ThrowsExactly<ArgumentException>(() => new ProcessRunRequest { FileName = fileName });

	[TestMethod]
	public void FileName_Null_IsRejected() => Assert.ThrowsExactly<ArgumentNullException>(() => new ProcessRunRequest { FileName = null! });

	[TestMethod]
	public void ArgumentList_NullEntry_IsRejected() =>
		Assert.ThrowsExactly<ArgumentException>(() => new ProcessRunRequest { FileName = "tool.exe", ArgumentList = ["first", null!] });

	[TestMethod]
	public void CollectionProperties_Null_AreTreatedAsEmpty()
	{
		var request = new ProcessRunRequest
		{
			FileName = "tool.exe",
			ArgumentList = null!,
			EnvironmentVariables = null!,
		};

		Assert.IsEmpty(request.ArgumentList);
		Assert.IsEmpty(request.EnvironmentVariables);
	}

	[TestMethod]
	public void Equality_DefaultCollections_ComparesStructurally()
	{
		var first = new ProcessRunRequest { FileName = "tool.exe" };
		var second = new ProcessRunRequest { FileName = "tool.exe" };

		Assert.AreEqual(first, second);
	}

	[TestMethod]
	public void Equality_EqualNonEmptyCollections_ComparesStructurally()
	{
		var first = new ProcessRunRequest
		{
			FileName = "tool.exe",
			ArgumentList = ["-flag"],
			EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "value" },
		};

		var second = new ProcessRunRequest
		{
			FileName = "tool.exe",
			ArgumentList = ["-flag"],
			EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "value" },
		};

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
		Assert.AreEqual(first, first with { });
	}

	[TestMethod]
	public void Equality_ArgumentOrder_IsSignificant()
	{
		var first = new ProcessRunRequest { FileName = "tool.exe", ArgumentList = ["-a", "-b"] };
		var second = new ProcessRunRequest { FileName = "tool.exe", ArgumentList = ["-b", "-a"] };

		Assert.AreNotEqual(first, second);
	}

	[TestMethod]
	public void Equality_EnvironmentVariables_IgnoreInsertionOrder()
	{
		var first = new ProcessRunRequest
		{
			FileName = "tool.exe",
			EnvironmentVariables = new Dictionary<string, string> { ["FIRST"] = "1", ["SECOND"] = "2" },
		};

		var second = new ProcessRunRequest
		{
			FileName = "tool.exe",
			EnvironmentVariables = new Dictionary<string, string> { ["SECOND"] = "2", ["FIRST"] = "1" },
		};

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_EquivalentOutputEncodings_CompareEqual()
	{
		ProcessRunRequest first = CreateRequestWithEncodings(new UTF8Encoding(false), new UTF8Encoding(false));
		ProcessRunRequest second = CreateRequestWithEncodings(new UTF8Encoding(false), new UTF8Encoding(false));

		// The encodings are separate instances; equivalence comes from the code page and decoder fallback.
		Assert.AreNotSame(first.StandardOutputEncoding, second.StandardOutputEncoding);
		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equality_DifferentOutputEncodingCodePage_IsRejected()
	{
		ProcessRunRequest first = CreateRequestWithEncodings(Encoding.UTF8, Encoding.UTF8);
		ProcessRunRequest second = CreateRequestWithEncodings(Encoding.Unicode, Encoding.UTF8);

		Assert.AreNotEqual(first, second);
	}

	[TestMethod]
	public void Equality_DifferentOutputEncodingDecoderFallback_IsRejected()
	{
		ProcessRunRequest first = CreateRequestWithEncodings(Encoding.UTF8, Encoding.UTF8);

		ProcessRunRequest second = CreateRequestWithEncodings(
			Encoding.GetEncoding(Encoding.UTF8.CodePage, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("[invalid]")),
			Encoding.UTF8
		);

		Assert.AreNotEqual(first, second);
	}

	private static ProcessRunRequest CreateRequestWithEncodings(Encoding outputEncoding, Encoding errorEncoding)
	{
		return new()
		{
			FileName = "tool.exe",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = outputEncoding,
			StandardErrorEncoding = errorEncoding,
		};
	}
}
