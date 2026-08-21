// Copyright (c) Microsoft. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Foundry.Hosting.IntegrationTests.TestContainer;

internal static class ResilientWorkflowAgent
{
    // Agent Administration tracks the durable session, not individual process lifetimes.
    // Persist our own incarnation so recovery can prove that a different process continued the work.
    private static readonly string s_processIncarnation = Guid.NewGuid().ToString("N");

    public static AIAgent Create()
    {
        ResilientInputExecutor input = new();
        ResilientWorkExecutor work = new();
        ResilientOutputExecutor output = new();

        return new WorkflowBuilder(input)
            .AddEdge(input, work)
            .AddEdge(work, output)
            .WithOutputFrom(output)
            .Build()
            .AsAIAgent(name: "resilient-workflow-agent");
    }

    private sealed class ResilientInputExecutor()
        : ChatProtocolExecutor("resilient-input", new() { AutoSendTurnToken = false })
    {
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
            base.ConfigureProtocol(protocolBuilder).SendsMessage<string>();

        protected override ValueTask TakeTurnAsync(
            List<ChatMessage> messages,
            IWorkflowContext context,
            bool? emitEvents,
            CancellationToken cancellationToken = default)
        {
            string request = messages.LastOrDefault()?.Text
                ?? throw new InvalidOperationException("The resilient workflow requires an input message.");
            return context.SendMessageAsync(request, cancellationToken: cancellationToken);
        }
    }

    private sealed class ResilientWorkExecutor()
        : Executor<string, string>("resilient-work")
    {
        public override async ValueTask<string> HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            string[] parts = message.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                throw new InvalidOperationException("Expected '<mode>:<token>'.");
            }

            string mode = parts[0];
            string token = parts[1];

            if (string.Equals(mode, "long", StringComparison.Ordinal))
            {
                int delaySeconds = GetLongRunningDelaySeconds();
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
                return $"LONG-RUN-COMPLETE:{token}";
            }

            if (string.Equals(mode, "crash", StringComparison.Ordinal))
            {
                if (TryCreateCrashMarker(token, out string crashedProcessIncarnation))
                {
                    Console.Out.Flush();
                    Console.Error.Flush();
                    Environment.Exit(70);
                    throw new InvalidOperationException("Process termination did not stop execution.");
                }

                if (string.Equals(crashedProcessIncarnation, s_processIncarnation, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The crash recovery stage resumed in the original process.");
                }

                return $"CRASH-RECOVERED:{token}:PROCESS-CHANGED";
            }

            throw new InvalidOperationException($"Unknown resilient workflow mode '{mode}'.");
        }

        private static int GetLongRunningDelaySeconds()
        {
            const int DefaultDelaySeconds = 20;
            string? value = Environment.GetEnvironmentVariable("IT_LONG_RUNNING_DELAY_SECONDS");
            return int.TryParse(value, out int seconds) && seconds > 0 ? seconds : DefaultDelaySeconds;
        }

        private static bool TryCreateCrashMarker(string token, out string crashedProcessIncarnation)
        {
            string home = Environment.GetEnvironmentVariable("HOME")
                ?? throw new InvalidOperationException("HOME is not set.");
            string markerDirectory = Path.Combine(home, ".foundry-hosting-it", "resilient-workflow");
            Directory.CreateDirectory(markerDirectory);

            string markerName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) + ".crashed";
            string markerPath = Path.Combine(markerDirectory, markerName);

            try
            {
                using FileStream marker = new(
                    markerPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
                byte[] incarnation = Encoding.UTF8.GetBytes(s_processIncarnation);
                marker.Write(incarnation);
                marker.Flush(flushToDisk: true);
                crashedProcessIncarnation = s_processIncarnation;
                return true;
            }
            catch (IOException) when (File.Exists(markerPath))
            {
                crashedProcessIncarnation = File.ReadAllText(markerPath, Encoding.UTF8).Trim();
                if (string.IsNullOrWhiteSpace(crashedProcessIncarnation))
                {
                    throw new InvalidOperationException("The crash marker does not contain a process incarnation.");
                }

                return false;
            }
        }
    }

    [YieldsOutput(typeof(string))]
    private sealed class ResilientOutputExecutor()
        : Executor<string>("resilient-output")
    {
        public override async ValueTask HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await context.YieldOutputAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
