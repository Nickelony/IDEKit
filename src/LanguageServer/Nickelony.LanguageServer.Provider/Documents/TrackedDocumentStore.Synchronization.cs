using Nickelony.IDEKit.Core.Editing;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class TrackedDocumentStore
{
	/// <summary>
	/// Synchronizes the tracked state for a document and returns the LSP action required to mirror it to the server.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="content">The latest document content.</param>
	/// <param name="references">The references the synchronization acquires for the document.</param>
	/// <param name="includeChangeRange">Whether content changes should compute an incremental change range.
	/// Callers that negotiated full synchronization can pass <see langword="false"/> to skip the diff computation.</param>
	/// <param name="requestReference">
	/// The reference object that receives the acquired request reference, or <see langword="null"/> when the caller
	/// does not need to release the reference by identity. The release contract lives on
	/// <see cref="DocumentRequestReference"/>; pass a fresh instance, and only together with
	/// <see cref="DocumentReferenceAcquisition.Request"/> in <paramref name="references"/>.
	/// </param>
	/// <returns>A synchronization request when the server copy must be updated; otherwise, <see langword="null"/>.</returns>
	/// <remarks>
	/// <para>
	/// Synchronization can create an idle server-open record when neither reference option is selected. The record is
	/// retained until explicitly closed or removed by trimming.
	/// </para>
	/// <para>
	/// Content and version are committed when the request is returned, before the caller sends the returned
	/// notification. A caller that fails to deliver the notification must invalidate the tracked state (for example by
	/// closing the document) so the next synchronization emits a full open instead of computing incremental ranges
	/// against content the server never received.
	/// </para>
	/// <para>
	/// Calls for the same document path must be serialized by the caller (for example through
	/// <see cref="DocumentOperationScheduler"/>), because each returned change range is computed against the content
	/// committed by the previous call. Delivering requests out of order, or synchronizing concurrently, produces
	/// incremental ranges for content the server never received; the document version on the returned request lets a
	/// caller detect reordering.
	/// </para>
	/// <para>
	/// A call that finds the content unchanged returns <see langword="null"/> but still records the requested
	/// open/request references and refreshes the access stamp, so a caller that treats <see langword="null"/> as
	/// "nothing happened" must still account for the references it asked for.
	/// </para>
	/// <para>
	/// A fault raised by a derived store's synchronization overrides propagates to the caller instead of being
	/// contained: those overrides produce or update the tracked record the caller mirrors, so a contained fault
	/// could not report a truthful outcome. The store's key invariant (a dictionary entry key always equals its
	/// record's current path) still holds when an override throws, so the record stays reachable and the caller's
	/// next synchronization can retry. This is separate from the deliberate contract exceptions the store raises
	/// for misuse, such as an already-bound request reference.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, the path is invalid on the current platform, or
	/// <paramref name="requestReference"/> is supplied without <see cref="DocumentReferenceAcquisition.Request"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException"><paramref name="requestReference"/> was already bound or released.</exception>
	public DocumentSynchronizationRequest? Synchronize(
		string filePath,
		string content,
		DocumentReferenceAcquisition references = DocumentReferenceAcquisition.None,
		bool includeChangeRange = true,
		DocumentRequestReference? requestReference = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		return SynchronizeNormalized(
			LanguageServerPaths.NormalizeLocalPath(filePath),
			content,
			references,
			includeChangeRange,
			requestReference);
	}

	/// <summary>
	/// Synchronizes the tracked state for a document whose path the caller has already normalized.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <param name="content">The latest document content.</param>
	/// <param name="references">The references the synchronization acquires for the document.</param>
	/// <param name="includeChangeRange">Whether content changes should compute an incremental change range.</param>
	/// <param name="requestReference">The reference object that receives the acquired request reference, or <see langword="null"/>.</param>
	/// <returns>A synchronization request when the server copy must be updated; otherwise, <see langword="null"/>.</returns>
	/// <remarks>
	/// This is the fast path for a caller that already holds a normalized path (the provider normalizes each document
	/// path once as it enters the request pipeline and threads that value through the per-document scheduler); it skips
	/// <see cref="LanguageServerPaths.NormalizeLocalPath(string)"/>. The behavior is otherwise identical to
	/// <see cref="Synchronize(string, string, DocumentReferenceAcquisition, bool, DocumentRequestReference?)"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="normalizedFilePath"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="normalizedFilePath"/> is empty or whitespace-only, or <paramref name="requestReference"/> is
	/// supplied without <see cref="DocumentReferenceAcquisition.Request"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException"><paramref name="requestReference"/> was already bound or released.</exception>
	internal DocumentSynchronizationRequest? SynchronizeNormalized(
		string normalizedFilePath,
		string content,
		DocumentReferenceAcquisition references,
		bool includeChangeRange,
		DocumentRequestReference? requestReference)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(normalizedFilePath);
		ArgumentNullException.ThrowIfNull(content);

		// The flags are translated once here; the rest of the method only needs the two acquisition decisions.
		bool acquireOpenReference = (references & DocumentReferenceAcquisition.Open) != 0;
		bool acquireRequestReference = (references & DocumentReferenceAcquisition.Request) != 0;

		if (!acquireRequestReference && requestReference is not null)
		{
			throw new ArgumentException(
				"A request reference can only be supplied together with the DocumentReferenceAcquisition.Request flag.",
				nameof(requestReference));
		}


		// Populated on the incremental-change path and consumed after the store lock is released; every other path
		// returns from inside the lock, so both locals are definitely assigned below.
		string previousContent;
		DocumentSnapshot changeSnapshot;

		lock (_syncRoot)
		{
			if (acquireRequestReference && requestReference is not null && !requestReference.CanBind)
			{
				throw new InvalidOperationException(
					"The supplied document request reference was already bound or released; document request references are single-use.");
			}

			if (!_documents.TryGetValue(normalizedFilePath, out TrackedDocumentState? state))
			{
				state = CreateTrackedDocumentState(new TrackedDocumentInitialState(
					normalizedFilePath,
					LanguageServerPaths.CreateFileUri(normalizedFilePath),
					content,
					Version: 1,
					IsOpen: true,
					OpenReferenceCount: acquireOpenReference ? 1 : 0,
					RequestReferenceCount: acquireRequestReference ? 1 : 0,
					LastAccessStamp: GetNextAccessStamp()));

				if (acquireRequestReference)
					requestReference?.Bind(state);

				_documents[normalizedFilePath] = state;
				return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
			}

			if (acquireOpenReference)
				state.References.AcquireOpen();

			if (acquireRequestReference)
			{
				state.References.AcquireRequest();
				requestReference?.Bind(state);
			}

			TouchTrackedDocumentState(state, GetNextAccessStamp());

			if (!state.IsOpen)
			{
				ReopenTrackedDocumentState(state, content);
				return new(DocumentSynchronizationKind.Open, state.CreateSnapshot());
			}

			if (!string.Equals(state.Content, content, StringComparison.Ordinal))
			{
				previousContent = ReplaceTrackedDocumentContent(state, content);
				changeSnapshot = state.CreateSnapshot();

				if (!includeChangeRange)
					return new(DocumentSynchronizationKind.Change, changeSnapshot);
			}
			else
			{
				return null;
			}
		}

		// The incremental change range is computed outside the store lock: both inputs are immutable strings and the
		// computation reads no store state, so the lock only covers the state mutation above.
		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(previousContent, content);
		((int startLine, int startCharacter), (int endLine, int endCharacter)) =
			GetLineCharacters(previousContent, change.Range.Offset, change.Range.EndOffset);

		return new(
			DocumentSynchronizationKind.Change,
			changeSnapshot,
			new DocumentChangeRange(
				startLine,
				startCharacter,
				endLine,
				endCharacter,
				change.NewText));
	}

	/// <summary>
	/// Converts two UTF-16 document offsets into zero-based line and character coordinates with one forward pass.
	/// </summary>
	/// <remarks>
	/// Line terminators match <c>TextLineMap</c> semantics (LF, CRLF, and lone CR); an offset inside a CRLF terminator
	/// maps to the end of the preceding line.
	/// </remarks>
	/// <param name="content">The document text the offsets belong to.</param>
	/// <param name="startOffset">The zero-based UTF-16 start offset; values outside the document are clamped.</param>
	/// <param name="endOffset">The zero-based UTF-16 end offset; values outside the document are clamped.</param>
	/// <returns>The zero-based line and UTF-16 character of the start and end offsets.</returns>
	private static ((int Line, int Character) Start, (int Line, int Character) End) GetLineCharacters(
		string content,
		int startOffset,
		int endOffset)
	{
		int safeStartOffset = Math.Clamp(startOffset, 0, content.Length);
		int safeEndOffset = Math.Clamp(endOffset, 0, content.Length);

		(int line, int lineStart, int resumeOffset) = FindLinePosition(content, safeStartOffset);
		int startCharacter = Math.Max(0, resumeOffset - lineStart);

		if (safeEndOffset == safeStartOffset)
			return ((line, startCharacter), (line, startCharacter));

		int i = resumeOffset;

		while (i < safeEndOffset)
		{
			char current = content[i];

			if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
			{
				if (i + 2 > safeEndOffset)
				{
					// The end offset identifies the LF half of a CRLF terminator and maps to the preceding line's end.
					return ((line, startCharacter), (line, Math.Max(0, i - lineStart)));
				}

				i += 2;
				line++;
				lineStart = i;
				continue;
			}

			if (current is '\r' or '\n')
			{
				i++;
				line++;
				lineStart = i;
				continue;
			}

			i++;
		}

		return ((line, startCharacter), (line, Math.Max(0, safeEndOffset - lineStart)));
	}

	/// <summary>
	/// Finds the zero-based line and line-start offset for one document offset and reports the scan position a later
	/// offset can resume from without rescanning the prefix.
	/// </summary>
	/// <remarks>
	/// An offset inside a CRLF terminator stops the scan on the CR without consuming it, so the character is measured
	/// from the CR and the resume position points at it; the resumed scan then observes the terminator as a pair.
	/// </remarks>
	/// <param name="content">The document text the offset belongs to.</param>
	/// <param name="offset">The zero-based UTF-16 document offset; values outside the document are clamped.</param>
	/// <returns>The line, the line-start offset, and the scan position a later offset can resume from.</returns>
	private static (int Line, int LineStart, int ResumeOffset) FindLinePosition(string content, int offset)
	{
		int line = 0;
		int lineStart = 0;
		int i = 0;

		while (i < offset)
		{
			char current = content[i];

			if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
			{
				if (i + 2 > offset)
					return (line, lineStart, i);

				i += 2;
				line++;
				lineStart = i;
				continue;
			}

			if (current is '\r' or '\n')
			{
				i++;
				line++;
				lineStart = i;
				continue;
			}

			i++;
		}

		return (line, lineStart, offset);
	}
}
