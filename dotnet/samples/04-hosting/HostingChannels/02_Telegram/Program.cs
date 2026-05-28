// Copyright (c) Microsoft. All rights reserved.

// Mounts an AIAgent on the Telegram channel and serves messages via long-poll getUpdates.
// Per-conversation isolation (ConversationScope.PerUserPerConversation) keeps memory separate
// between the same user's DM and any groups the bot is added to. Group filtering accepts only
// @-mentions by default.

#pragma warning disable CA1031 // demo-only top-level exception handling

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.Channels;
using Microsoft.Agents.AI.Hosting.Channels.Telegram;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using OpenAI.Chat;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
    ?? throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is not set. Create a bot with @BotFather and set the token to run this sample.");

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient(deploymentName)
    .AsAIAgent(name: "Concierge", instructions: "You are a helpful concierge.");

var builder = WebApplication.CreateBuilder(args);

// <add_host>
builder.AddAgentFrameworkHost(agent)
    .AddTelegramChannel(o =>
    {
        o.BotToken = telegramToken;
        o.Transport = TelegramTransport.Polling;
        o.ConversationScope = ConversationScope.PerUserPerConversation;
        o.AcceptInGroup = AcceptInGroup.MentionOnly;
        o.Commands.Add(new ChannelCommand("new", "Start a fresh conversation"));
    });
// </add_host>

var app = builder.Build();
app.MapAgentFrameworkHost();
app.Run();