namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the bounded standard-error buffer a session keeps for failure diagnostics: a noisy server must not be
/// able to grow the buffer without bound, and the summary must stay compact and ordered.
/// </summary>
[TestClass]
public sealed class TransportSessionTests
{
	private const int MaxRetainedLines = 5;
	private const int MaxRetainedLineLength = 200;

	private static TransportSession CreateSession()
		=> new(generation: 1, process: null, Stream.Null, Stream.Null);

	[TestMethod]
	public void GetRecentStandardErrorSummary_WithoutRecordedLines_ReturnsNull()
	{
		TransportSession session = CreateSession();

		Assert.IsNull(session.GetRecentStandardErrorSummary());
	}

	[TestMethod]
	public void RecordStandardErrorLine_IgnoresBlankLines()
	{
		TransportSession session = CreateSession();

		session.RecordStandardErrorLine(string.Empty);
		session.RecordStandardErrorLine("   ");
		session.RecordStandardErrorLine("\t");

		Assert.IsNull(session.GetRecentStandardErrorSummary());
	}

	[TestMethod]
	public void RecordStandardErrorLine_TrimsSurroundingWhitespace()
	{
		TransportSession session = CreateSession();

		session.RecordStandardErrorLine("  server warning  ");

		Assert.AreEqual("server warning", session.GetRecentStandardErrorSummary());
	}

	[TestMethod]
	public void RecordStandardErrorLine_TruncatesAnOverlongLine()
	{
		TransportSession session = CreateSession();
		string line = new('x', MaxRetainedLineLength + 50);

		session.RecordStandardErrorLine(line);

		string? summary = session.GetRecentStandardErrorSummary();

		Assert.IsNotNull(summary);
		Assert.AreEqual(MaxRetainedLineLength + 3, summary.Length, summary);
		Assert.IsTrue(summary.EndsWith("...", StringComparison.Ordinal), summary);
		Assert.AreEqual(line[..MaxRetainedLineLength], summary[..MaxRetainedLineLength]);
	}

	[TestMethod]
	public void RecordStandardErrorLine_KeepsOnlyTheMostRecentLines()
	{
		TransportSession session = CreateSession();

		for (int i = 1; i <= MaxRetainedLines + 3; i++)
			session.RecordStandardErrorLine($"line-{i}");

		string? summary = session.GetRecentStandardErrorSummary();

		Assert.AreEqual(string.Join(" | ", ["line-4", "line-5", "line-6", "line-7", "line-8"]), summary);
	}
}
