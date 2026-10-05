#Requires -Version 5.1
<#
.SYNOPSIS
	Formats the repository with stanza-format and stanza-markup, built from the Stanza checkout.

.DESCRIPTION
	Without a switch, formats the C# files git reports as changed, through the projects that compile them,
	then every markup file of the repository.
	Both tools are built from the checkout that StanzaRepoRoot names, as Directory.Build.props resolves it.
	That build builds the Stanza analyzers and their code fixes in Debug, the output `dotnet format` loads,
	so a run never applies a stale rule or misses a fix.
	stanza-format formats a file that several projects compile once per view, so a shared body is never merged
	from views that disagree.
	The Stanza format command reference lists the steps and the checks:
	https://github.com/Nickelony/Stanza/wiki/Format-command-reference

.PARAMETER All
	Formats the whole solution.

.PARAMETER Check
	Checks the whole solution and the markup files without writing, and fails when a step would change a file.
	Both checks run whatever the first reports, and the exit code is the worse of the two.

.EXAMPLE
	./format.ps1
#>
[CmdletBinding()]
param(
	[switch]$All,
	[switch]$Check
)

# The error preference stays Continue: each exit code is checked below,
# and under Stop, Windows PowerShell would end the run at a line a tool writes to a redirected error stream.
Set-StrictMode -Version Latest

$stanzaRoot = $env:StanzaRepoRoot

if ([string]::IsNullOrWhiteSpace($stanzaRoot)) {
	$stanzaRoot = Join-Path $PSScriptRoot '..\Stanza'
}

$format = Join-Path $stanzaRoot 'src/Nickelony.Stanza.Format/Nickelony.Stanza.Format.csproj'
$markup = Join-Path $stanzaRoot 'src/Nickelony.Stanza.Markup/Nickelony.Stanza.Markup.csproj'

foreach ($project in @($format, $markup)) {
	if (-not (Test-Path -LiteralPath $project)) {
		Write-Host "No Stanza checkout holds $project. Set StanzaRepoRoot to the checkout." -ForegroundColor Red
		exit 2
	}
}

# The build runs no analyzer, so a Stanza layout finding in the tools' own sources cannot stop them from building.
foreach ($project in @($format, $markup)) {
	& dotnet build $project -c Debug --nologo --verbosity quiet -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false

	if ($LASTEXITCODE -ne 0) {
		Write-Host "Failed to build $(Split-Path $project -Leaf) and the Stanza analyzers." -ForegroundColor Red
		exit $LASTEXITCODE
	}
}

$arguments = @()

if ($All) {
	$arguments += '--all'
}

if ($Check) {
	$arguments += '--check'
}

& dotnet run --project $format -c Debug --no-build -- @arguments $PSScriptRoot
$formatExitCode = $LASTEXITCODE

# A write run that failed leaves the markup files for the next run, but a check reports what each tool finds.
if ($formatExitCode -ne 0 -and -not $Check) {
	exit $formatExitCode
}

# The tool manifest pins released tools only, so stanza-format finds no stanza-markup there, and the one built here runs instead.
$command = if ($Check) { @('check', '.', '--strict') } else { @('format', '.') }

Push-Location $PSScriptRoot

try {
	& dotnet run --project $markup -c Debug --no-build -- @command
	$markupExitCode = $LASTEXITCODE
}
finally {
	Pop-Location
}

exit [Math]::Max($formatExitCode, $markupExitCode)
