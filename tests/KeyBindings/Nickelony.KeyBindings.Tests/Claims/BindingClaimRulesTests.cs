using Nickelony.KeyBindings.Testing;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class BindingClaimRulesTests
{
	private const string Geometry = "geometry";
	private const string Texture = "texture";

	private static readonly KeyChord s_k = Chord(KeyCode.K);
	private static readonly KeyChord s_kThenS = Chord(KeyCode.K, KeyCode.S);
	private static readonly KeyChord s_kThenSThenX = Chord(KeyCode.K, KeyCode.S, KeyCode.X);
	private static readonly KeyChord s_kThenF = Chord(KeyCode.K, KeyCode.F);
	private static readonly KeyChord s_e = Chord(KeyCode.E);

	[TestMethod]
	public void GetStrictPrefixes_OneStroke_ReturnsNone() => Assert.IsEmpty(BindingClaimRules.GetStrictPrefixes(s_k).ToArray());

	[TestMethod]
	public void GetStrictPrefixes_ThreeStrokes_ReturnsEachLeadingRunShortestFirstWithoutTheWholeChord() =>
		Assert.AreSequenceEqual([s_k, s_kThenS], [.. BindingClaimRules.GetStrictPrefixes(s_kThenSThenX)]);

	[TestMethod]
	public void IsStrictPrefix_ALeadingRunOfALongerChord_ReturnsTrue() => Assert.IsTrue(BindingClaimRules.IsStrictPrefix(s_k, s_kThenS));

	[TestMethod]
	public void IsStrictPrefix_TheSameChord_ReturnsFalse() => Assert.IsFalse(BindingClaimRules.IsStrictPrefix(s_kThenS, s_kThenS));

	[TestMethod]
	public void IsStrictPrefix_ALongerChord_ReturnsFalse() => Assert.IsFalse(BindingClaimRules.IsStrictPrefix(s_kThenS, s_k));

	[TestMethod]
	public void IsStrictPrefix_AShorterChordWithADifferentLeadingStroke_ReturnsFalse() =>
		Assert.IsFalse(BindingClaimRules.IsStrictPrefix(s_e, s_kThenS));

	[TestMethod]
	public void TryGetCoActiveClaim_NoClaimOfTheChord_ReturnsFalse()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_e)] = TestCommand.Save };

		Assert.IsFalse(
			BindingClaimRules.TryGetCoActiveClaim(
				claims,
				candidate: new ContextChord(Geometry, s_k),
				conflictingClaim: out _,
				conflictingOwner: out _
			)
		);
	}

	[TestMethod]
	public void TryGetCoActiveClaim_TheSameContext_ReturnsThatClaimAndItsOwner()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Geometry, s_e)] = TestCommand.Build };

		Assert.IsTrue(
			BindingClaimRules.TryGetCoActiveClaim(
				claims,
				candidate: new ContextChord(Geometry, s_e),
				conflictingClaim: out ContextChord claim,
				conflictingOwner: out TestCommand owner
			)
		);

		Assert.AreEqual(new ContextChord(Geometry, s_e), claim);
		Assert.AreEqual(TestCommand.Build, owner);
	}

	[TestMethod]
	public void TryGetCoActiveClaim_AnAlwaysActiveClaimAgainstAScopedCandidate_ReturnsTheAlwaysActiveClaim()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_e)] = TestCommand.Save };

		Assert.IsTrue(
			BindingClaimRules.TryGetCoActiveClaim(
				claims,
				candidate: new ContextChord(Geometry, s_e),
				conflictingClaim: out ContextChord claim,
				conflictingOwner: out TestCommand owner
			)
		);

		Assert.AreEqual(new ContextChord(null, s_e), claim);
		Assert.AreEqual(TestCommand.Save, owner);
	}

	[TestMethod]
	public void TryGetCoActiveClaim_AScopedClaimAgainstAnAlwaysActiveCandidate_ReturnsTheScopedClaim()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Texture, s_e)] = TestCommand.Find };

		Assert.IsTrue(
			BindingClaimRules.TryGetCoActiveClaim(
				claims,
				candidate: new ContextChord(null, s_e),
				conflictingClaim: out ContextChord claim,
				conflictingOwner: out TestCommand owner
			)
		);

		Assert.AreEqual(new ContextChord(Texture, s_e), claim);
		Assert.AreEqual(TestCommand.Find, owner);
	}

	[TestMethod]
	public void TryGetCoActiveClaim_AClaimOfAnotherContext_ReturnsFalse()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Texture, s_e)] = TestCommand.Find };

		Assert.IsFalse(
			BindingClaimRules.TryGetCoActiveClaim(
				claims,
				candidate: new ContextChord(Geometry, s_e),
				conflictingClaim: out _,
				conflictingOwner: out _
			)
		);
	}

	[TestMethod]
	public void IsPrefixConflict_APrefixClaimedInTheSameContext_ReturnsTrue()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Geometry, s_k)] = TestCommand.Build };

		Assert.IsTrue(
			BindingClaimRules.IsPrefixConflict(
				claims,
				contextScopedChords: new HashSet<KeyChord>(),
				claim: new ContextChord(Geometry, s_kThenS),
				prefix: s_k
			)
		);
	}

	[TestMethod]
	public void IsPrefixConflict_APrefixClaimedInTheAlwaysActiveContext_ReturnsTrueForAScopedClaim()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_k)] = TestCommand.Save };

		Assert.IsTrue(
			BindingClaimRules.IsPrefixConflict(
				claims,
				contextScopedChords: new HashSet<KeyChord>(),
				claim: new ContextChord(Geometry, s_kThenS),
				prefix: s_k
			)
		);
	}

	[TestMethod]
	public void IsPrefixConflict_AScopedClaimOfThePrefixAgainstAnAlwaysActiveClaim_ReturnsTrue()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Geometry, s_k)] = TestCommand.Build };

		Assert.IsTrue(
			BindingClaimRules.IsPrefixConflict(
				claims,
				contextScopedChords: new HashSet<KeyChord> { s_k },
				claim: new ContextChord(null, s_kThenS),
				prefix: s_k
			)
		);
	}

	[TestMethod]
	public void IsPrefixConflict_APrefixClaimedInAnotherContext_ReturnsFalse()
	{
		var claims = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Texture, s_k)] = TestCommand.Find };

		Assert.IsFalse(
			BindingClaimRules.IsPrefixConflict(
				claims,
				contextScopedChords: new HashSet<KeyChord> { s_k },
				claim: new ContextChord(Geometry, s_kThenS),
				prefix: s_k
			)
		);
	}

	[TestMethod]
	public void HasPrefixRelationship_TheProposalIsAPrefixOfAnotherCommandsChord_ReturnsTrue()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_kThenS)] = TestCommand.SaveAll };

		Assert.IsTrue(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord>(),
				chord: s_k,
				command: TestCommand.Save,
				context: null
			)
		);
	}

	[TestMethod]
	public void HasPrefixRelationship_AnotherCommandsChordIsAPrefixOfTheProposal_ReturnsTrue()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_k)] = TestCommand.Save };

		Assert.IsTrue(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord>(),
				chord: s_kThenSThenX,
				command: TestCommand.SaveAll,
				context: null
			)
		);
	}

	[TestMethod]
	public void HasPrefixRelationship_TheOnlyRelatedChordsBelongToTheProposingCommand_ReturnsFalse()
	{
		var bound = new Dictionary<ContextChord, TestCommand>
		{
			[new ContextChord(null, s_k)] = TestCommand.Save,
			[new ContextChord(null, s_kThenF)] = TestCommand.Save,
		};

		Assert.IsFalse(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord>(),
				chord: s_kThenS,
				command: TestCommand.Save,
				context: null
			)
		);

		Assert.IsFalse(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord>(),
				chord: s_k,
				command: TestCommand.Save,
				context: null
			)
		);
	}

	[TestMethod]
	public void HasPrefixRelationship_TheRelatedChordIsInAnotherContext_ReturnsFalse()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Texture, s_kThenS)] = TestCommand.Find };

		Assert.IsFalse(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord> { s_kThenS },
				chord: s_k,
				command: TestCommand.Build,
				context: Geometry
			)
		);
	}

	[TestMethod]
	public void HasPrefixRelationship_AnAlwaysActiveProposalWhosePrefixIsClaimedInAScopedContext_ReturnsTrue()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Geometry, s_k)] = TestCommand.Build };

		Assert.IsTrue(
			BindingClaimRules.HasPrefixRelationship(
				commandsByChord: bound,
				contextScopedChords: new HashSet<KeyChord> { s_k },
				chord: s_kThenS,
				command: TestCommand.Save,
				context: null
			)
		);
	}

	[TestMethod]
	public void TryGetOwner_AScopedLookupWithoutAScopedClaim_FallsBackToTheAlwaysActiveClaim()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(null, s_e)] = TestCommand.Save };

		Assert.IsTrue(BindingClaimRules.TryGetOwner(commandsByChord: bound, chord: s_e, context: Geometry, out TestCommand owner));
		Assert.AreEqual(TestCommand.Save, owner);
	}

	[TestMethod]
	public void TryGetOwner_AScopedClaimAndAnAlwaysActiveClaim_PrefersTheScopedClaim()
	{
		var bound = new Dictionary<ContextChord, TestCommand>
		{
			[new ContextChord(null, s_e)] = TestCommand.Save,
			[new ContextChord(Geometry, s_e)] = TestCommand.Build,
		};

		Assert.IsTrue(BindingClaimRules.TryGetOwner(commandsByChord: bound, chord: s_e, context: Geometry, out TestCommand owner));
		Assert.AreEqual(TestCommand.Build, owner);
	}

	[TestMethod]
	public void TryGetOwner_NoActiveContext_ConsidersOnlyTheAlwaysActiveClaim()
	{
		var bound = new Dictionary<ContextChord, TestCommand> { [new ContextChord(Geometry, s_e)] = TestCommand.Build };
		Assert.IsFalse(BindingClaimRules.TryGetOwner(commandsByChord: bound, chord: s_e, context: null, owner: out _));
	}

	private static KeyChord Chord(params KeyCode[] keys) => new([.. keys.Select(key => new KeyStroke(key, KeyModifiers.Control))]);
}
