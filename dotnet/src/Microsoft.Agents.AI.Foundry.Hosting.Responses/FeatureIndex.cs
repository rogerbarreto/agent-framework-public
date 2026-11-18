// Copyright (c) Microsoft. All rights reserved.

// Microsoft.Agents.AI.Foundry.Hosting declares an internal FeatureIndex in the parent namespace and grants this
// assembly access to its internals. Declaring this enum in that same namespace would make both definitions
// visible here and fail the build with CS0436, so the Responses feature index lives in a child namespace.
namespace Microsoft.Agents.AI.Foundry.Hosting.Responses;

internal enum FeatureIndex
{
    FoundryHosting = 53,
}
