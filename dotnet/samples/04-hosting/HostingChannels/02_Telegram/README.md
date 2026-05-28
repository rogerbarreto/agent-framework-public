# Telegram channel

Mounts an `AIAgent` on the Telegram channel from `Microsoft.Agents.AI.Hosting.Channels.Telegram` and serves messages via long-poll `getUpdates`.

## What it shows

* `IHostApplicationBuilder.AddAgentFrameworkHost(agent)` → `AddTelegramChannel(...)`
* `TelegramTransport.Polling` driven from the channel's `OnStartup` hook (no public HTTP route)
* `ConversationScope.PerUserPerConversation` so the bot's memory is scoped per user per chat
* `AcceptInGroup.MentionOnly` filters out group chatter not directed at the bot
* `ChannelCommand` registered with Telegram via `setMyCommands` at startup

## Requirements

* `AZURE_OPENAI_ENDPOINT` set, `az login` completed (DefaultAzureCredential)
* `AZURE_OPENAI_DEPLOYMENT_NAME` optional; defaults to `gpt-5.4-mini`
* `TELEGRAM_BOT_TOKEN` from @BotFather (required)

## Try it

```bash
cd dotnet/samples/04-hosting/HostingChannels/02_Telegram
dotnet run
```

Open Telegram, find your bot, and send a message. In a group, add the bot and mention it (`@your_bot hello`) to trigger a reply.

## Switching to webhook transport

Set `o.Transport = TelegramTransport.Webhook` in `Program.cs`, register your public URL with Telegram via `setWebhook` (out of band), and the channel publishes `POST /telegram/webhook` for inbound updates.