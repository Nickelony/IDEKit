namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Reports the document offset under the pointer of the editor that a <see cref="HoverController"/> serves.</summary>
/// <remarks>
/// <para>
/// A binding implements the source over its text area, and a host with a pen, touch, or synthetic pointer implements it over its own input.
/// The controller calls <see cref="GetHoveredOffset"/> only on its dispatcher's thread, and it can dispose the source on any thread.
/// </para>
/// <para>
/// The controller asks again when a completed result is about to be shown, so an implementation resolves the pointer on every call instead of caching
/// an offset that a scroll or an edit can invalidate.
/// </para>
/// </remarks>
public interface IHoverPointerSource : IDisposable
{
	/// <summary>Resolves the document offset under the pointer.</summary>
	/// <returns>
	/// The zero-based offset, or <see langword="null"/> when the pointer left the editor, has not moved over it since the source was created,
	/// or addresses no document position.
	/// </returns>
	int? GetHoveredOffset();
}
