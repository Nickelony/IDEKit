using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Drift guard for the conflict rules: the same co-active and prefix scenario is driven through the three entry
/// points that share <c>BindingClaimRules</c> - catalog construction, a service built from a document, and proposal validation - and each must reach
/// the same verdict.
/// </summary>
[TestClass]
public sealed class ConflictRuleScenarioTests
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
		PrefixDifferentContexts,
		PrefixCandidateLongerSameContext,
		PrefixCandidateLongerAlwaysAndScoped,
		PrefixCandidateLongerDifferentContexts,
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
		KeyBindingOutcome ExpectedOutcome
	);

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

			// Every entry point reaches the verdict the rules prescribe.
			Assert.AreEqual(claims.ExpectConflict, catalogRejected, $"{scenario}: catalog construction disagreed with the rules.");
			Assert.AreEqual(claims.ExpectConflict, documentRejected, $"{scenario}: service construction from a document disagreed with the rules.");
			Assert.AreEqual(claims.ExpectedOutcome, validateOutcome, $"{scenario}: proposal validation disagreed with the rules.");

			Assert.AreEqual(catalogRejected, documentRejected, $"{scenario}: catalog construction and document construction disagree.");
			Assert.AreEqual(catalogRejected, validateRejected, $"{scenario}: catalog construction and proposal validation disagree.");
		}
	}

	private static Scenario Describe(ClaimScenario scenario)
	{
		var single = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control));
		var longer = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));

		return scenario switch
		{
			ClaimScenario.SameChordSameContext => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: "geometry",
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.Conflict
			),

			ClaimScenario.SameChordAlwaysAndScoped => new(
				ExistingChord: single,
				ExistingContext: null,
				CandidateChord: single,
				CandidateContext: "geometry",
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.Conflict
			),

			ClaimScenario.SameChordAlwaysCandidateScopedExisting => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: null,
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.Conflict
			),

			ClaimScenario.SameChordDifferentContexts => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: "texture",
				ExpectConflict: false,
				ExpectedOutcome: KeyBindingOutcome.Succeeded
			),

			ClaimScenario.PrefixSameContext => new(
				ExistingChord: longer,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: "geometry",
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.PrefixConflict
			),

			ClaimScenario.PrefixAlwaysAndScoped => new(
				ExistingChord: longer,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: null,
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.PrefixConflict
			),

			ClaimScenario.PrefixDifferentContexts => new(
				ExistingChord: longer,
				ExistingContext: "geometry",
				CandidateChord: single,
				CandidateContext: "texture",
				ExpectConflict: false,
				ExpectedOutcome: KeyBindingOutcome.Succeeded
			),

			// The reverse direction: the candidate is the longer chord and the existing claim is the
			// shorter prefix, which must be rejected in exactly the same scopes.
			ClaimScenario.PrefixCandidateLongerSameContext => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: longer,
				CandidateContext: "geometry",
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.PrefixConflict
			),

			ClaimScenario.PrefixCandidateLongerAlwaysAndScoped => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: longer,
				CandidateContext: null,
				ExpectConflict: true,
				ExpectedOutcome: KeyBindingOutcome.PrefixConflict
			),

			ClaimScenario.PrefixCandidateLongerDifferentContexts => new(
				ExistingChord: single,
				ExistingContext: "geometry",
				CandidateChord: longer,
				CandidateContext: "texture",
				ExpectConflict: false,
				ExpectedOutcome: KeyBindingOutcome.Succeeded
			),

			_ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "The scenario is not described."),
		};
	}

	/// <summary>
	/// Drives the pair through catalog construction: the claim already present is declared first, then the candidate, so the candidate
	/// is the one the rules test.
	/// </summary>
	private static bool CatalogRejects(Scenario scenario)
	{
		void Build()
		{
			_ = new CommandCatalog<TestCommand>([
				Descriptor(TestCommand.Undo, scenario.ExistingContext, scenario.ExistingChord),
				Descriptor(TestCommand.Save, scenario.CandidateContext, scenario.CandidateChord),
			]);
		}

		if (scenario.ExpectConflict)
		{
			Assert.ThrowsExactly<ArgumentException>(Build);
			return true;
		}

		Build();
		return false;
	}

	/// <summary>
	/// Drives the pair through service construction from a document: the claim already present is the catalog default of a host-managed command
	/// and the candidate is the override, so a document is rejected exactly when the rules do not let the override take the default's chord over.
	/// </summary>
	private static bool DocumentRejects(Scenario scenario)
	{
		var catalog = new CommandCatalog<TestCommand>([
			Descriptor(TestCommand.Undo, scenario.ExistingContext, CommandRemappingPolicy.HostManaged, scenario.ExistingChord),
			Descriptor(TestCommand.Save, scenario.CandidateContext),
		]);

		(string KeyName, int Modifiers)[] strokes =
		[
			.. scenario.CandidateChord.Strokes.ToArray().Select(stroke => (stroke.Key.ToString(), (int)stroke.Modifiers)),
		];

		KeyBindingOverrides overrides = CreateChordOverrides(nameof(TestCommand.Save), strokes);

		if (scenario.ExpectConflict)
		{
			Assert.ThrowsExactly<InvalidOperationException>(() => CreateService(overrides, catalog: catalog));
			return true;
		}

		_ = CreateService(overrides, catalog: catalog);
		return false;
	}

	/// <summary>
	/// Drives the candidate through proposal validation against the same kind of catalog the document path uses, under the reject policy so an exact
	/// collision reports <c>Conflict</c>.
	/// </summary>
	private static KeyBindingOutcome ValidateCandidate(Scenario scenario)
	{
		var catalog = new CommandCatalog<TestCommand>([
			Descriptor(TestCommand.Undo, scenario.ExistingContext, CommandRemappingPolicy.HostManaged, scenario.ExistingChord),
			Descriptor(TestCommand.Save, scenario.CandidateContext),
		]);

		return CreateService(catalog: catalog).Validate(TestCommand.Save, [scenario.CandidateChord]);
	}

	private static CommandDescriptor<TestCommand> Descriptor(TestCommand command, string? context, params KeyChord[] bindings) =>
		Descriptor(command, context, remappingPolicy: CommandRemappingPolicy.Remappable, bindings);

	private static CommandDescriptor<TestCommand> Descriptor(
		TestCommand command,
		string? context,
		CommandRemappingPolicy remappingPolicy,
		params KeyChord[] bindings
	) => new(command, serializedId: command.ToString(), remappingPolicy, defaultBindings: bindings) { Context = context };
}
