// Copyright (c) Microsoft. All rights reserved.

# Invocations + Telegram hosted side by side

This sample mounts a single `AIAgent` on two `Microsoft.Agents.AI.Hosting.Channels` channels at the same time and shares one `AgentFrameworkHost`, one identity registry, and one isolation-key space across them.

## What it shows

* `AddAgentFrameworkHost(agent)` + `AddInvocationsChannel()` + optional `AddTelegramChannel(...)`
* `UseIdentityLinker<OneTimeCodeIdentityLinker>()` for low-ceremony cross-channel linking
* `MapAgentFrameworkHost()` mounting every channel's routes rooted at the channel `Path`
* Same agent answering both an `/invocations/invoke` POST and Telegram messages
* Cross-channel `IChannelPush` delivery to a Telegram user when linked via the one-time code

## Requirements

* `AZURE_OPENAI_ENDPOINT` set, with `az login` completed (DefaultAzureCredential)
* `AZURE_OPENAI_DEPLOYMENT_NAME` optional; defaults to `gpt-5.4-mini`
* `TELEGRAM_BOT_TOKEN` optional; when omitted the Telegram channel is skipped

## Try it

```bash
cd dotnet/samples/04-hosting/HostingChannels/InvocationsAndTelegram
dotnet run
```

Invocations channel sanity check:

```bash
curl -X POST http://localhost:5000/invocations/invoke \
  -H "Content-Type: application/json" \
  -d '{ "input": "Hi, what can you do?" }'
```

If you supplied a Telegram bot token, message your bot from the Telegram client. Type `/new` on Telegram to rotate the active session alias for your isolation key.