using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Drift guard for the conflict rules: the same co-active and prefix scenario is driven through the
/// three entry points that share <c>BindingClaimRules</c> - catalog construction, a service built from a
/// document, and proposal validation - and all three must reach the same verdict. A rule change that
/// reached only one entry point would fail here, which is what the single authority in
/// <c>BindingClaimRules</c> exists to prevent.
/// </summary>
[TestClass]
public class ConflictRuleMatrixTests
{
	/// <summary>The relationship a scenario compares between the claim already present and the candidate.</summary>
	private enum ClaimScenario
	{
		SameChordSameContext,
		SameChordAlwaysAndScoped,
		SameChordAlwaysCandidateScopedExisting,
		SameChordDifferentContexts,
		PrefixSameContext,
		PrefixAlwaysAndScoped,
		PrefixDifferentContexts
	}

	/// <summary>The two claims a scenario compares, and the verdict the rules must reach.</summary>
	/// <param name="ExistingChord">The chord the claim already present covers.</param>
	/// <param name="ExistingContext">The context of the claim already present, or <see langword="null"/> for the always-active context.</param>
	/// <param name="CandidateChord">The chord the candidate claim covers.</param>
	/// <param name="CandidateContext">The context of the candidate claim, or <see langword="null"/> for the always-active context.</param>
	/// <param name="ExpectConflict"><see langword="true"/> when the rules reject the pair; otherwise, <see langword="false"/>.</param>
	/// <param name="ExpectedOutcome">The outcome proposal validation must report.</param>
	private sealed record Scenario(
		KeyChord ExistingChord,
		string? ExistingContext,
		KeyChord CandidateChord,
		string? CandidateContext,
		bool ExpectConflict,
		KeyBindingOutcome ExpectedOutcome);

	[TestMethod]
	public void ConflictRules_AgreeAcrossEntryPoints()
	{
		foreach (ClaimScenario scenario in Enum.GetValues<ClaimScenario>())
		{
			Scenario claims = Describe(scenario);

			bool catalogRejected = CatalogRejects(claims);
			bool documentRejected = DocumentRejects(claims);
			KeyBindingOutcome validateOutcome = ValidateCandidate(claims);
			bool validateRejected = validateOutcome != KeyBindingOutcome.Succeeded;

			// Every entry point must reach the verdict the rules prescribe...
			Assert.AreEqual(claims.ExpectConflict, catalogRejected, $"{scenario}: catalog construction disagreed with the rules.");
			Assert.AreEqual(claims.ExpectConflict, documentRejected, $"{scenario}: service construction from a document disagreed with the rules.");
			Assert.AreEqual(claims.ExpectedOutcome, validateOutcome, $"{scenario}: proposal validation disagreed with the rules.");

			// ...and they must agree with one another, which is the drift guard the shared authority buys.
			Assert.AreEqual(catalogRejected, documentRejected, $"{scenario}: catalog construction and document construction disagree.");
			Assert.AreEqual(catalogRejected, validateRejected, $"{scenario}: catalog construction and proposal validation disagree.");
		}
	}

	private static Scenario Describe(ClaimScenario scenario)
	{
		var single = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control));
		var longer = new KeyChord(
			new KeyCombo(KeyCode.K, KeyModifierSet.Control),
			new KeyCombo(KeyCode.S, KeyModifierSet.Control));

		return scenario switch
		{
			ClaimScenario.SameChordSameContext =>
				new(single, "geometry", single, "geometry", true, KeyBindingOutcome.Conflict),
			ClaimScenario.SameChordAlwaysAndScoped =>
				new(single, null, single, "geometry", true, KeyBindingOutcome.Conflict),
			ClaimScenario.SameChordAlwaysCandidateScopedExisting =>
				new(single, "geometry", single, null, true, KeyBindingOutcome.Conflict),
			ClaimScenario.SameChordDifferentContexts =>
				new(single, "geometry", single, "texture", false, KeyBindingOutcome.Succeeded),
			ClaimScenario.PrefixSameContext =>
				new(longer, "geometry", single, "geometry", true, KeyBindingOutcome.PrefixConflict),
			ClaimScenario.PrefixAlwaysAndScoped =>
				new(longer, "geometry", single, null, true, KeyBindingOutcome.PrefixConflict),
			ClaimScenario.PrefixDifferentContexts =>
				new(longer, "geometry", single, "texture", false, KeyBindingOutcome.Succeeded),
			_ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "The scenario is not described.")
		};
	}

	/// <summary>
	/// Drives the pair through catalog construction: the claim already present is declared first, then the
	/// candidate, so the candidate is the one the rules test.
	/// </summary>
	private static bool CatalogRejects(Scenario scenario)
	{
		try
		{
			_ = new CommandCatalog<TestCommand>([
				Descriptor(TestCommand.Undo, scenario.ExistingContext, scenario.ExistingChord),
				Descriptor(TestCommand.Save, scenario.CandidateContext, scenario.CandidateChord)
			]);

			return false;
		}
		catch (ArgumentException)
		{
			return true;
		}
	}

	/// <summary>
	/// Drives the pair through service construction from a document: the claim already present is the
	/// catalog default of a host-managed command and the candidate is the override, so a document is
	/// rejected exactly when the rules do not let the override take the default's chord over.
	/// </summary>
	private static bool DocumentRejects(Scenario scenario)
	{
		var catalog = new CommandCatalog<TestCommand>([
			Descriptor(TestCommand.Undo, scenario.ExistingContext, CommandRemappingPolicy.HostManaged, scenario.ExistingChord),
			Descriptor(TestCommand.Save, scenario.CandidateContext)
		]);

		(string KeyName, int Modifiers)[] strokes =
			[.. scenario.CandidateChord.Strokes.ToArray().Select(combo => (combo.Key.ToString(), (int)combo.Modifiers))];

		KeyBindingOverrides overrides = CreateChordOverrides(nameof(TestCommand.Save), strokes);

		try
		{
			_ = CreateService(overrides, catalog: catalog);
			return false;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}

	/// <summary>
	/// Drives the candidate through proposal validation against the same kind of catalog the document path
	/// uses, under the reject policy so an exact collision reports <c>Conflict</c>.
	/// </summary>
	private static KeyBindingOutcome ValidateCandidate(Scenario scenario)
	{
		var catalog = new CommandCatalog<TestCommand>([
			Descriptor(TestCommand.Undo, scenario.ExistingContext, CommandRemappingPolicy.HostManaged, scenario.ExistingChord),
			Descriptor(TestCommand.Save, scenario.CandidateContext)
		]);

		return CreateService(catalog: catalog).Validate(TestCommand.Save, [scenario.CandidateChord]);
	}

	private static CommandDescriptor<TestCommand> Descriptor(
		TestCommand command,
		string? context,
		params KeyChord[] bindings)
		=> Descriptor(command, context, CommandRemappingPolicy.Remappable, bindings);

	private static CommandDescriptor<TestCommand> Descriptor(
		TestCommand command,
		string? context,
		CommandRemappingPolicy remappingPolicy,
		params KeyChord[] bindings)
		=> new(command, command.ToString(), remappingPolicy, bindings) { Context = context };
}
