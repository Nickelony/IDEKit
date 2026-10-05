using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// The token parameter that cancels a member's own work is <c>cancellationToken</c> and a logger parameter is <c>logger</c>, so a reader finds
/// the same name in every member and an analyzer or a search by name works across the repository. A token with another job takes a name for that job
/// and carries a row.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class ParameterNameGuardTests
{
	private const string Guard = nameof(ParameterNameGuardTests);

	// A CancellationToken parameter that is not named cancellationToken, keyed "{path}: {type}.{member}({parameter})", where a constructor is
	// ".ctor". The name says the job of the token, so the row states the job.
	private static readonly AllowList s_namedTokens = new(
		"tokens named for their job",

		(
			"src/IDEKit/Nickelony.IDEKit.FileWatching/FileChangeDispatcher.cs: FileChangeDispatcher..ctor(lifetimeToken)",
			"The token is the lifetime of the owner, which ends the dispatch loop the type runs; it cancels no single call."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/Lifecycle/HandshakeRunner.cs: HandshakeRunner.RunAsync(initializeCancellationToken)",
			"The token cancels only the initialize request, and cancellationToken cancels the notifications that follow it."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/ServerMessages/DiagnosticsCoalescer.cs: DiagnosticsCoalescer..ctor(lifetimeToken)",
			"The token is the lifetime of the owner, which ends the drain loop the type runs; it cancels no single call."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/ServerMessages/DrainWorker.cs: DrainWorker..ctor(lifetimeToken)",
			"The token is the lifetime of the owner, which ends the drain loop the type runs; it cancels no single call."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/ServerMessages/ServerMessageDispatcher.cs: ServerMessageDispatcher..ctor(lifetimeToken)",
			"The token is the lifetime of the owner, which ends the drain loop the type runs; it cancels no single call."
		),

		(
			"src/LanguageServer/Nickelony.LanguageServer.Client/ServerMessages/ServerMessageRouter.cs: ServerMessageRouter..ctor(lifetimeToken)",
			"The token is the lifetime of the owner, which ends the routing the type runs; it cancels no single call."
		)
	);

	// A logger parameter that is not named logger, in the same key shape. A positional record parameter names a property, so its first letter
	// may be upper case.
	private static readonly AllowList s_namedLoggers = new("loggers with another name");

	[TestMethod]
	public void ACancellationTokenParameterIsNamedCancellationTokenOrForItsJob()
	{
		List<string> violations = Violations(
			allowed: s_namedTokens,
			typeName: "CancellationToken",
			expectedName: "cancellationToken",
			advice: "Name it cancellationToken, or name it for its job and add a row."
		);

		violations.AddRange(s_namedTokens.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Cancellation token parameters:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ALoggerParameterIsNamedLogger()
	{
		List<string> violations = Violations(allowed: s_namedLoggers, typeName: "ILogger", expectedName: "logger", advice: "Name it logger.");

		violations.AddRange(s_namedLoggers.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Logger parameters:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>
	/// The parameters of the given type, in a member or a record, whose name is not the expected one and that the list does not excuse.
	/// </summary>
	private static List<string> Violations(AllowList allowed, string typeName, string expectedName, string advice)
	{
		HashSet<string> offenders = new(StringComparer.Ordinal);
		int read = 0;

		foreach (ProductFile file in ProductSyntax.Files())
		{
			foreach (ParameterSyntax parameter in file.Root.DescendantNodes().OfType<ParameterSyntax>())
			{
				if (!IsTypeNamed(parameter.Type, typeName) || DeclaringMember(parameter) is not { } member)
					continue;

				read++;
				string name = parameter.Identifier.ValueText;

				// A positional record parameter is also a property, so it is spelled with an upper-case first letter.
				bool matches =
					name == expectedName
					|| (member.IsPositionalRecordParameter && string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase));

				if (!matches)
					offenders.Add($"{file.Path}: {member.Owner}.{member.Name}({name})");
			}
		}

		Assert.IsGreaterThan(0, read, $"The guard read no {typeName} parameter, so it checks nothing.");

		List<string> violations = [];

		foreach (string offender in offenders.Order(StringComparer.Ordinal).Where(offender => !allowed.Allows(offender)))
			violations.Add(allowed.Rejection($"{offender} is a {typeName} parameter, and it is not named {expectedName}. {advice}", Guard));

		return violations;
	}

	private static bool IsTypeNamed(TypeSyntax? type, string name)
	{
		TypeSyntax? inner = type is NullableTypeSyntax nullable ? nullable.ElementType : type;

		return inner switch
		{
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText == name,
			QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == name,
			_ => false,
		};
	}

	/// <summary>
	/// The member, local function, delegate or record that declares the parameter, or <see langword="null"/> for a lambda or an accessor.
	/// </summary>
	private static (string Owner, string Name, bool IsPositionalRecordParameter)? DeclaringMember(ParameterSyntax parameter)
	{
		SyntaxNode? declaration = parameter.Parent?.Parent;
		TypeDeclarationSyntax? owner = parameter.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

		string? name = declaration switch
		{
			MethodDeclarationSyntax method => method.Identifier.ValueText,
			ConstructorDeclarationSyntax => ".ctor",
			LocalFunctionStatementSyntax local => local.Identifier.ValueText,
			DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier.ValueText,
			TypeDeclarationSyntax => ".ctor",
			_ => null,
		};

		if (name is null)
			return null;

		string ownerName =
			declaration is TypeDeclarationSyntax primary ? ProductSyntax.NameOf(primary)
			: owner is null ? string.Empty
			: ProductSyntax.NameOf(owner);

		return (ownerName, name, declaration is RecordDeclarationSyntax);
	}
}
