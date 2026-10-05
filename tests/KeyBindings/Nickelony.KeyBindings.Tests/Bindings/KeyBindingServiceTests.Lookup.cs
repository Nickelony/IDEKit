using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>Covers the read surface: resolution, listings, display text, and prefix probes.</summary>
public partial class KeyBindingServiceTests
{
	[TestMethod]
	public void TryGetCommand_KnownStrokes_ReturnsCommands()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.S, KeyModifiers.Control), null, out TestCommand saveCommand));
		Assert.AreEqual(TestCommand.Save, saveCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.F9, KeyModifiers.None), null, out TestCommand buildCommand));
		Assert.AreEqual(TestCommand.Build, buildCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.F4, KeyModifiers.Alt), null, out TestCommand exitCommand));
		Assert.AreEqual(TestCommand.Exit, exitCommand);
	}

	[TestMethod]
	public void TryGetCommand_DifferentModifiers_ReturnsFalse()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsFalse(service.TryGetCommand(new KeyStroke(KeyCode.S, KeyModifiers.None), null, out _));
		Assert.IsFalse(service.TryGetCommand(new KeyStroke(KeyCode.S, KeyModifiers.Control | KeyModifiers.Alt), null, out _));
		Assert.IsFalse(service.TryGetCommand(new KeyStroke(KeyCode.A, KeyModifiers.None), null, out _));
	}

	[TestMethod]
	public void TryGetCommand_SecondaryBinding_Resolves()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.H, KeyModifiers.Control), null, out TestCommand findCommand));
		Assert.AreEqual(TestCommand.Find, findCommand);
	}

	[TestMethod]
	public void GetBindings_UnboundOrUncatalogedCommand_ReturnsEmpty()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save)));

		Assert.IsEmpty(service.GetBindings(TestCommand.Save));
		Assert.IsEmpty(service.GetBindings(TestCommand.None));
	}

	/// <summary>
	/// Pins that a command's stored order is the order of its override entry: the entry declares two bindings, and the lookup returns them exactly
	/// as declared.
	/// </summary>
	[TestMethod]
	public void GetBindings_OverrideWithTwoBindings_ReturnsTheEntryOrder()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("X", (int)KeyModifiers.Control), ("Q", (int)KeyModifiers.None))
		);

		IReadOnlyList<KeyChord> bindings = service.GetBindings(TestCommand.Save);

		Assert.HasCount(2, bindings);
		Assert.AreEqual(new KeyStroke(KeyCode.X, KeyModifiers.Control), bindings[0]);
		Assert.AreEqual(new KeyStroke(KeyCode.Q, KeyModifiers.None), bindings[1]);
	}

	[TestMethod]
	public void GetDisplayText_ReturnsExactTexts()
	{
		KeyBindingService<TestCommand> service = CreateService(CreateOverrides(nameof(TestCommand.Save)));

		Assert.AreEqual("Control+Z", service.GetDisplayText(TestCommand.Undo));
		Assert.AreEqual("Control+F / Control+H", service.GetDisplayText(TestCommand.Find));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.Save, "Fallback"));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.None, "Fallback"));
		Assert.AreEqual(string.Empty, service.GetDisplayText(TestCommand.None));
	}

	[TestMethod]
	public void GetDisplayText_UsesConfiguredFormatter()
	{
		KeyBindingService<TestCommand> service = CreateService(displayTextFormatter: new BracketFormatter());
		Assert.AreEqual("[F] / [H]", service.GetDisplayText(TestCommand.Find));
	}

	/// <summary>
	/// Pins that the precomputed display text is rebuilt by every mutation: a text that only reflected the construction-time bindings would keep
	/// showing the old chord after an <c>Apply</c> and would not return to the default after a <c>Reset</c>.
	/// </summary>
	[TestMethod]
	public void GetDisplayText_AfterApplyAndReset_TracksTheCurrentBindings()
	{
		KeyBindingService<TestCommand> service = CreateService();

		Assert.AreEqual("Control+S", service.GetDisplayText(TestCommand.Save));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Apply(TestCommand.Save, [new KeyStroke(KeyCode.X, KeyModifiers.Control)]));
		Assert.AreEqual("Control+X", service.GetDisplayText(TestCommand.Save));

		Assert.AreEqual(KeyBindingOutcome.Succeeded, service.Reset(TestCommand.Save));
		Assert.AreEqual("Control+S", service.GetDisplayText(TestCommand.Save));
	}

	[TestMethod]
	public void TryGetCommand_ByChord_ReturnsTheOwningCommand()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(
			service.TryGetCommand(
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
				null,
				out TestCommand saveAll
			)
		);

		Assert.AreEqual(TestCommand.SaveAll, saveAll);

		// A single stroke is a one-stroke chord.
		Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.S, KeyModifiers.Control), null, out TestCommand save));
		Assert.AreEqual(TestCommand.Save, save);

		// The prefix both chords share is bound to nothing.
		Assert.IsFalse(service.TryGetCommand(new KeyStroke(KeyCode.K, KeyModifiers.Control), null, out _));
	}

	[TestMethod]
	public void IsChordPrefix_ReportsUnboundPrefixesOnly()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(service.IsChordPrefix(new KeyStroke(KeyCode.K, KeyModifiers.Control), null, _ => true));

		Assert.IsFalse(
			service.IsChordPrefix(
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
				null,
				_ => true
			)
		);

		Assert.IsFalse(service.IsChordPrefix(new KeyStroke(KeyCode.S, KeyModifiers.Control), null, _ => true));
	}

	[TestMethod]
	public void IsChordPrefix_PredicateRejectsEveryCommandUnderThePrefix_ReportsFalse()
	{
		KeyBindingService<TestCommand> service = CreateChordService();
		var asked = new List<TestCommand>();

		bool result = service.IsChordPrefix(
			new KeyStroke(KeyCode.K, KeyModifiers.Control),
			null,
			command =>
			{
				asked.Add(command);
				return false;
			}
		);

		// Both chords that start with Control+K belong to a command, and the predicate saw each command once.
		Assert.IsFalse(result);
		Assert.HasCount(2, asked);
		Assert.Contains(TestCommand.SaveAll, asked);
		Assert.Contains(TestCommand.Find, asked);
	}

	[TestMethod]
	public void IsChordPrefix_PredicateAcceptsOneCommandUnderThePrefix_ReportsTrue()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.IsTrue(service.IsChordPrefix(new KeyStroke(KeyCode.K, KeyModifiers.Control), null, command => command == TestCommand.Find));
	}

	[TestMethod]
	public void IsChordPrefix_PredicateAcceptsACommand_StopsAskingAtThatCommand()
	{
		KeyBindingService<TestCommand> service = CreateChordService();
		int calls = 0;

		bool result = service.IsChordPrefix(
			new KeyStroke(KeyCode.K, KeyModifiers.Control),
			null,
			_ =>
			{
				calls++;
				return true;
			}
		);

		Assert.IsTrue(result);
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void IsChordPrefix_CommandWithSeveralChordsUnderOnePrefix_IsOfferedToThePredicateOnce()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.SaveAll,
				nameof(TestCommand.SaveAll),
				CommandRemappingPolicy.Remappable,
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
				new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.A, KeyModifiers.Control))
			),
		]);

		KeyBindingService<TestCommand> service = CreateService(catalog: catalog);
		var asked = new List<TestCommand>();

		service.IsChordPrefix(
			new KeyStroke(KeyCode.K, KeyModifiers.Control),
			null,
			command =>
			{
				asked.Add(command);
				return false;
			}
		);

		Assert.HasCount(1, asked);
	}

	[TestMethod]
	public void IsChordPrefix_PredicateSeesOnlyTheCommandsOfTheContextAndTheAlwaysActiveOnes()
	{
		KeyBindingService<TestCommand> service = CreateModeChordService();
		var asked = new List<TestCommand>();

		bool result = service.IsChordPrefix(
			new KeyStroke(KeyCode.K, KeyModifiers.Control),
			TextureContext,
			command =>
			{
				asked.Add(command);
				return false;
			}
		);

		// The chord is bound to SaveAll in the geometry context and to Find in the texture context.
		Assert.IsFalse(result);
		Assert.HasCount(1, asked);
		Assert.AreEqual(TestCommand.Find, asked[0]);
	}

	[TestMethod]
	public void IsChordPrefix_NullPredicate_Throws()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.ThrowsExactly<ArgumentNullException>(() => service.IsChordPrefix(new KeyStroke(KeyCode.K, KeyModifiers.Control), null, null!));
	}

	[TestMethod]
	public void GetBindings_ChordCatalog_ReturnsTheChord()
	{
		Assert.AreEqual(
			new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control)),
			CreateChordService().GetBindings(TestCommand.SaveAll)[0]
		);
	}

	[TestMethod]
	public void GetDisplayText_RendersChordStrokesWithTheFormatter()
	{
		KeyBindingService<TestCommand> service = CreateChordService();

		Assert.AreEqual("Control+K, Control+S", service.GetDisplayText(TestCommand.SaveAll));
		Assert.AreEqual("Control+K, Control+F", service.GetDisplayText(TestCommand.Find));
	}

	/// <summary>
	/// Pins the blank-token contract on a context-scoped catalog: an empty, whitespace-only, or otherwise undeclared token resolves
	/// only the always-active bindings and never a context-scoped one.
	/// </summary>
	[TestMethod]
	public void TryGetCommand_BlankOrUndeclaredToken_ResolvesOnlyAlwaysActiveBindings()
	{
		KeyBindingService<TestCommand> service = CreateContextService();
		var contextScoped = new KeyStroke(KeyCode.E, KeyModifiers.None);

		foreach (string? token in new[] { "", "   ", "Gemoetry" })
		{
			Assert.IsTrue(service.TryGetCommand(new KeyStroke(KeyCode.S, KeyModifiers.Control), token, out TestCommand alwaysCommand));
			Assert.AreEqual(TestCommand.Save, alwaysCommand, $"'{token}' must still resolve the always-active binding.");

			Assert.IsFalse(service.TryGetCommand(contextScoped, token, out _), $"'{token}' must not resolve a context-scoped binding.");
		}
	}

	/// <summary>
	/// Pins that a blank or undeclared token reports only the always-active prefix relationships, never a context-scoped one: the catalog declares
	/// a chord prefix in each scope, and only the always-active one is reported for a token the catalog does not declare.
	/// </summary>
	[TestMethod]
	public void IsChordPrefix_BlankOrUndeclaredToken_ReportsOnlyAlwaysActivePrefixes()
	{
		var alwaysActivePrefix = new KeyStroke(KeyCode.K, KeyModifiers.Control);
		var contextScopedPrefix = new KeyStroke(KeyCode.J, KeyModifiers.Control);

		KeyBindingService<TestCommand> service = CreateService(
			catalog: new CommandCatalog<TestCommand>([
				new CommandDescriptor<TestCommand>(
					TestCommand.SaveAll,
					nameof(TestCommand.SaveAll),
					CommandRemappingPolicy.Remappable,
					new KeyChord(alwaysActivePrefix, new KeyStroke(KeyCode.S, KeyModifiers.Control))
				),

				new CommandDescriptor<TestCommand>(
					TestCommand.Find,
					nameof(TestCommand.Find),
					CommandRemappingPolicy.Remappable,
					new KeyChord(contextScopedPrefix, new KeyStroke(KeyCode.F, KeyModifiers.Control))
				)
				{
					Context = GeometryContext,
				},
			])
		);

		foreach (string? token in new[] { "", "   ", "Gemoetry" })
		{
			Assert.IsTrue(service.IsChordPrefix(alwaysActivePrefix, token, _ => true), $"'{token}' must report the always-active prefix.");
			Assert.IsFalse(service.IsChordPrefix(contextScopedPrefix, token, _ => true), $"'{token}' must not report a context-scoped prefix.");
		}
	}

	[TestMethod]
	public void GetDisplayText_EveryBindingShadowed_ReturnsFallback()
	{
		// Save's override claims both of Find's defaults, so Find has no dispatching binding left.
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("F", (int)KeyModifiers.Control), ("H", (int)KeyModifiers.Control))
		);

		Assert.IsEmpty(service.GetBindings(TestCommand.Find));
		Assert.AreEqual("Fallback", service.GetDisplayText(TestCommand.Find, "Fallback"));
	}
}
