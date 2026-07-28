#requires -Version 7
<#
.SYNOPSIS
  Stages a hosted-agent sample in a temporary folder wired to build against the local
  Agent Framework source, so `azd` deploys your framework changes instead of the published packages.
.DESCRIPTION
  Source (ZIP) deploy uploads the sample folder and Foundry runs `dotnet restore` + `dotnet publish`
  on it in the cloud. That restore pulls the Agent Framework from nuget.org, so a contributor's local
  framework changes are never exercised.

  This script produces a staging copy whose restore resolves the Agent Framework from packages
  carried inside the upload:

    1. Copies the sample into a fresh temp folder (the repo working tree is left untouched).
    2. Builds and packs the Agent Framework projects the samples depend on into `src/local-feed`,
       stamped with a version derived from the repo's current VersionPrefix plus a `-preview-local`
       suffix. The whole closure is packed: packing only the leaf packages lets NuGet fill the rest
       from nuget.org, mixing a published core with a locally built host.
    3. Writes `src/nuget.config`, mapping Microsoft.Agents.AI* to that folder feed and everything
       else to nuget.org.
    4. Writes `src/local-feed.props`, which the sample's project file imports to pin the Agent
       Framework version to the one just packed.

  `local-feed/`, `nuget.config`, and `local-feed.props` are not excluded by `.agentignore`, so they
  travel inside the ZIP and the server-side restore uses them.

  The staged layout keeps the standard end-user flow intact: `run/` is the empty working directory
  you run `azd` from, and `src/` is the sample that `azd ai agent init -m` adopts. The script prints
  the exact commands to run when it finishes.
.PARAMETER Sample
  Sample folder name under `responses/` or `invocations/`, for example `Hosted-ChatClientAgent`.
.PARAMETER ProjectId
  Optional Foundry project resource ID, used only to print a ready-to-paste `init` command.
.PARAMETER ModelDeployment
  Optional model deployment name, used only to print a ready-to-paste `init` command.
.EXAMPLE
  ./New-ContributorStage.ps1 -Sample Hosted-ChatClientAgent
.EXAMPLE
  ./New-ContributorStage.ps1 -Sample Hosted-ChatClientAgent -ProjectId "/subscriptions/.../projects/my-project" -ModelDeployment gpt-5.4-mini
.NOTES
  For contributors validating framework changes end to end. End users deploy the sample directly
  from the repo folder and get the published packages.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Sample,

    [string]$ProjectId,

    [string]$ModelDeployment
)

$ErrorActionPreference = 'Stop'

# The Agent Framework closure the hosted samples resolve. Packing only the leaf packages makes
# NuGet satisfy the rest from nuget.org, producing assembly-reference errors at build time.
$frameworkProjects = @(
    'Microsoft.Agents.AI.Abstractions'
    'Microsoft.Agents.AI'
    'Microsoft.Agents.AI.Workflows'
    'Microsoft.Agents.AI.Foundry'
    'Microsoft.Agents.AI.Foundry.Hosting'
)

$hostedRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = (Resolve-Path (Join-Path $hostedRoot '..\..\..')).Path
$srcRoot = Join-Path $dotnetRoot 'src'

$samplePath = @('responses', 'invocations')
| ForEach-Object { Join-Path $hostedRoot "$_\$Sample" }
| Where-Object { Test-Path $_ }
| Select-Object -First 1

if (-not $samplePath) {
    throw "Sample '$Sample' not found under $hostedRoot\responses or $hostedRoot\invocations."
}

# Derive the package version from the repo so the staged packages track the current release line.
# The timestamp keeps every run unique: NuGet caches by id and version, so reusing a version would
# silently restore the previously packed bits instead of the build you just made.
$packagePropsPath = Join-Path $dotnetRoot 'nuget\nuget-package.props'
$versionPrefix = (Select-String -Path $packagePropsPath -Pattern '<VersionPrefix>(.+?)</VersionPrefix>').Matches[0].Groups[1].Value
$version = "$versionPrefix-preview-local.$(Get-Date -Format 'yyyyMMddHHmmss')"

$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) "af-hosted-$Sample-$([System.IO.Path]::GetRandomFileName().Substring(0, 8))"
$stageSrc = Join-Path $stageRoot 'src'
$stageRun = Join-Path $stageRoot 'run'
$feedPath = Join-Path $stageSrc 'local-feed'

New-Item -ItemType Directory -Path $stageSrc, $stageRun, $feedPath -Force | Out-Null

Write-Host "Staging $Sample" -ForegroundColor Cyan
Write-Host "  version: $version"
Write-Host "  stage:   $stageRoot"
Write-Host ''

# Local-only state (.env, build output, azd environments) must not leak into the staged copy.
$excludedDirs = @('.azure', '.checkpoints', 'bin', 'obj', 'scripts')
$excludedFiles = @('.env')
robocopy $samplePath $stageSrc /E /XD $excludedDirs /XF $excludedFiles /NFL /NDL /NP /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "Failed to copy the sample (robocopy exit code $LASTEXITCODE)."
}

foreach ($project in $frameworkProjects) {
    $projectPath = Join-Path $srcRoot "$project\$project.csproj"
    Write-Host "Packing $project..."

    # Debug, not Release: the Release configuration runs the repo's formatting and analyzer passes,
    # which rewrite source files and fail the build on style violations. Staging only needs runnable
    # binaries, so Debug keeps the working tree untouched.
    #
    # PackageVersion (not Version) is the property the repo's packaging props use to stamp both the
    # package version and its dependency ranges, so the packed packages reference each other at this
    # version instead of the bare VersionPrefix.
    dotnet build $projectPath -c Debug -p:PackageVersion=$version --tl:off | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $project." }

    dotnet pack $projectPath -c Debug --no-build -o $feedPath -p:PackageVersion=$version --tl:off | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Pack failed for $project." }
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$nugetConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<!-- Generated for contributor staging: resolves the Agent Framework from the packages in this upload. -->
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="./local-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-feed">
      <package pattern="Microsoft.Agents.AI*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
'@
[System.IO.File]::WriteAllText((Join-Path $stageSrc 'nuget.config'), ($nugetConfig -replace "`r`n", "`n"), $utf8NoBom)

$props = @"
<Project>
  <!-- Generated for contributor staging: pins the Agent Framework to the locally packed version. -->
  <PropertyGroup>
    <AgentFrameworkVersion>$version</AgentFrameworkVersion>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText((Join-Path $stageSrc 'local-feed.props'), ($props -replace "`r`n", "`n"), $utf8NoBom)

$initArguments = "-m `"$stageSrc\azure.yaml`""
if ($ProjectId) { $initArguments += " -p `"$ProjectId`"" }
if ($ModelDeployment) { $initArguments += " -d $ModelDeployment" }

# `azd ai agent init -m` scaffolds into a folder named after the top-level `name` in azure.yaml,
# which is the agent name and does not have to match the sample's folder name.
$scaffoldName = (Select-String -Path (Join-Path $stageSrc 'azure.yaml') -Pattern '(?m)^name:\s*(\S+)').Matches[0].Groups[1].Value

Write-Host ''
Write-Host 'Stage ready. Run the standard flow against it:' -ForegroundColor Green
Write-Host ''
Write-Host "  cd `"$stageRun`""
Write-Host "  azd ai agent init $initArguments"
Write-Host "  cd $scaffoldName"
Write-Host '  azd provision'
Write-Host '  azd deploy'
Write-Host '  azd ai agent invoke "Hello!"'
Write-Host ''
Write-Host "Delete $stageRoot when you are done."
