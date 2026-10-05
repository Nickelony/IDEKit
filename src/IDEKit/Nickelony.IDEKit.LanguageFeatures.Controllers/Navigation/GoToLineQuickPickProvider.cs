using System.Globalization;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Moves the caret of the active editor to a line, and a column, in the mode that the prefix <c>:</c> selects.</summary>
/// <remarks>
/// <para>
/// The text after the prefix is <c>line</c> or <c>line:column</c>, as <see cref="LineColumnQuery.TryParse"/> reads it. The mode lists one item
/// that names the position, clamped to the document, and previews it: the caret moves and the position scrolls into view while the user types,
/// and the view returns to where it was when the session ends without an accept. Text that is no position lists one dimmed item that tells
/// the position of the caret before the preview and the range of lines, and it moves nothing, so a preview that an earlier valid text started
/// is undone.
/// </para>
/// <para>
/// The mode asks <c>getTarget</c> on the dispatcher thread each time it lists, and keeps the target it previewed in until the preview ends,
/// so an editor that stops being the active one meanwhile still gets its view back.
/// </para>
/// </remarks>
public sealed class GoToLineQuickPickProvider : IQuickPickProvider
{
	private readonly Func<IEditorNavigationTarget?> _getTarget;
	private readonly GoToLineQuickPickOptions _options;

	private IEditorNavigationTarget? _previewed;
	private (int Line, int Column) _caretBeforePreview;

	/// <param name="getTarget">
	/// Gets the editor to move the caret in, or <see langword="null"/> when no editor is open. It runs on the dispatcher thread.
	/// </param>
	/// <param name="options">The options, or <see langword="null"/> to use the defaults.</param>
	/// <exception cref="ArgumentNullException"><paramref name="getTarget"/> is <see langword="null"/>.</exception>
	public GoToLineQuickPickProvider(Func<IEditorNavigationTarget?> getTarget, GoToLineQuickPickOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(getTarget);

		_getTarget = getTarget;
		_options = options ?? GoToLineQuickPickOptions.Default;
	}

	/// <inheritdoc/>
	public string Prefix => ":";

	/// <inheritdoc/>
	public string Name => _options.Name;

	/// <inheritdoc/>
	public string Placeholder => _options.Placeholder;

	/// <inheritdoc/>
	public QuickPickFilterMode FilterMode => QuickPickFilterMode.None;

	/// <inheritdoc/>
	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Task.FromResult<IReadOnlyList<QuickPickItem>>([ItemFor(_getTarget(), request.Query)]);
	}

	/// <inheritdoc/>
	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The accept moves the caret itself, so it does not depend on the preview having run.
		if (request.Item.Tag is GoToLinePlan plan && (_previewed ?? _getTarget()) is { } target)
			target.MoveCaret(plan.LineNumber, plan.Column);

		_previewed = null;

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public void Preview(QuickPickItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (item.Tag is not GoToLinePlan plan)
			return;

		if (_previewed is null)
		{
			if (_getTarget() is not { } target)
				return;

			_caretBeforePreview = (target.CaretLineNumber, target.CaretColumn);
			target.SaveView();
			_previewed = target;
		}

		_previewed.MoveCaret(plan.LineNumber, plan.Column);
	}

	/// <inheritdoc/>
	public void CancelPreview()
	{
		_previewed?.RestoreView();
		_previewed = null;
	}

	private QuickPickItem ItemFor(IEditorNavigationTarget? target, string query)
	{
		if (target is null)
			return Hint(_options.NoEditorText);

		if (GoToLinePlanner.Plan(query, target.LineCount, target.GetLineLength) is not { } plan)
		{
			// While a preview runs the caret has moved, so the hint tells where it stood before the preview.
			(int line, int column) = _previewed is null ? (target.CaretLineNumber, target.CaretColumn) : _caretBeforePreview;

			return Hint(
				string.Format(provider: CultureInfo.CurrentCulture, format: _options.HintText, arg0: line, arg1: column, arg2: target.LineCount)
			);
		}

		string label = plan.HasColumn
			? string.Format(provider: CultureInfo.CurrentCulture, format: _options.LineColumnText, arg0: plan.LineNumber, arg1: plan.Column)
			: string.Format(CultureInfo.CurrentCulture, _options.LineText, plan.LineNumber);

		return new QuickPickItem($"{plan.LineNumber}:{plan.Column}", label) { Tag = plan };
	}

	private static QuickPickItem Hint(string text) => new("hint", text) { IsEnabled = false };
}
