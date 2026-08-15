namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Applies the shared normalization for optional text payload values.
/// </summary>
/// <remarks>
/// <para>
/// Optional display and identity text is trimmed and blank values are treated as absent, so payloads
/// and their consumers agree on one policy. Text with matching semantics that is compared verbatim
/// (for example completion filter text) deliberately does not use this helper.
/// </para>
/// <para>
/// This file is compiled into every package that needs it through a <c>&lt;Compile Include&gt;</c>
/// link. The <c>Nickelony.IDEKit.Infrastructure</c> namespace is intentionally shared by those
/// linked copies instead of following one project's folder-to-namespace convention, so the helper
/// keeps a single identity across packages; the same convention is used by
/// <c>NumericValidation</c>.
/// </para>
/// </remarks>
internal static class OptionalText
{
	/// <summary>
	/// Normalizes an optional text value.
	/// </summary>
	/// <param name="text">The optional text value.</param>
	/// <returns>The trimmed value, or <see langword="null"/> when the value is blank.</returns>
	internal static string? Normalize(string? text)
		=> string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
