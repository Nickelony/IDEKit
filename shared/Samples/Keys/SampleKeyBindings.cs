using Nickelony.KeyBindings;
using KeyModifiers = Nickelony.KeyBindings.KeyModifiers;
#if AVALONIAEDIT
using Avalonia.Input;
using Nickelony.KeyBindings.Avalonia;
#else
using System.Windows.Input;
using Nickelony.KeyBindings.Wpf;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Maps strokes to the commands of the sample: one catalog and one overrides file serve two dispatchers, which a chain tries in order.
/// The editor tier answers while an editor has the focus, and the window tier answers anywhere for the commands that act on the window.
/// </summary>
/// <remarks>
/// The window passes every key press to <see cref="HandleKeyDown"/>, so the tabs can open and close without the bindings following them.
/// </remarks>
internal sealed class SampleKeyBindings
{
	private readonly CommandCatalog<string> _catalog;
	private readonly KeyBindingService<string> _service;
	private readonly DesktopKeyDisplayTextFormatter _formatter;
	private readonly KeyBindingDispatcherChain<string> _chain;

	/// <param name="commands">
	/// The commands of the sample, whose captions and menus give the catalog the titles and categories that the command palette lists.
	/// </param>
	/// <param name="overridesStore">The store that supplies the saved overrides and persists each change.</param>
	/// <param name="isEditorFocused">Tells whether the editor of the active pane has the keyboard focus.</param>
	/// <param name="canExecuteInEditor">Tells whether the focused editor may run a command.</param>
	/// <param name="runsAnywhere">Tells whether a command acts on the window, so its shortcut works wherever the focus is.</param>
	/// <param name="execute">Runs a command.</param>
	internal SampleKeyBindings(
		IReadOnlyList<SampleCommand> commands,
		IKeyBindingOverrideStore overridesStore,
		Func<bool> isEditorFocused,
		Func<string, bool> canExecuteInEditor,
		Func<string, bool> runsAnywhere,
		Action<string> execute
	)
	{
		KeyBindingPlatformProfile platform = KeyBindingPlatformProfile.ForCurrentPlatform;

		// WPF's gesture converter names the punctuation keys (OemPeriod), so both heads render the platform's labels instead.
		_formatter = new DesktopKeyDisplayTextFormatter(platform);

		_catalog = new CommandCatalog<string>(CreateDescriptors(platform, commands));
		_service = new KeyBindingService<string>(_catalog, overridesStore, new KeyBindingServiceOptions { DisplayTextFormatter = _formatter });

		// A tier consumes a stroke only for the commands its predicate accepts, which the dispatcher also asks for the first stroke of a chord.
		_chain = new KeyBindingDispatcherChain<string>([
			new KeyBindingDispatcher<string>(
				_service,
				new KeyBindingDispatcherHooks<string>(commandId => isEditorFocused() && canExecuteInEditor(commandId), execute)
			),

			new KeyBindingDispatcher<string>(_service, new KeyBindingDispatcherHooks<string>(runsAnywhere, execute)),
		]);
	}

#if AVALONIAEDIT
	/// <summary>The gate that ignores the repeat of a key still held, because Avalonia reports no auto-repeat flag. The window sets it.</summary>
	internal KeyRepeatGate? RepeatGate { get; set; }
#endif

	/// <summary>Raised after a rebinding or a reset changed what a command shows as its shortcut.</summary>
	internal event EventHandler<BindingsChangedEventArgs<string>>? BindingsChanged
	{
		add => _service.BindingsChanged += value;
		remove => _service.BindingsChanged -= value;
	}

	/// <summary>The commands of the sample with their titles, categories and default bindings.</summary>
	internal IReadOnlyList<CommandDescriptor<string>> Descriptors => _catalog.Descriptors;

	/// <summary>Dispatches a stroke the way a key press in the window does.</summary>
	internal KeyStrokeDispatchOutcome Dispatch(KeyStroke keyStroke) => _chain.Dispatch(keyStroke, context: null);

	/// <summary>Dispatches a key press of the window and reports whether a command took it.</summary>
	internal bool HandleKeyDown(KeyEventArgs e)
	{
		// Escape abandons a chord in progress; the package never times one out.
		if (e.Key == Key.Escape && _chain.HasPendingChord)
		{
			CancelPendingChords();
			return true;
		}

#if AVALONIAEDIT
		return _chain.HandleKeyDown(e, context: null, RepeatGate) != KeyStrokeDispatchOutcome.NotHandled;
#else
		return _chain.HandleKeyDown(e, context: null) != KeyStrokeDispatchOutcome.NotHandled;
#endif
	}

	/// <summary>Abandons a chord in progress, as the window does when it loses the focus.</summary>
	internal void CancelPendingChords() => _chain.CancelPendingChord();

	/// <summary>The shortcut of a command as display text, or an empty string when it has none.</summary>
	internal string GetDisplayText(string commandId) => _service.GetDisplayText(commandId);

	/// <summary>The display text of one stroke, such as a stroke the key bindings window recorded.</summary>
	internal string GetDisplayText(KeyStroke keyStroke) => _formatter.GetDisplayText(keyStroke);

	/// <summary>Binds a command to the typed shortcut, and describes what happened.</summary>
	/// <param name="commandId">The identifier of a command in <see cref="SampleCommandIds"/>.</param>
	/// <param name="shortcutText">The shortcut as typed, for example <c>Control+Shift+C</c>.</param>
	/// <param name="takeOver">Whether the shortcut may take a chord over from another command, which the user confirmed.</param>
	internal string Rebind(string commandId, string shortcutText, bool takeOver = false) =>
		KeyChord.TryParse(shortcutText, out KeyChord chord) ? Rebind(commandId, chord, takeOver) : $"'{shortcutText}' is not a valid shortcut";

	/// <summary>Binds a command to a chord, such as one recorded from a key press, and describes what happened.</summary>
	/// <param name="commandId">The identifier of a command in <see cref="SampleCommandIds"/>.</param>
	/// <param name="chord">The chord the command takes in place of its bindings.</param>
	/// <param name="takeOver">Whether the chord may be taken over from another command, which the user confirmed.</param>
	internal string Rebind(string commandId, KeyChord chord, bool takeOver = false)
	{
		// Validation first reports a conflict without changing anything, so a host can ask before it takes a chord over.
		KeyBindingConflictPolicy policy = takeOver ? KeyBindingConflictPolicy.Replace : KeyBindingConflictPolicy.Reject;
		KeyBindingOutcome outcome = _service.Validate(commandId, [chord], policy);

		if (outcome == KeyBindingOutcome.Succeeded)
			outcome = _service.Apply(commandId, [chord], policy);

		return outcome == KeyBindingOutcome.Succeeded
			? $"{commandId} is bound to {_service.GetDisplayText(commandId)}"
			: $"The shortcut was not applied: {outcome}";
	}

	/// <summary>Gives a command its default bindings again, and describes what happened.</summary>
	internal string Reset(string commandId) => Describe(_service.Reset(commandId), $"{commandId} has its default shortcut again");

	/// <summary>Gives every command its default bindings again, and describes what happened.</summary>
	internal string ResetAll() => Describe(_service.ResetAll(), "Every command has its default shortcut again");

	private static string Describe(KeyBindingOutcome outcome, string success) =>
		outcome == KeyBindingOutcome.Succeeded ? success : $"The shortcuts were not reset: {outcome}";

	private static IReadOnlyList<CommandDescriptor<string>> CreateDescriptors(
		KeyBindingPlatformProfile platform,
		IReadOnlyList<SampleCommand> commands
	)
	{
		// The menu text marks the access key of an item with an underscore, which a title and a category do without.
		static string Plain(string menuText) => menuText.Replace("_", string.Empty, StringComparison.Ordinal);

		CommandDescriptor<string> Descriptor(string commandId, params KeyChord[] defaultBindings)
		{
			SampleCommand command = commands.First(candidate => candidate.Id == commandId);

			return new(commandId, commandId, CommandRemappingPolicy.Remappable, defaultBindings)
			{
				Title = Plain(command.Caption),
				Category = Plain(command.Menu),
			};
		}

		// A command without a default binding is still in the catalog, so the key bindings window can give it one.
		return
		[
			Descriptor(SampleCommandIds.NewFile, platform.PrimaryStroke(KeyCode.N)),
			Descriptor(SampleCommandIds.NewFolder),
			Descriptor(SampleCommandIds.Save, platform.PrimaryStroke(KeyCode.S)),
			Descriptor(SampleCommandIds.SaveAll, platform.PrimaryStroke(KeyCode.S, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.Revert),
			Descriptor(SampleCommandIds.CloseTab, platform.PrimaryStroke(KeyCode.W)),
			Descriptor(SampleCommandIds.Complete, platform.PrimaryStroke(KeyCode.Space)),
			Descriptor(SampleCommandIds.CodeActions, platform.PrimaryStroke(KeyCode.Period)),
			Descriptor(SampleCommandIds.ToggleComment, platform.PrimaryStroke(KeyCode.Slash)),
			Descriptor(SampleCommandIds.TrimWhitespace, platform.PrimaryStroke(KeyCode.W, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.Format, platform.PrimaryStroke(KeyCode.F, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.ConvertIndentationToSpaces),
			Descriptor(SampleCommandIds.ConvertIndentationToTabs),
			Descriptor(SampleCommandIds.ReindentSpaces),
			Descriptor(SampleCommandIds.Rename, new KeyStroke(KeyCode.F6, KeyModifiers.None)),
			Descriptor(SampleCommandIds.ToggleBookmark, new KeyStroke(KeyCode.F2, KeyModifiers.None)),
			Descriptor(SampleCommandIds.FindAndReplace, platform.PrimaryStroke(KeyCode.F), platform.PrimaryStroke(KeyCode.H)),
			Descriptor(SampleCommandIds.FindNext, new KeyStroke(KeyCode.F3, KeyModifiers.None)),
			Descriptor(SampleCommandIds.FindPrevious, new KeyStroke(KeyCode.F3, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.FindAll),
			Descriptor(SampleCommandIds.FindInFiles, platform.PrimaryStroke(KeyCode.F, KeyModifiers.Alt)),
			Descriptor(SampleCommandIds.ReplaceAll),
			Descriptor(SampleCommandIds.QuickOpen, platform.PrimaryStroke(KeyCode.P)),
			Descriptor(SampleCommandIds.GoToLine, platform.PrimaryStroke(KeyCode.G)),
			Descriptor(SampleCommandIds.GoToSymbol, platform.PrimaryStroke(KeyCode.O, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.GoToWorkspaceSymbol, platform.PrimaryStroke(KeyCode.T)),
			Descriptor(SampleCommandIds.GoToDefinition, new KeyStroke(KeyCode.F12, KeyModifiers.None)),
			Descriptor(SampleCommandIds.FindReferences, new KeyStroke(KeyCode.F12, KeyModifiers.Shift)),
			Descriptor(SampleCommandIds.NextProblem, new KeyStroke(KeyCode.F8, KeyModifiers.None)),
			Descriptor(SampleCommandIds.Split, platform.PrimaryStroke(KeyCode.Backslash)),
			Descriptor(SampleCommandIds.Preview, new KeyChord(platform.PrimaryStroke(KeyCode.K), new KeyStroke(KeyCode.V, KeyModifiers.None))),
			Descriptor(SampleCommandIds.ToggleTheme, new KeyChord(platform.PrimaryStroke(KeyCode.K), platform.PrimaryStroke(KeyCode.T))),

			Descriptor(
				SampleCommandIds.CommandPalette,
				platform.PrimaryStroke(KeyCode.P, KeyModifiers.Shift),
				new KeyStroke(KeyCode.F1, KeyModifiers.None)
			),

			Descriptor(SampleCommandIds.KeyBindings),
			Descriptor(SampleCommandIds.StartLuaServer),
			Descriptor(SampleCommandIds.UseInProcessLua),
			Descriptor(SampleCommandIds.StartRoslynServer),
			Descriptor(SampleCommandIds.StopRoslynServer),
			Descriptor(SampleCommandIds.Run, new KeyStroke(KeyCode.F5, KeyModifiers.None)),
		];
	}
}
