// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Foundry.Hosting;

namespace HostedChatClientAgent;

/// <summary>
/// Local-development routing helper for this hosted-agent sample.
/// </summary>
internal static class LocalDevEndpoint
{
    /// <summary>
    /// In Development only, maps the per-agent OpenAI route shape that live Foundry uses
    /// (<c>/api/projects/{project}/agents/{agentName}/endpoint/protocols/openai/responses</c>)
    /// on top of the default <c>MapFoundryResponses()</c>, so a code-first client that talks to
    /// the agent through <c>AIProjectClient.AsAIAgent(Uri agentEndpoint)</c> (see the sibling
    /// <c>Using-Samples</c> REPLs) can reach this server while it runs locally on
    /// <c>http://localhost:8088</c>.
    ///
    /// <para>
    /// The <c>{project}</c> and <c>{agentName}</c> segments are route-parameter wildcards; the
    /// handler does not consume them, so any value the client sends is accepted.
    /// </para>
    ///
    /// <para>
    /// This is a no-op outside Development. The Foundry hosted runtime runs in the Production
    /// environment, so the deployed agent never exposes this route; hosted agents are always
    /// reached through the platform-routed per-agent endpoint.
    /// </para>
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to attach the route to.</param>
    /// <returns>The same <see cref="WebApplication"/> for chaining.</returns>
    public static WebApplication MapLocalDevAgentEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapFoundryResponses("api/projects/{project}/agents/{agentName}/endpoint/protocols/openai");
        }

        return app;
    }
}
