namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// The document text a <see cref="TextCodeActionContext"/> describes, materialized only when it is
/// first read.
/// </summary>
/// <remarks>
/// <para>
/// A code-action controller builds one context for every settled request. Carrying the document as a
/// plain string would copy the whole document on that cadence even when the host's request builder
/// vetoes the request without looking at the text; this carrier instead defers the copy until
/// <see cref="Text"/> is read, so a host that never reads the text never pays for it.
/// </para>
/// <para>
/// The carrier is immutable once created: the owner supplies the text length up front, which
/// <see cref="TextCodeActionContext"/> validates its offsets against, and a factory that produces the
/// text of the same length. The factory runs at most once, even when concurrent readers race the first
/// read; a factory that returns text of a different length violates the carrier's contract and makes
/// the read fail. Use <see cref="FromText"/> for text that is already materialized and
/// <see cref="FromFactory"/> for text that should be produced on demand.
/// </para>
/// </remarks>
public sealed class TextCodeActionDocumentText
{
	private readonly string? _text;
	private readonly Lazy<string>? _materializedText;

	private TextCodeActionDocumentText(int textLength, string? text, Func<string>? materialize)
	{
		TextLength = textLength;
		_text = text;

		// The lazy value publishes one materialization to every reader: when concurrent readers race
		// the first read, the factory must still run once, because two runs could publish different
		// strings and the declared length is validated only against the published one.
		if (materialize is not null)
		{
			_materializedText = new Lazy<string>(
				() => CreateCheckedText(textLength, materialize),
				LazyThreadSafetyMode.ExecutionAndPublication);
		}
	}

	/// <summary>
	/// Gets the number of UTF-16 code units in the document text.
	/// </summary>
	public int TextLength { get; }

	/// <summary>
	/// Gets the document text, materializing it on first read and caching it afterwards.
	/// </summary>
	/// <remarks>
	/// The first read invokes the factory, once even when concurrent readers race that read; later
	/// reads return the same instance. The value is the text captured when the carrier was created, so
	/// it does not observe later document edits.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// The factory returned <see langword="null"/> or produced text of a length other than the length
	/// the carrier was created for.
	/// </exception>
	public string Text => _text ?? _materializedText!.Value;

	/// <summary>
	/// Creates a carrier for text that is already materialized.
	/// </summary>
	/// <param name="text">The document text.</param>
	/// <returns>The carrier.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static TextCodeActionDocumentText FromText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		return new TextCodeActionDocumentText(text.Length, text, materialize: null);
	}

	/// <summary>
	/// Creates a carrier that materializes its text on first read.
	/// </summary>
	/// <param name="textLength">The length of the text the factory produces.</param>
	/// <param name="materialize">A factory that produces the document text.</param>
	/// <returns>The carrier.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="materialize"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="textLength"/> is negative.</exception>
	public static TextCodeActionDocumentText FromFactory(int textLength, Func<string> materialize)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(textLength);
		ArgumentNullException.ThrowIfNull(materialize);

		return new TextCodeActionDocumentText(textLength, text: null, materialize);
	}

	// The declared length is what a context validated its offsets against, so the produced text must
	// match it: publishing a different length would let a validated offset address another document. A
	// factory that returns null is the same contract violation.
	private static string CreateCheckedText(int textLength, Func<string> materialize)
	{
		string text = materialize();

		if (text is null)
			throw new InvalidOperationException("The factory returned null instead of document text.");

		return text.Length == textLength
			? text
			: throw new InvalidOperationException(
				$"The factory produced text of length {text.Length}, but the carrier was created for length {textLength}.");
	}

	/// <summary>
	/// Returns the document text.
	/// </summary>
	/// <returns>The document text.</returns>
	public override string ToString() => Text;
}
