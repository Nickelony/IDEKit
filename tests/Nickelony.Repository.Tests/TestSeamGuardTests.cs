using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A test seam in product code stays inert in production: a test hook runs only when a test installed it, a hooks type hands out no settable factory,
/// and no type keeps static mutable state that a test could set.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class TestSeamGuardTests
{
	private const string Guard = nameof(TestSeamGuardTests);

	// A static field that is not readonly, keyed "{path}: {type}.{field}".
	private static readonly AllowList s_staticFields = new(
		"static mutable fields",

		(
			"src/IDEKit/Nickelony.IDEKit.Core/Highlighting/RuleSetCache.cs: RuleSetCache.s_buildsInProgress",
			"A thread-static re-entrancy guard of one cache, which no test sets."
		)
	);

	[TestMethod]
	public void ATestHookRunsOnlyThroughANullConditionalInvokeOrAfterANullCheckAndAnAwait()
	{
		HashSet<string> hookNames = new(StringComparer.Ordinal);

		foreach (
			(_, TypeDeclarationSyntax type) in ProductSyntax
				.Types()
				.Where(entry => entry.Type.Identifier.ValueText.EndsWith("TestHooks", StringComparison.Ordinal))
		)
		{
			foreach (
				PropertyDeclarationSyntax property in type.Members.OfType<PropertyDeclarationSyntax>().Where(property => IsDelegate(property.Type))
			)
			{
				hookNames.Add(property.Identifier.ValueText);
			}
		}

		Assert.IsNotEmpty(hookNames, "No test hook was found, so the guard checks nothing.");

		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (InvocationExpressionSyntax call in file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
			{
				(string? name, ExpressionSyntax? hook, bool nullConditional) = HookOf(call);

				if (name is null || hook is null || !hookNames.Contains(name) || nullConditional)
					continue;

				if (!IsAwaitedAfterNullCheck(call, hook))
				{
					int line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

					violations.Add(
						$"{file.Path}:{line}: {call.ToString().ReplaceLineEndings(" ")} calls a test hook without ?.Invoke() and without a null check around an await."
					);
				}
			}
		}

		Assert.IsEmpty(
			violations,
			"Test hook calls:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void AHooksTypeHoldsNoSettableFactory()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (
			(ProductFile file, TypeDeclarationSyntax type) in ProductSyntax
				.Types()
				.Where(entry => entry.Type.Identifier.ValueText.EndsWith("Hooks", StringComparison.Ordinal))
		)
		{
			foreach (PropertyDeclarationSyntax property in type.Members.OfType<PropertyDeclarationSyntax>())
			{
				bool settable = property.AccessorList?.Accessors.Any(accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration)) == true;

				if (settable && IsFactory(property.Type))
					violations.Add($"{file.Path}: {ProductSyntax.NameOf(type)}.{property.Identifier.ValueText} is a settable factory.");
			}
		}

		Assert.IsEmpty(
			violations,
			"Settable factories on hooks types:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void NoProductTypeKeepsStaticMutableState()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach ((ProductFile file, TypeDeclarationSyntax type) in ProductSyntax.Types())
		{
			foreach (MemberDeclarationSyntax member in type.Members.Where(member => member.Modifiers.Any(SyntaxKind.StaticKeyword)))
			{
				string? name = member switch
				{
					FieldDeclarationSyntax field
						when !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword) => field
						.Declaration
						.Variables[0]
						.Identifier
						.ValueText,

					PropertyDeclarationSyntax property
						when property.AccessorList?.Accessors.Any(accessor =>
							accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration)
						) == true => property.Identifier.ValueText,

					_ => null,
				};

				if (name is not null)
					offenders.Add($"{file.Path}: {ProductSyntax.NameOf(type)}.{name}");
			}
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal))
		{
			if (!s_staticFields.Allows(offender))
				violations.Add(s_staticFields.Rejection($"{offender} is static state that code can change.", Guard));
		}

		violations.AddRange(s_staticFields.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Static mutable state:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>
	/// The hook member that a call invokes, the expression that reads the hook, and whether the call goes through <c>?.Invoke()</c>.
	/// A call of a member that is no delegate invocation yields a <see langword="null"/> name.
	/// </summary>
	private static (string? Name, ExpressionSyntax? Hook, bool NullConditional) HookOf(InvocationExpressionSyntax call)
	{
		return call.Expression switch
		{
			// hooks.Name?.Invoke(...)
			MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Invoke" } when call.Parent is ConditionalAccessExpressionSyntax conditional =>
				(LastName(conditional.Expression), conditional.Expression, true),

			// hooks.Name.Invoke(...)
			MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } invoke => (LastName(invoke.Expression), invoke.Expression, false),

			// hooks.Name(...) and Name(...)
			_ => (LastName(call.Expression), call.Expression, false),
		};
	}

	private static string? LastName(ExpressionSyntax expression)
	{
		return expression switch
		{
			MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed => LastName(suppressed.Operand),
			_ => null,
		};
	}

	/// <summary>Reports whether the call is awaited inside an <c>if</c> whose condition tests the same hook against <see langword="null"/>.</summary>
	private static bool IsAwaitedAfterNullCheck(InvocationExpressionSyntax call, ExpressionSyntax hook)
	{
		if (!call.Ancestors().TakeWhile(node => node is not StatementSyntax).Any(node => node is AwaitExpressionSyntax))
			return false;

		string text = hook.ToString();

		return call.Ancestors()
			.OfType<IfStatementSyntax>()
			.Any(statement =>
			{
				string condition = statement.Condition.ToString();

				return condition.Contains($"{text} is not null", StringComparison.Ordinal)
					|| condition.Contains($"{text} != null", StringComparison.Ordinal);
			});
	}

	private static bool IsDelegate(TypeSyntax type)
	{
		TypeSyntax unwrapped = type is NullableTypeSyntax nullable ? nullable.ElementType : type;
		return unwrapped is GenericNameSyntax { Identifier.ValueText: "Action" or "Func" } or IdentifierNameSyntax { Identifier.ValueText: "Action" };
	}

	/// <summary>
	/// Reports whether the type is a function that produces a value other than a task, so a test could replace what the type creates.
	/// </summary>
	private static bool IsFactory(TypeSyntax type)
	{
		TypeSyntax unwrapped = type is NullableTypeSyntax nullable ? nullable.ElementType : type;

		if (unwrapped is not GenericNameSyntax { Identifier.ValueText: "Func" } function)
			return false;

		TypeSyntax result = function.TypeArgumentList.Arguments[^1];

		return result is not IdentifierNameSyntax { Identifier.ValueText: "Task" or "ValueTask" };
	}
}
