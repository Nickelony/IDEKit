namespace Nickelony.IDEKit.Core.Text;

/// <summary>Applies the shared normalization for optional text payload values.</summary>
/// <remarks>
/// <para>
/// Optional display and identity text is trimmed and blank values are treated as absent, so payloads and their consumers agree on one policy.
/// Text with matching semantics that is compared verbatim deliberately does not use this helper.
/// </para>
/// </remarks>
public static class OptionalTextNormalizer
{
	/// <summary>Normalizes an optional text value.</summary>
	/// <param name="text">The optional text value.</param>
	/// <returns>The trimmed value, or <see langword="null"/> when the value is blank.</returns>
	public static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
