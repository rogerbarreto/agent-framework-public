// Copyright (c) Microsoft. All rights reserved.

// This sample mounts ONE agent on TWO hosting channels at the same time:
//   * Invocations  - POST /invocations/invoke for JSON-only callers (curl / SDK / test bot)
//   * Telegram     - long-poll bot accepting messages, with cross-channel push back to peers
// Both channels share a single AgentFrameworkHost, so a Telegram user and an Invocations caller
// who link their identities via the OneTimeCodeIdentityLinker resolve to the same isolation key
// and therefore the same AgentSession.

#pragma warning disable CA1031 // demo-only top-level exception handling

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Hosting.Channels.Invocations;
using Microsoft.Agents.AI.Hosting.Channels.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

var builder = WebApplication.CreateBuilder(args);

// <add_host>
var host = builder.AddAgentFrameworkHost(agent, options =>
    {
        // Allow any identity for the demo; auto-issued isolation keys.
        options.DefaultAllowlist = AuthorizationProfile.Open();
    })
    .UseIdentityLinker<OneTimeCodeIdentityLinker>();
// </add_host>

// <add_channels>
host.AddInvocationsChannel();

if (!string.IsNullOrEmpty(telegramToken))
{
    host.AddTelegramChannel(o =>
    {
        o.BotToken = telegramToken;
        o.Transport = TelegramTransport.Polling;
        o.ConversationScope = ConversationScope.PerUserPerConversation;
        o.AcceptInGroup = AcceptInGroup.MentionOnly;
        o.Commands.Add(new ChannelCommand("new", "Start a fresh conversation"));
    });
    Console.WriteLine("Telegram channel enabled.");
}
else
{
    Console.WriteLine("TELEGRAM_BOT_TOKEN not set; Telegram channel disabled. Invocations channel is still available at /invocations/invoke.");
}
// </add_channels>

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();