// Copyright (c) Microsoft. All rights reserved.

using Xunit;

namespace Microsoft.Agents.AI.Foundry.Hosting.UnitTests;

/// <summary>
/// xUnit collection that serializes tests mutating the <c>ASPNETCORE_URLS</c> process
/// environment variable. Without this, parallel test execution causes flaky races between
/// tests that set / unset the variable.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AspNetCoreUrlsEnvFixture
{
    public const string Name = "AspNetCoreUrlsEnv";
}
