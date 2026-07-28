#requires -Version 7
<#
.SYNOPSIS
  Rewires an already-scaffolded hosted-agent folder to build against the local Agent Framework
  source, so `azd deploy` ships your framework changes instead of the published packages.
.DESCRIPTION
  Source (ZIP) deploy uploads the agent folder and Foundry runs `dotnet restore` + `dotnet publish`
  on it in the cloud. That restore pulls the Agent Framework from nuget.org, so a contributor's
  local framework changes are never exercised.

  Run this after `azd ai agent init` and before `azd provision`. It drops three files into the
  folder that `init` scaffolded:

    local-feed/       The Agent Framework packed from the local source tree, stamped with a version
                      derived from the repo's current VersionPrefix plus a `-preview-local` suffix.
                      The whole closure is packed: packing only the leaf packages lets NuGet fill
                      the rest from nuget.org, mixing a published core with a locally built host.
    nuget.config      Maps Microsoft.Agents.AI* to that folder feed and everything else to nuget.org.
    local-feed.props  Pins the Agent Framework version to the one just packed. The sample's project
                      file imports it when present.

  None of the three are excluded by `.agentignore`, so they travel inside the ZIP and the
  server-side restore uses them.

  Everything else stays identical to the end-user flow: you create the working directory, run
  `azd ai agent init`, and finish with `azd provision`, `azd deploy`, and `azd ai agent invoke`.
.PARAMETER Path
  The folder `azd ai agent init` scaffolded, for example `./hosted-chat-client-agent`.
  Defaults to the current directory.
.EXAMPLE
  # From the working directory, after azd ai agent init created ./hosted-chat-client-agent
  <repo>/dotnet/samples/04-hosting/FoundryHostedAgents/scripts/Add-LocalFrameworkFeed.ps1 -Path ./hosted-chat-client-agent
.EXAMPLE
  # From inside the scaffolded folder
  cd hosted-chat-client-agent
  <repo>/dotnet/samples/04-hosting/FoundryHostedAgents/scripts/Add-LocalFrameworkFeed.ps1
.NOTES
  For contributors validating framework changes end to end. End users skip this script entirely and
  get the published packages.
#>

[CmdletBinding()]
param(
    [string]$Path = '.'
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

$target = (Resolve-Path $Path).Path

if (-not (Test-Path (Join-Path $target 'azure.yaml'))) {
    throw "No azure.yaml in '$target'. Point -Path at the folder 'azd ai agent init' scaffolded."
}

$projectFile = Get-ChildItem $target -Filter *.csproj -File | Select-Object -First 1
if (-not $projectFile) {
    throw "No .csproj in '$target'. This script targets .NET hosted agents."
}

if (-not (Select-String -Path $projectFile.FullName -Pattern 'local-feed\.props' -Quiet)) {
    throw "$($projectFile.Name) does not import local-feed.props, so it cannot pick up a local build."
}

$hostedRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = (Resolve-Path (Join-Path $hostedRoot '..\..\..')).Path
$srcRoot = Join-Path $dotnetRoot 'src'

# Derive the package version from the repo so the packages track the current release line.
# The timestamp keeps every run unique: NuGet caches by id and version, so reusing a version would
# silently restore the previously packed bits instead of the build you just made. It also changes
# the ZIP contents on every run, which matters because Foundry mints a new agent version only when
# the uploaded ZIP changes.
$packagePropsPath = Join-Path $dotnetRoot 'nuget\nuget-package.props'
$versionPrefix = (Select-String -Path $packagePropsPath -Pattern '<VersionPrefix>(.+?)</VersionPrefix>').Matches[0].Groups[1].Value
$version = "$versionPrefix-preview-local.$(Get-Date -Format 'yyyyMMddHHmmss')"

$feedPath = Join-Path $target 'local-feed'
if (Test-Path $feedPath) { Remove-Item $feedPath -Recurse -Force }
New-Item -ItemType Directory -Path $feedPath -Force | Out-Null

Write-Host "Wiring $(Split-Path -Leaf $target) to the local Agent Framework" -ForegroundColor Cyan
Write-Host "  version: $version"
Write-Host ''

foreach ($project in $frameworkProjects) {
    $projectPath = Join-Path $srcRoot "$project\$project.csproj"
    Write-Host "Packing $project..."

    # Debug, not Release: the Release configuration runs the repo's formatting and analyzer passes,
    # which rewrite source files and fail the build on style violations. Packing only needs runnable
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
<!-- Generated by Add-LocalFrameworkFeed.ps1: resolves the Agent Framework from this upload. -->
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
[System.IO.File]::WriteAllText((Join-Path $target 'nuget.config'), ($nugetConfig -replace "`r`n", "`n"), $utf8NoBom)

$props = @"
<Project>
  <!-- Generated by Add-LocalFrameworkFeed.ps1: pins the Agent Framework to the locally packed version. -->
  <PropertyGroup>
    <AgentFrameworkVersion>$version</AgentFrameworkVersion>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText((Join-Path $target 'local-feed.props'), ($props -replace "`r`n", "`n"), $utf8NoBom)

Write-Host ''
Write-Host 'Done. Continue with the standard flow:' -ForegroundColor Green
Write-Host ''
Write-Host "  cd `"$target`""
Write-Host '  azd provision'
Write-Host '  azd deploy'
Write-Host '  azd ai agent invoke "Hello!"'
