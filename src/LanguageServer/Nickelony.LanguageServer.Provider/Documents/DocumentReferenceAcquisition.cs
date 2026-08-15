namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Selects which references a <see cref="TrackedDocumentStore.Synchronize"/> call acquires
/// for the document.
/// </summary>
/// <remarks>
/// <para>
/// The values compose, so one call can acquire an open-document reference, a temporary request-driven reference, or
/// both. <see cref="None"/> still mirrors content and may create an idle server-open record, but acquires no
/// reference that keeps the record tracked.
/// </para>
/// <para>
/// This is the reference-policy axis: it decides whether the record stays tracked. The outbound notification a
/// synchronization emits is <see cref="DocumentSynchronizationKind"/>; the result of an explicit close is
/// <see cref="DocumentCloseOutcome"/>.
/// </para>
/// </remarks>
[Flags]
public enum DocumentReferenceAcquisition
{
	/// <summary>Acquires no reference; the synchronization only mirrors content into the tracked state.</summary>
	None = 0,

	/// <summary>Acquires an open-document reference, which keeps the record tracked until it is released.</summary>
	Open = 1,

	/// <summary>Acquires a temporary request-driven reference, which keeps the record tracked until it is released.</summary>
	Request = 2
}
