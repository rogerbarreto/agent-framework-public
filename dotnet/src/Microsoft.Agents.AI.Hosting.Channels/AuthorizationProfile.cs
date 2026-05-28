// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.Channels;

/// <summary>
/// Convenience factory for the common allowlist shapes. Mirrors Python's named <c>AuthPolicy</c> factories.
/// </summary>
public static class AuthorizationProfile
{
    /// <summary>Open: admit every identity, auto-issue isolation keys on first contact.</summary>
    public static IIdentityAllowlist Open() => AllowAllIdentityAllowlist.Instance;

    /// <summary>Force a link ceremony but otherwise admit every linked identity.</summary>
    public static IIdentityAllowlist ForcedLink() => AllowAllIdentityAllowlist.Instance;

    /// <summary>Admit only identities whose channel-native id is in the configured set.</summary>
    public static IIdentityAllowlist NativeAllowlist(string channel, params string[] nativeIds) =>
        new NativeIdAllowlist(channel, nativeIds);

    /// <summary>Admit only identities whose verified claim matches one of the values (forces link).</summary>
    public static IIdentityAllowlist LinkedClaimAllowlist(string claim, params string[] values) =>
        new LinkedClaimAllowlist(claim, values);

    /// <summary>Native ids bypass link; everyone else funnels into a linked-claim allowlist.</summary>
    public static IIdentityAllowlist Mixed(IIdentityAllowlist nativeAllowlist, IIdentityAllowlist linkedClaimAllowlist) =>
        new AnyOfIdentityAllowlist(nativeAllowlist, linkedClaimAllowlist);
}