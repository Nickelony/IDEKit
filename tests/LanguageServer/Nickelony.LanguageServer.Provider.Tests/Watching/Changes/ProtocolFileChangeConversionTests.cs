using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the mapping of a watched file change kind to the kind that the protocol names.</summary>
[TestClass]
public sealed class ProtocolFileChangeConversionTests
{
	[TestMethod]
	[DataRow(FileChangeKind.Created, FileChangeType.Created)]
	[DataRow(FileChangeKind.Changed, FileChangeType.Changed)]
	[DataRow(FileChangeKind.Deleted, FileChangeType.Deleted)]
	public void ToProtocol_MapsEachKindToItsProtocolCounterpart(FileChangeKind kind, FileChangeType expected) =>
		Assert.AreEqual(expected, ProtocolFileChangeConversion.ToProtocol(kind));

	[TestMethod]
	public void ToProtocol_ForAnUndefinedKind_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProtocolFileChangeConversion.ToProtocol((FileChangeKind)99));
}
