using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Lists the open editor tabs of the host in the mode that the prefix <c>edt </c> selects, and activates the tab the user picks.</summary>
/// <remarks>
/// <para>
/// The host supplies the tabs in the order it wants them listed, such as the order of its tab strip or the most recently used first, so the list
/// of an empty query is that order. A query filters the tabs by title and by folder, as the file mode does. A row shows the folder of the document
/// below its workspace root, as <see cref="OpenEditorsQuickPickOptions.RootPaths"/> says. A tab whose document has unsaved changes shows
/// <see cref="OpenEditorsQuickPickOptions.UnsavedText"/> at the right edge and carries <see cref="OpenEditorDescriptor.UnsavedIconKey"/>.
/// </para>
/// <para>
/// Both callbacks run on the dispatcher thread, and the controller calls <see cref="AcceptAsync"/> one call at a time, so a host callback
/// is never entered twice at once.
/// </para>
/// </remarks>
public sealed class OpenEditorsQuickPickProvider : IQuickPickProvider
{
	private readonly Func<IReadOnlyList<OpenEditorDescriptor>> _listOpenEditors;
	private readonly Action<OpenEditorDescriptor> _activate;
	private readonly OpenEditorsQuickPickOptions _options;

	/// <param name="listOpenEditors">Lists the open tabs in the order the quick pick shows them. It runs on the dispatcher thread.</param>
	/// <param name="activate">Makes the tab of a descriptor the active one. It runs on the dispatcher thread.</param>
	/// <param name="options">The options, or <see langword="null"/> to use the defaults.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="listOpenEditors"/> or <paramref name="activate"/> is <see langword="null"/>.
	/// </exception>
	public OpenEditorsQuickPickProvider(
		Func<IReadOnlyList<OpenEditorDescriptor>> listOpenEditors,
		Action<OpenEditorDescriptor> activate,
		OpenEditorsQuickPickOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(listOpenEditors);
		ArgumentNullException.ThrowIfNull(activate);

		_listOpenEditors = listOpenEditors;
		_activate = activate;
		_options = options ?? OpenEditorsQuickPickOptions.Default;
	}

	/// <inheritdoc/>
	public string Prefix => "edt ";

	/// <inheritdoc/>
	public string Name => _options.Name;

	/// <inheritdoc/>
	public string Placeholder => _options.Placeholder;

	/// <inheritdoc/>
	public QuickPickFilterMode FilterMode => QuickPickFilterMode.Path;

	/// <inheritdoc/>
	public string EmptyText => _options.EmptyText;

	/// <inheritdoc/>
	public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		IReadOnlyList<OpenEditorDescriptor> editors = _listOpenEditors();
		Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
		List<QuickPickItem> items = new(editors.Count);

		foreach (OpenEditorDescriptor editor in editors)
		{
			if (editor is null)
				continue;

			// Two tabs of one document would share a key, which the controller uses to keep the active item.
			occurrences.TryGetValue(editor.DocumentId, out int occurrence);
			occurrences[editor.DocumentId] = occurrence + 1;

			items.Add(
				new QuickPickItem(occurrence == 0 ? editor.DocumentId : $"{editor.DocumentId}#{occurrence}", editor.Title)
				{
					Description = DescribeFolder(editor.DocumentId),
					TrailingText = editor.IsUnsaved ? _options.UnsavedText : null,
					IconKey = editor.IsUnsaved ? OpenEditorDescriptor.UnsavedIconKey : null,
					Tag = editor,
				}
			);
		}

		return Task.FromResult<IReadOnlyList<QuickPickItem>>(items);
	}

	/// <inheritdoc/>
	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Item.Tag is OpenEditorDescriptor editor)
			_activate(editor);

		return Task.CompletedTask;
	}

	private string? DescribeFolder(string documentId)
	{
		string folder = Path.GetDirectoryName(documentId) ?? string.Empty;

		if (folder.Length == 0)
			return null;

		return LocalPath.TryNormalize(folder, out string normalized)
			? LocalPath.DescribeFolder(_options.RootPaths, normalized, _options.PathComparison)
			: folder;
	}
}
