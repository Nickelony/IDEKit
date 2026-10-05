using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A public or protected method or constructor rejects a <see langword="null"/> argument
/// before it does anything else, with <c>ArgumentNullException.ThrowIfNull</c>, an <c>is null</c> test that throws, or <c>?? throw</c>, so a host
/// that passes <see langword="null"/> learns it at the call and not later in the library. A member whose first statement hands the argument unchanged
/// to a call that rejects it counts as guarded. A positional record parameter takes no guard of its own, because a <see langword="default"/>
/// struct skips the constructor, so such a member also rejects each non-nullable reference member that it reads from a record
/// the host fills in: a request, query, specification or hooks record.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class PublicArgumentGuardTests
{
	private const string Guard = nameof(PublicArgumentGuardTests);

	// The value types that the repository's source names but does not declare: the framework's, and the editor engines' structs and enums.
	// A name that is missing here is read as a reference type, so a parameter of a struct that is missing from the list needs this entry or a row.
	private static readonly HashSet<string> s_engineValueTypes = new(StringComparer.Ordinal)
	{
		"AvaloniaKeyModifiers",
		"CancellationToken",
		"CancellationTokenRegistration",
		"Color",
		"CornerRadius",
		"DateOnly",
		"DateTime",
		"DateTimeOffset",
		"Guid",
		"GridLength",
		"ImmutableArray",
		"Index",
		"JsonElement",
		"Key",
		"KeyModifiers",
		"KeyValuePair",
		"Matrix",
		"Memory",
		"ModifierKeys",
		"MouseButton",
		"Point",
		"Range",
		"ReadOnlyMemory",
		"RegexOptions",
		"ReadOnlySpan",
		"Rect",
		"Rune",
		"Size",
		"Span",
		"StringComparison",
		"TextLocation",
		"Thickness",
		"TimeOnly",
		"TimeSpan",
		"ValueTask",
		"Vector",
	};

	// A public or protected member that leaves a non-nullable reference argument unchecked, keyed "{path}: {type}.{member}({parameter})", where
	// a constructor is ".ctor".
	private static readonly AllowList s_unguardedArguments = new(
		"unguarded public arguments",

		(
			"shared/Editor.LanguageFeatures/Completion/CompletionItemAdapter.cs: CompletionItemAdapter.Complete(insertionRequestEventArgs)",
			"ICompletionData of the engine declares the member, and the engine supplies the argument."
		),

		(
			"shared/Editor/LineStatus/LineStatusIconMarginBase.Common.cs: LineStatusIconMarginBase.OnIconClicked(visualLine)",
			"A virtual hook that only the margin calls, with the visual line it hit."
		),

		(
			"shared/Primitives/Callbacks/CallbackContainment.cs: CallbackContainment.Invoke(fallbackValue)",
			"The method returns the fallback as given, so a null fallback is a valid result."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Requests/KeyedLatestRequestGate.cs: KeyedLatestRequestGate.Begin(key)",
			"The dictionary lookup of the key rejects a null with an ArgumentNullException unless the gate has stopped."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Requests/KeyedLatestRequestGate.cs: KeyedLatestRequestGate.Cancel(key)",
			"The dictionary removal of the key rejects a null with an ArgumentNullException."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextOffsetValidation.cs: TextOffsetValidation.ValidateOffset(offsetDescription)",
			"The text only fills the message of the exception that the method throws."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextOffsetValidation.cs: TextOffsetValidation.ValidateOffset(parameterName)",
			"The text only names the parameter in the exception that the method throws, and the exception accepts a null name."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextOffsetValidation.cs: TextOffsetValidation.ValidateRange(parameterName)",
			"The text only names the parameter in the exception that the method throws, and the exception accepts a null name."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Text/TextOffsetValidation.cs: TextOffsetValidation.ValidateRange(rangeDescription)",
			"The text only fills the message of the exception that the method throws."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileWatchSpecification.cs: FileWatchSpecification.Validate(paramName)",
			"The text only names the parameter in the exception that the method throws, and the exception accepts a null name."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/Completion/CompletionWindowOptions.cs: CompletionWindowOptions.Validate(parameterName)",
			"The text only names the parameter in the exception that the method throws, and the exception accepts a null name."
		),

		(
			"src/KeyBindings/Nickelony.KeyBindings/Commands/CommandDescriptor.cs: CommandDescriptor..ctor(command)",
			"Catalog construction rejects the default value of the identity, which is null for a reference type."
		)
	);

	// The records that a host fills in and a member consumes: the complete input of one call, and a bundle of host delegates. The library builds
	// every other record, so it takes no guard of its own.
	private static readonly string[] s_inputRecordSuffixes = ["Hooks", "Query", "Request", "Specification"];

	// A non-nullable reference member of an input record that a public member reads and does not reject a null of, keyed
	// "{path}: {type}.{member}({argument}.{record member})", where a constructor is ".ctor".
	private static readonly AllowList s_unrejectedRecordMembers = new("record members a public member reads without a null check");

	[TestMethod]
	public void APublicMemberRejectsEachNonNullableReferenceArgumentBeforeAnyOtherStatement()
	{
		Dictionary<string, HashSet<string>> declaredKinds = DeclaredTypeKinds();
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			if (type is InterfaceDeclarationSyntax || !ProductSyntax.IsReachable(type))
				continue;

			foreach (MemberDeclarationSyntax member in type.Members.Where(member => ProductSyntax.IsVisible(member, type)))
			{
				if (
					member.Modifiers.Any(SyntaxKind.OverrideKeyword)
					|| member.Modifiers.Any(SyntaxKind.AbstractKeyword)
					|| member.Modifiers.Any(SyntaxKind.ExternKeyword)
				)
				{
					continue;
				}

				Callable? callable = member switch
				{
					MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null } method when HasBody(method) => new Callable(
						Parameters: method.ParameterList,
						Constraints: method.ConstraintClauses,
						TypeParameters: method.TypeParameterList,
						Initializer: null,
						Body: method.Body,
						Expression: method.ExpressionBody,
						Name: method.Identifier.ValueText
					),

					ConstructorDeclarationSyntax constructor when HasBody(constructor) => new Callable(
						Parameters: constructor.ParameterList,
						Constraints: default,
						TypeParameters: null,
						Initializer: constructor.Initializer,
						Body: constructor.Body,
						Expression: constructor.ExpressionBody,
						Name: ".ctor"
					),

					_ => null,
				};

				if (callable is null)
					continue;

				foreach (string parameter in UnguardedParameters(callable, owner: type, declaredKinds, project: file.Project))
					offenders.Add($"{file.Path}: {ProductSyntax.NameOf(type)}.{callable.Name}({parameter})");
			}
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal))
		{
			if (!s_unguardedArguments.Allows(offender))
			{
				violations.Add(
					s_unguardedArguments.Rejection(
						$"{offender} is a non-nullable reference argument of a public or protected member, and the member does not reject a null before its first other statement. Use ArgumentNullException.ThrowIfNull, an is-null test that throws, or ?? throw.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_unguardedArguments.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Unguarded public arguments:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AMemberThatTakesAnInputRecordRejectsEachNonNullableReferenceMemberItReads()
	{
		Dictionary<string, HashSet<string>> declaredKinds = DeclaredTypeKinds();
		Dictionary<string, HashSet<string>> inputRecords = new(StringComparer.Ordinal);

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			if (
				type is not RecordDeclarationSyntax { ParameterList: { } parameters } record
				|| !ProductSyntax.IsReachable(record)
				|| !IsInputRecord(record)
			)
			{
				continue;
			}

			HashSet<string> generic = new(
				record.TypeParameterList?.Parameters.Select(parameter => parameter.Identifier.ValueText) ?? [],
				StringComparer.Ordinal
			);

			SyntaxList<TypeParameterConstraintClauseSyntax>[] constraints = [record.ConstraintClauses];

			HashSet<string> members = new(
				parameters
					.Parameters.Where(parameter => NeedsGuard(parameter, generic, constraintLists: constraints, declaredKinds, project: file.Project))
					.Select(parameter => parameter.Identifier.ValueText),

				StringComparer.Ordinal
			);

			if (members.Count > 0)
				inputRecords[record.Identifier.ValueText] = members;
		}

		Assert.IsNotEmpty(inputRecords, "The guard read no input record, so it checks nothing.");

		HashSet<string> offenders = new(StringComparer.Ordinal);
		int reads = 0;

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			if (!ProductSyntax.IsReachable(type))
				continue;

			foreach (MemberDeclarationSyntax member in type.Members.Where(member => ProductSyntax.IsVisible(member, type)))
			{
				if (member is not BaseMethodDeclarationSyntax method || !HasBody(method) || method.Modifiers.Any(SyntaxKind.OverrideKeyword))
					continue;

				SyntaxNode body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;

				string memberName = member switch
				{
					ConstructorDeclarationSyntax => ".ctor",
					MethodDeclarationSyntax named => named.Identifier.ValueText,
					_ => "operator",
				};

				foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
				{
					string argument = parameter.Identifier.ValueText;

					if (RecordNameOf(parameter.Type) is not { } recordName || !inputRecords.TryGetValue(recordName, out HashSet<string>? members))
						continue;

					foreach (string recordMember in members.Where(recordMember => ReadsMember(body, argument, recordMember)))
					{
						reads++;

						if (!GuardsMember(body, argument, recordMember))
							offenders.Add($"{file.Path}: {ProductSyntax.NameOf(type)}.{memberName}({argument}.{recordMember})");
					}
				}
			}
		}

		Assert.IsGreaterThan(0, reads, "No public member reads a member of an input record, so the guard checks nothing.");

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_unrejectedRecordMembers.Allows(offender)))
		{
			violations.Add(
				s_unrejectedRecordMembers.Rejection(
					$"{offender} reads a non-nullable reference member of an input record and does not reject a null first. Use ArgumentNullException.ThrowIfNull on the member, ?? throw, or an is-null test that throws.",
					Guard
				)
			);
		}

		violations.AddRange(s_unrejectedRecordMembers.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Unrejected record members:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>Reports whether a record is the complete input of one call or a bundle of host delegates, by the last word of its name.</summary>
	private static bool IsInputRecord(RecordDeclarationSyntax record) =>
		s_inputRecordSuffixes.Any(suffix => record.Identifier.ValueText.EndsWith(suffix, StringComparison.Ordinal));

	/// <summary>The name of the type of a parameter, without a nullable mark or generic arguments.</summary>
	private static string? RecordNameOf(TypeSyntax? type)
	{
		TypeSyntax? inner = type is NullableTypeSyntax nullable ? nullable.ElementType : type;

		return inner switch
		{
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			GenericNameSyntax generic => generic.Identifier.ValueText,
			_ => null,
		};
	}

	/// <summary>Reports whether the body reads <c>argument.member</c>.</summary>
	private static bool ReadsMember(SyntaxNode body, string argument, string member) =>
		body.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(access => IsMemberOf(access, argument, member));

	private static bool IsMemberOf(ExpressionSyntax expression, string argument, string member)
	{
		return expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target, Name.Identifier.ValueText: var name }
			&& target.Identifier.ValueText == argument
			&& name == member;
	}

	/// <summary>
	/// Reports whether the body rejects a <see langword="null"/> <c>argument.member</c>: a <c>ThrowIf</c> call on it, <c>?? throw</c>,
	/// or an <c>if</c> that tests it for <see langword="null"/> or emptiness and throws.
	/// </summary>
	private static bool GuardsMember(SyntaxNode body, string argument, string member)
	{
		foreach (SyntaxNode node in body.DescendantNodes())
		{
			switch (node)
			{
				// A local that copies the member and is rejected in its turn: `ITextSnapshot snapshot = request.Snapshot;` and then `ThrowIfNull(snapshot)`.
				case VariableDeclaratorSyntax { Initializer.Value: { } copied } declarator
					when IsMemberOf(copied, argument, member) && Guards(body, declarator.Identifier.ValueText, boxes: false):
					return true;

				case InvocationExpressionSyntax call
					when call.Expression.ToString().Contains(".ThrowIf", StringComparison.Ordinal)
						&& call.ArgumentList.Arguments.Count > 0
						&& IsMemberOf(call.ArgumentList.Arguments[0].Expression, argument, member):
					return true;

				case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression, Right: ThrowExpressionSyntax } coalesce
					when IsMemberOf(coalesce.Left, argument, member):
					return true;

				case IfStatementSyntax conditional
					when conditional.Statement.DescendantNodesAndSelf().Any(inner => inner is ThrowStatementSyntax or ThrowExpressionSyntax)
						&& conditional.Condition.DescendantNodesAndSelf().Any(inner => IsNullTest(inner, argument, member)):
					return true;
			}
		}

		return false;
	}

	private static bool IsNullTest(SyntaxNode node, string argument, string member)
	{
		return node switch
		{
			IsPatternExpressionSyntax pattern => IsMemberOf(pattern.Expression, argument, member),

			BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } equals => IsMemberOf(equals.Left, argument, member)
				&& equals.Right.IsKind(SyntaxKind.NullLiteralExpression),

			InvocationExpressionSyntax call => call.Expression.ToString() is "string.IsNullOrEmpty" or "string.IsNullOrWhiteSpace"
				&& call.ArgumentList.Arguments.Count > 0
				&& IsMemberOf(call.ArgumentList.Arguments[0].Expression, argument, member),

			_ => false,
		};
	}

	private static bool HasBody(BaseMethodDeclarationSyntax method) => method.Body is not null || method.ExpressionBody is not null;

	/// <summary>The names of the non-nullable reference parameters that neither a guard clause nor a forwarding first statement covers.</summary>
	private static IEnumerable<string> UnguardedParameters(
		Callable callable,
		TypeDeclarationSyntax owner,
		Dictionary<string, HashSet<string>> declaredKinds,
		ProjectFile project
	)
	{
		HashSet<string> generic = new(
			owner
				.AncestorsAndSelf()
				.OfType<TypeDeclarationSyntax>()
				.SelectMany(type => type.TypeParameterList?.Parameters ?? [])
				.Select(parameter => parameter.Identifier.ValueText),

			StringComparer.Ordinal
		);

		generic.UnionWith((callable.TypeParameters?.Parameters ?? []).Select(parameter => parameter.Identifier.ValueText));

		SyntaxList<TypeParameterConstraintClauseSyntax>[] constraintLists =
		[
			callable.Constraints,
			.. owner.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Select(type => type.ConstraintClauses),
		];

		ParameterSyntax[] required =
		[
			.. callable.Parameters.Parameters.Where(parameter => NeedsGuard(parameter, generic, constraintLists, declaredKinds, project)),
		];

		if (required.Length == 0)
			yield break;

		List<SyntaxNode> guards = [];

		if (callable.Initializer is not null)
			guards.Add(callable.Initializer);

		if (callable.Expression is not null)
			guards.Add(callable.Expression.Expression);

		StatementSyntax[] statements = [.. callable.Body?.Statements ?? []];
		StatementSyntax[] guardClauses = [.. statements.TakeWhile(IsGuardClause)];

		guards.AddRange(guardClauses);

		// The first statement that is no guard clause, or the expression body, is where an argument may be handed on.
		SyntaxNode? first = callable.Expression is not null ? callable.Expression.Expression : statements.Skip(guardClauses.Length).FirstOrDefault();

		foreach (ParameterSyntax parameter in required)
		{
			string name = parameter.Identifier.ValueText;

			// ThrowIfNull boxes a struct argument of a generic parameter, so a generic parameter that is not a class takes an is-null test.
			bool boxes =
				parameter.Type is IdentifierNameSyntax typeName
				&& generic.Contains(typeName.Identifier.ValueText)
				&& !HasKeywordConstraint(constraintLists, typeName.Identifier.ValueText, SyntaxKind.ClassKeyword);

			bool guarded =
				guards.Any(guard => Guards(guard, name, boxes))
				|| (callable.Initializer is not null && IsArgumentOfACall(callable.Initializer, name))
				|| (first is not null && IsArgumentOfACall(first, name));

			if (!guarded)
				yield return name;
		}
	}

	/// <summary>Reports whether a constraint clause of the type parameter names the <c>class</c> or <c>struct</c> keyword.</summary>
	private static bool HasKeywordConstraint(
		SyntaxList<TypeParameterConstraintClauseSyntax>[] constraintLists,
		string typeParameter,
		SyntaxKind keyword
	)
	{
		return constraintLists
			.SelectMany(clauses => clauses)
			.Any(clause =>
				clause.Name.Identifier.ValueText == typeParameter
				&& clause.Constraints.Any(constraint =>
					constraint is ClassOrStructConstraintSyntax { ClassOrStructKeyword: var token } && token.IsKind(keyword)
				)
			);
	}

	/// <summary>
	/// Reports whether the node hands the parameter unchanged to a call as a direct argument, outside any lambda, so that call rejects
	/// the <see langword="null"/>.
	/// </summary>
	private static bool IsArgumentOfACall(SyntaxNode node, string parameter)
	{
		foreach (SyntaxNode descendant in node.DescendantNodesAndSelf(descend => descend is not LambdaExpressionSyntax))
		{
			BaseArgumentListSyntax? arguments = descendant switch
			{
				InvocationExpressionSyntax call => call.ArgumentList,
				ObjectCreationExpressionSyntax creation => creation.ArgumentList,
				ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList,
				ConstructorInitializerSyntax initializer => initializer.ArgumentList,
				_ => null,
			};

			if (
				arguments is not null
				&& arguments.Arguments.Any(argument =>
					argument.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter
				)
			)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// The kind of each type that the product source declares by name: <c>value</c> for a struct, record struct or enum, otherwise <c>reference</c>.
	/// </summary>
	private static Dictionary<string, HashSet<string>> DeclaredTypeKinds()
	{
		Dictionary<string, HashSet<string>> kinds = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (SyntaxNode node in file.Root.DescendantNodes())
			{
				(string Name, string Kind)? declared = node switch
				{
					EnumDeclarationSyntax @enum => (@enum.Identifier.ValueText, "value"),
					StructDeclarationSyntax @struct => (@struct.Identifier.ValueText, "value"),

					RecordDeclarationSyntax record => (
						record.Identifier.ValueText,
						record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "value" : "reference"
					),

					BaseTypeDeclarationSyntax type => (type.Identifier.ValueText, "reference"),
					DelegateDeclarationSyntax @delegate => (@delegate.Identifier.ValueText, "reference"),
					_ => null,
				};

				if (declared is null)
					continue;

				if (!kinds.TryGetValue(declared.Value.Name, out HashSet<string>? set))
					kinds[declared.Value.Name] = set = new HashSet<string>(StringComparer.Ordinal);

				set.Add(declared.Value.Kind);
			}
		}

		return kinds;
	}

	/// <summary>
	/// Reports whether the parameter is a non-nullable reference argument, or a generic one that the constraints do not make a struct.
	/// A parameter whose type neither the repository's source nor the value-type list names is read as a reference.
	/// </summary>
	private static bool NeedsGuard(
		ParameterSyntax parameter,
		HashSet<string> generic,
		SyntaxList<TypeParameterConstraintClauseSyntax>[] constraintLists,
		Dictionary<string, HashSet<string>> declaredKinds,
		ProjectFile project
	)
	{
		if (parameter.Type is null || parameter.Default is not null)
			return false;

		if (
			parameter.Modifiers.Any(modifier =>
				modifier.IsKind(SyntaxKind.OutKeyword) || modifier.IsKind(SyntaxKind.RefKeyword) || modifier.IsKind(SyntaxKind.InKeyword)
			)
		)
		{
			return false;
		}

		switch (parameter.Type)
		{
			case NullableTypeSyntax or PointerTypeSyntax or TupleTypeSyntax:
				return false;

			case ArrayTypeSyntax:
				return true;

			case PredefinedTypeSyntax predefined:
				return predefined.Keyword.IsKind(SyntaxKind.StringKeyword) || predefined.Keyword.IsKind(SyntaxKind.ObjectKeyword);
		}

		string name = parameter.Type switch
		{
			GenericNameSyntax genericName => genericName.Identifier.ValueText,
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
			_ => parameter.Type.ToString(),
		};

		if (generic.Contains(name))
			return !HasKeywordConstraint(constraintLists, name, SyntaxKind.StructKeyword);

		// A struct in Avalonia and a class in WPF, so the project's engine decides.
		if (name == "Typeface")
			return project.EditorEngine != "Avalonia";

		if (s_engineValueTypes.Contains(name))
			return false;

		if (declaredKinds.TryGetValue(name, out HashSet<string>? kinds))
			return kinds.Count == 1 && kinds.Contains("reference");

		return true;
	}

	/// <summary>
	/// Reports whether a statement is a guard clause that may precede the <see langword="null"/> guards:
	/// an <c>Argument...Exception.ThrowIf...</c> call, an assignment or declaration that uses <c>?? throw</c>, or an <c>if</c> without an <c>else</c>
	/// whose body only throws.
	/// </summary>
	private static bool IsGuardClause(StatementSyntax statement)
	{
		switch (statement)
		{
			case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }:
				string callee = call.Expression.ToString();
				return callee.StartsWith("Argument", StringComparison.Ordinal) && callee.Contains(".ThrowIf", StringComparison.Ordinal);

			case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }:
				return assignment.Right.DescendantNodesAndSelf().Any(node => node is ThrowExpressionSyntax);

			case LocalDeclarationStatementSyntax local:
				return local.DescendantNodes().Any(node => node is ThrowExpressionSyntax);

			case IfStatementSyntax { Else: null } conditional:
				return conditional.Statement is ThrowStatementSyntax or BlockSyntax { Statements: [ThrowStatementSyntax] };

			default:
				return false;
		}
	}

	/// <summary>
	/// Reports whether the node rejects a <see langword="null"/> value of the parameter: a <c>ThrowIf</c> call on it, <c>?? throw</c>, or an is-null
	/// test of an <c>if</c> that throws. A <c>ThrowIfNull</c> call does not count for an argument that <paramref name="boxes"/>.
	/// </summary>
	private static bool Guards(SyntaxNode node, string parameter, bool boxes)
	{
		foreach (SyntaxNode descendant in node.DescendantNodesAndSelf())
		{
			switch (descendant)
			{
				case InvocationExpressionSyntax call
					when call.Expression.ToString().Contains(".ThrowIf", StringComparison.Ordinal)
						&& !(boxes && call.Expression.ToString().EndsWith(".ThrowIfNull", StringComparison.Ordinal))
						&& call.ArgumentList.Arguments.Count > 0
						&& call.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argument
						&& argument.Identifier.ValueText == parameter:
					return true;

				case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression, Right: ThrowExpressionSyntax } coalesce
					when coalesce.Left is IdentifierNameSyntax left && left.Identifier.ValueText == parameter:
					return true;

				case IsPatternExpressionSyntax { Expression: IdentifierNameSyntax tested }
					when tested.Identifier.ValueText == parameter && node is IfStatementSyntax:
					return true;

				case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } equals
					when equals.Left is IdentifierNameSyntax compared
						&& compared.Identifier.ValueText == parameter
						&& equals.Right.IsKind(SyntaxKind.NullLiteralExpression)
						&& node is IfStatementSyntax:
					return true;
			}
		}

		return false;
	}

	/// <summary>A method or constructor whose parameters the guard reads.</summary>
	private sealed record Callable(
		ParameterListSyntax Parameters,
		SyntaxList<TypeParameterConstraintClauseSyntax> Constraints,
		TypeParameterListSyntax? TypeParameters,
		ConstructorInitializerSyntax? Initializer,
		BlockSyntax? Body,
		ArrowExpressionClauseSyntax? Expression,
		string Name
	);
}
