namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentVersionPolicyTests
{
	[TestMethod]
	public void TryAccept_RejectsOlderPositiveVersion()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 4, out int acceptedVersion);

		Assert.IsFalse(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_PreservesCurrentVersionForUnversionedPayload()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 0, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_AdvancesToNewerPositiveVersion()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 6, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(6, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_KeepsIdenticalVersion()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 5, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void IsPayloadCurrent_AcceptsUnknownOrMatchingPayloadVersionsOnly()
	{
		Assert.IsTrue(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 0));
		Assert.IsTrue(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 5));
		Assert.IsFalse(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 4));
		Assert.IsFalse(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 6));

		// The tracked version is always positive in production; an unknown tracked version does not
		// make an arbitrary payload acceptable.
		Assert.IsFalse(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 0, payloadVersion: 5));
		Assert.IsTrue(DocumentVersionPolicy.IsPayloadCurrent(documentVersion: 0, payloadVersion: 0));
	}

	[TestMethod]
	public void TryAccept_AdoptsPositiveVersionWhenCurrentVersionIsUnknown()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 0, incomingVersion: 5, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_TreatsNegativeIncomingVersionLikeAnUnknownVersion()
	{
		bool accepted = DocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: -1, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}
}
