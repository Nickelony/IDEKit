using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A method that runs asynchronous work for its caller to await ends in <c>Async</c>, with <c>CoreAsync</c> for the inner
/// body behind it. A method that reacts to an event is <c>Handle{Event}</c>: no method is named <c>{Source}_{Event}</c>, and <c>On{Event}</c> names
/// only an override or a hook that a base type or an interface declares.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class MethodNameGuardTests
{
	private const string Guard = nameof(MethodNameGuardTests);

	// A method that returns a task and does not end in Async, keyed "{path}: {type}.{method}". M3 names the methods that start background work and
	// hand back its task for the owner to keep; the others return a task that was started elsewhere.
	private static readonly AllowList s_unsuffixedTaskMethods = new(
		"task-returning methods without the Async suffix",

		(
			"src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/DocumentSymbols/DocumentSymbolQuickPickProvider.cs: DocumentSymbolQuickPickProvider.ShareOutline",
			"The method starts the shared outline request when none is current and hands its task to the outline slot of the owner, which every query of the stay awaits."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Processes/ProcessHandle.cs: ProcessHandle.RequireStandardError",
			"The method returns the capture task that the handle started with the process, or throws when the stream was not redirected, and does no work of its own."
		),

		(
			"src/IDEKit/Nickelony.IDEKit.Processes/ProcessHandle.cs: ProcessHandle.RequireStandardOutput",
			"The method returns the capture task that the handle started with the process, or throws when the stream was not redirected, and does no work of its own."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Connection/OutboundStreamForwarder.cs: OutboundStreamForwarder.Start",
			"The method starts the copy and hands back its task for the owner to keep and observe, as M3 allows."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Connection/StandardErrorReader.cs: StandardErrorReader.Start",
			"The method starts the read loop and hands back its task for the owner to keep and observe, as M3 allows."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Session/BackgroundTaskObserver.cs: BackgroundTaskObserver.StartObservedLoop",
			"The method starts the observed loop and hands back its task for the owner to keep and observe, as M3 allows."
		)
	);

	// A method named On{Event} that is no override, no virtual or abstract hook, and no member of an interface, keyed "{path}: {type}.{method}".
	private static readonly AllowList s_onMethods = new(
		"On methods that are no override or declared hook",

		(
			"src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/DocumentSymbols/DocumentSymbolQuickPickProvider.cs: DocumentSymbolQuickPickProvider.OnDeactivated",
			"The method implements the OnDeactivated hook that IQuickPickProvider declares and the quick-pick controller invokes."
		)
	);

	// A method that a `+=`, `-=`, AddHandler or RemoveHandler names and that is not Handle{Event}, keyed "{path}: {type}.{method}".
	private static readonly AllowList s_subscribedMethods = new("subscribed methods without the Handle prefix");

	[TestMethod]
	public void AMethodThatReturnsATaskEndsInAsyncAndAnInnerBodyEndsInCoreAsync()
	{
		HashSet<string> misorderedCore = new(StringComparer.Ordinal);
		HashSet<string> unsuffixed = new(StringComparer.Ordinal);

		int read = 0;

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (MethodShape method in Methods(file))
			{
				read++;

				if (method.Name.EndsWith("AsyncCore", StringComparison.Ordinal))
				{
					misorderedCore.Add(
						$"{method.Key} ends in AsyncCore. Name an inner body behind an Async method {method.Name[..^"AsyncCore".Length]}CoreAsync."
					);
				}
				else if (method.ReturnsTask && !method.Name.EndsWith("Async", StringComparison.Ordinal) && !method.KeepsAnInheritedName)
				{
					unsuffixed.Add(method.Key);
				}
			}
		}

		Assert.IsGreaterThan(0, read, "The guard read no method, so it checks nothing.");

		List<string> violations = [.. misorderedCore.Order(StringComparer.Ordinal)];

		foreach (string offender in unsuffixed.Order(StringComparer.Ordinal).Where(offender => !s_unsuffixedTaskMethods.Allows(offender)))
			violations.Add(s_unsuffixedTaskMethods.Rejection($"{offender} returns a task and does not end in Async.", Guard));

		violations.AddRange(s_unsuffixedTaskMethods.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Asynchronous method names:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AMethodThatReactsToAnEventIsNamedHandleAndNeverForItsSource()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (MethodShape method in Methods(file))
			{
				if (method.Name.Contains('_', StringComparison.Ordinal))
					offenders.Add($"{method.Key} is named {{Source}}_{{Event}}. Name it Handle{{Event}}.");
			}
		}

		List<string> violations = [.. offenders.Order(StringComparer.Ordinal)];

		Assert.IsEmpty(violations, "Event handler names:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnOnMethodIsAnOverrideOrAHookThatABaseTypeOrAnInterfaceDeclares()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (MethodShape method in Methods(file).Where(method => method.IsOnMethod && !method.IsHook))
				offenders.Add(method.Key);
		}

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_onMethods.Allows(offender)))
		{
			violations.Add(
				s_onMethods.Rejection(
					$"{offender} is named On{{Event}} and is no override, no virtual or abstract method, and no member of an interface. A method that reacts to an event is Handle{{Event}}.",
					Guard
				)
			);
		}

		violations.AddRange(s_onMethods.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "On methods:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AMethodThatASubscriptionNamesIsNamedHandle()
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);
		int subscriptions = 0;

		foreach (ProductFile file in ProductSyntax.Files())
		{
			// A method group is a simple name that the same file declares as a method; a local function, a field and a parameter are no method.
			HashSet<string> declared = new(
				file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(method => method.Identifier.ValueText),
				StringComparer.Ordinal
			);

			foreach (SyntaxNode node in file.Root.DescendantNodes())
			{
				IEnumerable<ExpressionSyntax> subscribed = node switch
				{
					AssignmentExpressionSyntax assignment
						when assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) =>
					[
						assignment.Right,
					],

					InvocationExpressionSyntax call
						when call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddHandler" or "RemoveHandler" } =>
						call.ArgumentList.Arguments.Select(argument => argument.Expression),

					_ => [],
				};

				foreach (ExpressionSyntax expression in subscribed)
				{
					if (expression is not IdentifierNameSyntax { Identifier.ValueText: var name } || !declared.Contains(name))
						continue;

					subscriptions++;
					TypeDeclarationSyntax? owner = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

					if (!name.StartsWith("Handle", StringComparison.Ordinal))
						offenders.Add($"{file.Path}: {(owner is null ? string.Empty : ProductSyntax.NameOf(owner))}.{name}");
				}
			}
		}

		Assert.IsGreaterThan(0, subscriptions, "The guard read no method group that a subscription names, so it checks nothing.");

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !s_subscribedMethods.Allows(offender)))
		{
			violations.Add(
				s_subscribedMethods.Rejection(
					$"{offender} is subscribed to an event and is not named Handle{{Event}}. Name it for the event it reacts to.",
					Guard
				)
			);
		}

		violations.AddRange(s_subscribedMethods.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Subscribed method names:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static IEnumerable<MethodShape> Methods(ProductFile file)
	{
		foreach (SyntaxNode node in file.Root.DescendantNodes())
		{
			if (node is MethodDeclarationSyntax method)
			{
				var owner = method.Parent as TypeDeclarationSyntax;

				yield return new MethodShape(
					Key: $"{file.Path}: {(owner is null ? string.Empty : ProductSyntax.NameOf(owner))}.{method.Identifier.ValueText}",
					Name: method.Identifier.ValueText,
					ReturnsTask: ReturnsTask(method.ReturnType),
					KeepsAnInheritedName: method.ExplicitInterfaceSpecifier is not null || method.Modifiers.Any(SyntaxKind.OverrideKeyword),

					IsHook: owner is InterfaceDeclarationSyntax
						|| method.Modifiers.Any(SyntaxKind.OverrideKeyword)
						|| method.Modifiers.Any(SyntaxKind.VirtualKeyword)
						|| method.Modifiers.Any(SyntaxKind.AbstractKeyword)
				);
			}
			else if (node is LocalFunctionStatementSyntax local)
			{
				TypeDeclarationSyntax? owner = local.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

				yield return new MethodShape(
					Key: $"{file.Path}: {(owner is null ? string.Empty : ProductSyntax.NameOf(owner))}.{local.Identifier.ValueText}",
					Name: local.Identifier.ValueText,
					ReturnsTask: ReturnsTask(local.ReturnType),
					KeepsAnInheritedName: false,
					IsHook: false
				);
			}
		}
	}

	private static bool ReturnsTask(TypeSyntax type)
	{
		string name = type switch
		{
			GenericNameSyntax generic => generic.Identifier.ValueText,
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
			_ => string.Empty,
		};

		return name is "Task" or "ValueTask";
	}

	private readonly record struct MethodShape(string Key, string Name, bool ReturnsTask, bool KeepsAnInheritedName, bool IsHook)
	{
		internal bool IsOnMethod => Name.Length > 2 && Name.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(Name[2]);
	}
}
