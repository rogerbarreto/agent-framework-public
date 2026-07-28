// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// xUnit collection that serializes tests mutating the <c>ASPNETCORE_URLS</c> and
/// <c>FOUNDRY_HOSTING_ENVIRONMENT</c> process environment variables. Without this, parallel test
/// execution causes flaky races between tests that set / unset those variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostingEnvFixture
{
    public const string Name = "HostingEnv";
}
