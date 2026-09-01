// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Hosting.AzureStorage;

/// <summary>
/// Configuration options for <see cref="AzureBlobAgentSessionStore"/>.
/// </summary>
public sealed class AzureBlobAgentSessionStoreOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to automatically create the container if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>.
    /// Set this to <see langword="false"/> when the supplied identity has data access but cannot create containers.
    /// </remarks>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether reads may fall back to the legacy version 1 blob key.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/> because the legacy key can map a scoped session and an unscoped
    /// session to the same blob. Enable this only during a controlled migration after every application
    /// instance writes the current key format and only when scoped and unscoped session identifiers cannot
    /// coexist. Sessions loaded through the fallback are written with the current key on their next save.
    /// </remarks>
    public bool EnableLegacyKeyFallback { get; set; }

    /// <summary>
    /// Gets or sets the blob name prefix to use for organizing sessions.
    /// </summary>
    /// <remarks>
    /// This can be used to namespace sessions within a container.
    /// For example, setting this to "prod/" will store all blobs under a "prod/" prefix.
    /// The normalized prefix cannot exceed 886 characters.
    /// </remarks>
    public string? BlobNamePrefix { get; set; }
}
