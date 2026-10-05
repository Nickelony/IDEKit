using System.Reflection;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the transitions of a logical document directly: the fields each one changes, the version pair and the store lock.</summary>
[TestClass]
public sealed class LogicalDocumentTests
{
	private static readonly FileStamp s_stampA = StoreCollaboratorsTestHost.StampOf("a");
	private static readonly FileStamp s_stampB = StoreCollaboratorsTestHost.StampOf("b");
	private static readonly FileStamp s_stampC = StoreCollaboratorsTestHost.StampOf("c");

	private static readonly TextFileFormat s_crlf = TestSnapshots.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.CrLf };

	private sealed class TestHost
	{
		internal TestHost()
		{
			Document = new LogicalDocument(
				SyncRoot,
				documentKey: new WorkspaceDocumentKey(Guid.NewGuid()),
				documentId: "doc",
				displayPath: "doc",
				content: "content",
				fileFormat: TestSnapshots.FileFormat,
				onDiskStamp: s_stampA,
				noBomEncodings: [TextEncoding.Utf8]
			);
		}

		internal object SyncRoot { get; } = new();

		internal LogicalDocument Document { get; }

		/// <summary>Runs a transition under the store lock, as the store does.</summary>
		internal void Locked(Action<LogicalDocument> transition)
		{
			lock (SyncRoot)
				transition(Document);
		}
	}

	[TestMethod]
	public void ReplaceContent_ChangesTheLogicalSideAndMovesTheVersionOn_LeavingTheBaseline()
	{
		var host = new TestHost();

		host.Locked(document => document.ReplaceContent("changed", s_crlf));

		LogicalDocument document = host.Document;

		Assert.AreEqual("changed", document.Content);
		Assert.AreEqual(s_crlf, document.FileFormat);
		Assert.AreEqual(1, document.Version);
		Assert.AreEqual(0, document.PersistedVersion);
		Assert.AreEqual("content", document.PersistedContent);
		Assert.AreEqual(TestSnapshots.FileFormat, document.PersistedFileFormat);
		Assert.AreEqual(s_stampA, document.OnDiskStamp);
		Assert.IsTrue(document.IsDirty);
	}

	[TestMethod]
	public void ReplaceContent_WithTheBaselineContentAndFormat_IsCleanAtTheNewVersion()
	{
		var host = new TestHost();
		host.Locked(document => document.ReplaceContent("changed", TestSnapshots.FileFormat));

		host.Locked(document => document.ReplaceContent(new string("content".AsSpan()), TestSnapshots.FileFormat));

		Assert.IsFalse(host.Document.IsDirty);
		Assert.AreEqual(2, host.Document.Version);
		Assert.AreEqual(0, host.Document.PersistedVersion);
	}

	[TestMethod]
	public void ReplaceContent_WithOnlyAnotherFormat_IsDirty()
	{
		var host = new TestHost();

		host.Locked(document => document.ReplaceContent("content", s_crlf));

		Assert.IsTrue(host.Document.IsDirty);
	}

	[TestMethod]
	public void DiscardChanges_RestoresTheBaselineAndTheVersionFollowsTheMovedVersion()
	{
		var host = new TestHost();
		host.Locked(document => document.ReplaceContent("changed", s_crlf));

		host.Locked(document => document.DiscardChanges());

		LogicalDocument document = host.Document;

		Assert.AreEqual("content", document.Content);
		Assert.AreEqual(TestSnapshots.FileFormat, document.FileFormat);
		Assert.AreEqual(2, document.Version);
		Assert.AreEqual(2, document.PersistedVersion);
		Assert.AreEqual(s_stampA, document.OnDiskStamp);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public void AdoptDiskContent_InstallsTheContentAsBothSidesAtANewVersion()
	{
		var host = new TestHost();
		host.Locked(document => document.ReplaceContent("edited", TestSnapshots.FileFormat));

		host.Locked(document => document.AdoptDiskContent("disk", s_crlf, s_stampB));

		LogicalDocument document = host.Document;

		Assert.AreEqual("disk", document.Content);
		Assert.AreEqual("disk", document.PersistedContent);
		Assert.AreEqual(s_crlf, document.FileFormat);
		Assert.AreEqual(s_crlf, document.PersistedFileFormat);
		Assert.AreEqual(2, document.Version);
		Assert.AreEqual(2, document.PersistedVersion);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public void InstallBaseline_RecordsTheWrittenContent_LeavingTheLogicalSideAndTheVersion()
	{
		var host = new TestHost();

		host.Locked(document => document.ReplaceContent("written", TestSnapshots.FileFormat));
		host.Locked(document => document.ReplaceContent("typed meanwhile", TestSnapshots.FileFormat));

		host.Locked(document =>
			document.InstallBaseline(content: "written", fileFormat: TestSnapshots.FileFormat, version: 1, onDiskStamp: s_stampB)
		);

		LogicalDocument document = host.Document;

		Assert.AreEqual("written", document.PersistedContent);
		Assert.AreEqual(1, document.PersistedVersion);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.AreEqual("typed meanwhile", document.Content);
		Assert.AreEqual(2, document.Version);
		Assert.IsTrue(document.IsDirty);
	}

	[TestMethod]
	public void InstallBaseline_OfTheCurrentContent_IsClean()
	{
		var host = new TestHost();
		host.Locked(document => document.ReplaceContent("written", s_crlf));

		host.Locked(document => document.InstallBaseline(content: "written", fileFormat: s_crlf, version: 1, onDiskStamp: s_stampB));

		Assert.IsFalse(host.Document.IsDirty);
		Assert.AreEqual(host.Document.Version, host.Document.PersistedVersion);
	}

	[TestMethod]
	public void InstallBaseline_OfANoBomEncodingTheDocumentDidNotDecodeWith_MakesItTheOnlyCandidate()
	{
		var host = new TestHost();
		TextFileFormat written = TestSnapshots.FileFormat with { Encoding = TextEncoding.Utf16LittleEndian, HasBom = false };

		host.Locked(document => document.InstallBaseline(content: "content", fileFormat: written, version: 0, onDiskStamp: s_stampB));

		Assert.AreSequenceEqual([TextEncoding.Utf16LittleEndian], host.Document.NoBomEncodings);
	}

	[TestMethod]
	public void InstallBaseline_OfTheEncodingTheDocumentDecodedWith_KeepsTheCandidates()
	{
		var host = new TestHost();

		host.Locked(document =>
			document.InstallBaseline(content: "content", fileFormat: TestSnapshots.FileFormat, version: 0, onDiskStamp: s_stampB)
		);

		Assert.AreSequenceEqual([TextEncoding.Utf8], host.Document.NoBomEncodings);
	}

	[TestMethod]
	public void InstallBaseline_OfAnEncodingWithABom_KeepsTheCandidates()
	{
		var host = new TestHost();
		TextFileFormat written = TestSnapshots.FileFormat with { Encoding = TextEncoding.Utf16LittleEndian, HasBom = true };

		host.Locked(document => document.InstallBaseline(content: "content", fileFormat: written, version: 0, onDiskStamp: s_stampB));

		Assert.AreSequenceEqual([TextEncoding.Utf8], host.Document.NoBomEncodings);
	}

	[TestMethod]
	public void RestrictNoBomEncodings_MakesTheEncodingTheOnlyCandidate_LeavingTheRest()
	{
		var host = new TestHost();

		host.Locked(document => document.RestrictNoBomEncodings(TextEncoding.Utf16LittleEndian));

		LogicalDocument document = host.Document;

		Assert.AreSequenceEqual([TextEncoding.Utf16LittleEndian], document.NoBomEncodings);
		Assert.AreEqual(0, document.Version);
		Assert.AreEqual("content", document.Content);
		Assert.AreEqual(s_stampA, document.OnDiskStamp);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public void Retarget_ChangesTheIdentityAndTheStampAndMovesTheVersionOn_LeavingTheContent()
	{
		var host = new TestHost();
		host.Locked(document => document.ReplaceContent("changed", TestSnapshots.FileFormat));
		WorkspaceDocumentKey key = host.Document.DocumentKey;

		host.Locked(document => document.Retarget("moved", "moved path", s_stampB));

		LogicalDocument document = host.Document;

		Assert.AreEqual("moved", document.DocumentId);
		Assert.AreEqual("moved path", document.DisplayPath);
		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.AreEqual(2, document.Version);
		Assert.AreEqual(0, document.PersistedVersion);
		Assert.AreEqual(key, document.DocumentKey);
		Assert.AreEqual("changed", document.Content);
		Assert.AreEqual("content", document.PersistedContent);
		Assert.IsTrue(document.IsDirty);
	}

	[TestMethod]
	public void ObserveOnDiskStamp_ChangesOnlyTheStamp()
	{
		var host = new TestHost();

		host.Locked(document => document.ObserveOnDiskStamp(s_stampB));

		LogicalDocument document = host.Document;

		Assert.AreEqual(s_stampB, document.OnDiskStamp);
		Assert.AreEqual(0, document.Version);
		Assert.AreEqual(0, document.PersistedVersion);
		Assert.AreEqual("content", document.Content);
		Assert.IsFalse(document.IsDirty);
	}

	[TestMethod]
	public void EveryTransition_WithoutTheStoreLock_ThrowsAndChangesNothing()
	{
		var host = new TestHost();
		LogicalDocument document = host.Document;

		var transitions = new (string Name, Action Run)[]
		{
			(nameof(LogicalDocument.ReplaceContent), () => document.ReplaceContent("changed", s_crlf)),
			(nameof(LogicalDocument.DiscardChanges), document.DiscardChanges),
			(nameof(LogicalDocument.AdoptDiskContent), () => document.AdoptDiskContent("disk", s_crlf, s_stampB)),

			(
				nameof(LogicalDocument.InstallBaseline),
				() => document.InstallBaseline(content: "written", fileFormat: s_crlf, version: 0, onDiskStamp: s_stampB)
			),

			(nameof(LogicalDocument.Retarget), () => document.Retarget("moved", "moved", s_stampB)),
			(nameof(LogicalDocument.ObserveOnDiskStamp), () => document.ObserveOnDiskStamp(s_stampB)),
			(nameof(LogicalDocument.RestrictNoBomEncodings), () => document.RestrictNoBomEncodings(TextEncoding.Utf16LittleEndian)),
		};

		foreach ((string name, Action run) in transitions)
		{
			Assert.ThrowsExactly<InvalidOperationException>(run, $"{name} must require the store lock.");

			Assert.AreEqual("doc", document.DocumentId, name);
			Assert.AreEqual("content", document.Content, name);
			Assert.AreEqual("content", document.PersistedContent, name);
			Assert.AreEqual(TestSnapshots.FileFormat, document.FileFormat, name);
			Assert.AreEqual(TestSnapshots.FileFormat, document.PersistedFileFormat, name);
			Assert.AreEqual(s_stampA, document.OnDiskStamp, name);
			Assert.AreEqual(0, document.Version, name);
			Assert.AreEqual(0, document.PersistedVersion, name);
			Assert.IsTrue(document.NoBomEncodings.SequenceEqual([TextEncoding.Utf8]), name);
		}
	}

	[TestMethod]
	public void Transitions_InAnyOrder_KeepTheVersionPairAndTheDirtyStateConsistent()
	{
		var host = new TestHost();
		var random = new Random(20260526);

		TextFileFormat[] formats = [TestSnapshots.FileFormat, s_crlf];
		FileStamp[] stamps = [s_stampA, s_stampB, s_stampC];

		for (int step = 0; step < 2000; step++)
		{
			LogicalDocument document = host.Document;

			long version = document.Version;
			long persistedVersion = document.PersistedVersion;

			bool movesTheVersion = true;
			string content = $"content {random.Next(3)}";
			TextFileFormat format = formats[random.Next(formats.Length)];
			FileStamp stamp = stamps[random.Next(stamps.Length)];

			switch (random.Next(6))
			{
				case 0:
					host.Locked(d => d.ReplaceContent(content, format));
					break;

				case 1:
					host.Locked(d => d.DiscardChanges());

					Assert.AreEqual(document.Version, document.PersistedVersion);
					Assert.IsFalse(document.IsDirty);

					break;

				case 2:
					host.Locked(d => d.AdoptDiskContent(content, format, stamp));

					Assert.AreEqual(document.Version, document.PersistedVersion);
					Assert.IsFalse(document.IsDirty);

					break;

				case 3:
					movesTheVersion = false;
					long written = random.NextInt64(persistedVersion, version + 1);
					host.Locked(d => d.InstallBaseline(content, fileFormat: format, version: written, onDiskStamp: stamp));
					Assert.AreEqual(written, document.PersistedVersion);
					break;

				case 4:
					host.Locked(d => d.Retarget($"doc {random.Next(3)}", "path", stamp));
					break;

				default:
					movesTheVersion = false;
					host.Locked(d => d.ObserveOnDiskStamp(stamp));
					break;
			}

			Assert.AreEqual(movesTheVersion ? version + 1 : version, document.Version, $"step {step}");
			Assert.IsLessThanOrEqualTo(document.Version, document.PersistedVersion, $"step {step}");

			bool dirty =
				document.FileFormat != document.PersistedFileFormat
				|| !string.Equals(document.Content, document.PersistedContent, StringComparison.Ordinal);

			Assert.AreEqual(dirty, document.IsDirty, $"step {step}");
		}
	}

	[TestMethod]
	public void TheStateOfADocument_HasNoSetterOutsideTheType()
	{
		PropertyInfo[] properties = typeof(LogicalDocument).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		Assert.IsNotEmpty(properties);

		foreach (PropertyInfo property in properties)
			Assert.IsTrue(property.SetMethod is null || property.SetMethod.IsPrivate, $"{property.Name} must change only through a transition.");
	}
}
