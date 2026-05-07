// Copyright (c) Microsoft. All rights reserved.

// Session Files Client - A REPL that demonstrates the alpha
// Azure.AI.Projects.AgentSessionFiles API end to end against a deployed
// Hosted-Files agent.
//
// On startup, the REPL:
//   1. Resolves a deployed Hosted-Files agent via AgentAdministrationClient.
//   2. Creates a new session (with isolation key) and waits until it is Active.
//   3. Builds a per-agent ProjectResponsesClient bound to the same agent endpoint.
//
// REPL commands:
//   upload <local-path> [<remote-path>]   Upload a local file into $HOME/<remote-path>.
//   ls [<remote-path>]                    List session files at the given path (default: ".").
//   download <remote-path> <local-path>   Download a session file to a local path.
//   rm <remote-path>                      Delete a session file.
//   ask <prompt>                          Send a prompt to the agent. The request body is
//                                         pinned to this REPL's agent_session_id so the agent
//                                         container reads files this REPL uploaded.
//   help                                  Show command reference.
//   quit                                  Delete session and exit.
//
// Required environment variables:
//   FOUNDRY_PROJECT_ENDPOINT   - Azure AI Foundry project endpoint
//   HOSTED_AGENT_NAME          - Deployed agent name (e.g., hosted-files)
//
// Optional:
//   HOSTED_AGENT_VERSION       - Specific agent version (default: latest deployed version)

#pragma warning disable AAIP001 // AgentSessionFiles is experimental
#pragma warning disable OPENAI001 // CreateResponseOptions is experimental
#pragma warning disable SCME0001 // CreateResponseOptions.Patch is for evaluation purposes

using System.ClientModel.Primitives;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using DotNetEnv;
using OpenAI.Responses;

// Bypass the SampleEnvironment alias for optional env vars (which prompts when missing).
using SystemEnvironment = System.Environment;

Env.TraversePath().Load();

string projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
string agentName = Environment.GetEnvironmentVariable("HOSTED_AGENT_NAME")
    ?? throw new InvalidOperationException("HOSTED_AGENT_NAME is not set.");
string? agentVersionEnv = SystemEnvironment.GetEnvironmentVariable("HOSTED_AGENT_VERSION");

const string FoundryFeatures = "HostedAgents=V1Preview,AgentEndpoints=V1Preview";
var endpointUri = new Uri(projectEndpoint);
var credential = new AzureCliCredential();

// ── Build the AgentAdministrationClient with Foundry-Features header ─────────

var adminOptions = new AgentAdministrationClientOptions();
adminOptions.AddPolicy(new FeaturePolicy(FoundryFeatures), PipelinePosition.PerCall);

var agentsClient = new AgentAdministrationClient(endpointUri, credential, adminOptions);
AgentSessionFiles sessionFiles = agentsClient.GetAgentSessionFiles();

// ── Resolve the agent version ────────────────────────────────────────────────

ProjectsAgentVersion agentVersion = agentVersionEnv is null
    ? await GetLatestAgentVersionAsync(agentsClient, agentName)
    : await agentsClient.GetAgentVersionAsync(agentName, agentVersionEnv);

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"Agent: {agentVersion.Name} (version {agentVersion.Version})");
Console.ResetColor();

// ── Build the per-agent ProjectResponsesClient (Foundry-Features required) ──
// AgentName on ProjectOpenAIClientOptions selects the per-agent URL suffix
// `/agents/{name}/endpoint/protocols/openai`. Without it the client targets
// the project-level URL and cannot serve a hosted agent.

var openAIOptions = new ProjectOpenAIClientOptions { AgentName = agentVersion.Name };
openAIOptions.AddPolicy(new FeaturePolicy(FoundryFeatures), PipelinePosition.PerCall);
ProjectResponsesClient responsesClient = new ProjectOpenAIClient(endpointUri, credential, openAIOptions)
    .GetProjectResponsesClient();

// ── Create a session and wait until it is Active ─────────────────────────────

string isolationKey = Guid.NewGuid().ToString("N");
string requestedSessionId = Guid.NewGuid().ToString("N");

ProjectAgentSession session = await agentsClient.CreateSessionAsync(
    agentName: agentVersion.Name,
    isolationKey: isolationKey,
    versionIndicator: new VersionRefIndicator(agentVersion.Version),
    agentSessionId: requestedSessionId);

Console.WriteLine($"Created session: {session.AgentSessionId} (waiting for Active state...)");

while (session.Status != AgentSessionStatus.Active && session.Status != AgentSessionStatus.Failed)
{
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    session = await agentsClient.GetSessionAsync(agentVersion.Name, session.AgentSessionId);
}

if (session.Status == AgentSessionStatus.Failed)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Session creation failed: {session.AgentSessionId}");
    Console.ResetColor();
    return;
}

string sessionId = session.AgentSessionId;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""

    ══════════════════════════════════════════════════════════
    Session Files REPL
    Agent:      {agentVersion.Name}
    Session:    {sessionId}
    Isolation:  {isolationKey}

    Type 'help' for commands, 'quit' to delete the session and exit.
    ══════════════════════════════════════════════════════════
    """);
Console.ResetColor();

// ── REPL ─────────────────────────────────────────────────────────────────────

try
{
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("files> ");
        Console.ResetColor();

        string? line = Console.ReadLine();
        if (line is null)
        {
            break;
        }

        line = line.Trim();
        if (line.Length == 0)
        {
            continue;
        }

        if (line.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        try
        {
            await DispatchAsync(line, agentVersion.Name, sessionId, sessionFiles, responsesClient);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
    }
}
finally
{
    Console.WriteLine($"Deleting session {sessionId}...");
    try
    {
        await agentsClient.DeleteSessionAsync(agentVersion.Name, sessionId, isolationKey: isolationKey);
        Console.WriteLine("Session deleted.");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to delete session: {ex.Message}");
        Console.ResetColor();
    }
}

// ── Command handlers ─────────────────────────────────────────────────────────

static async Task DispatchAsync(string line, string agentName, string sessionId, AgentSessionFiles files, ProjectResponsesClient responses)
{
    string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string command = parts[0].ToUpperInvariant() switch
    {
        "HELP" => "help",
        "UPLOAD" => "upload",
        "LS" or "LIST" => "ls",
        "DOWNLOAD" or "GET" => "download",
        "RM" or "DELETE" => "rm",
        "ASK" => "ask",
        _ => parts[0],
    };

    string[] args = command == "ask"
        ? parts
        : line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);

    switch (command)
    {
        case "help":
            PrintHelp();
            break;

        case "upload":
            await UploadAsync(args, agentName, sessionId, files);
            break;

        case "ls":
            await ListAsync(args, agentName, sessionId, files);
            break;

        case "download":
            await DownloadAsync(args, agentName, sessionId, files);
            break;

        case "rm":
            await DeleteAsync(args, agentName, sessionId, files);
            break;

        case "ask":
            await AskAsync(args, sessionId, responses);
            break;

        default:
            Console.WriteLine($"Unknown command '{parts[0]}'. Type 'help'.");
            break;
    }
}

static async Task UploadAsync(string[] parts, string agentName, string sessionId, AgentSessionFiles files)
{
    if (parts.Length < 2)
    {
        Console.WriteLine("Usage: upload <local-path> [<remote-path>]");
        return;
    }

    string localPath = parts[1];
    string remotePath = parts.Length >= 3 ? parts[2] : Path.GetFileName(localPath);

    var response = await files.UploadSessionFileAsync(agentName, sessionId, remotePath, localPath);
    Console.WriteLine($"Uploaded {response.Value.BytesWritten} bytes to {response.Value.Path}");
}

static async Task ListAsync(string[] parts, string agentName, string sessionId, AgentSessionFiles files)
{
    string remotePath = parts.Length >= 2 ? parts[1] : ".";
    var response = await files.GetSessionFilesAsync(agentName, sessionId, remotePath);

    Console.WriteLine($"{response.Value.Path}:");
    foreach (var entry in response.Value.Entries)
    {
        string kind = entry.IsDirectory ? "<DIR>" : entry.Size.ToString().PadLeft(8);
        Console.WriteLine($"  {kind}  {entry.Name}");
    }
}

static async Task DownloadAsync(string[] parts, string agentName, string sessionId, AgentSessionFiles files)
{
    if (parts.Length < 3)
    {
        Console.WriteLine("Usage: download <remote-path> <local-path>");
        return;
    }

    await files.DownloadSessionFileAsync(agentName, sessionId, parts[1], parts[2]);
    Console.WriteLine($"Downloaded {parts[1]} -> {parts[2]}");
}

static async Task DeleteAsync(string[] parts, string agentName, string sessionId, AgentSessionFiles files)
{
    if (parts.Length < 2)
    {
        Console.WriteLine("Usage: rm <remote-path>");
        return;
    }

    await files.DeleteSessionFileAsync(agentName, sessionId, parts[1]);
    Console.WriteLine($"Deleted {parts[1]}");
}

// ── Agent invocation pinned to this REPL's agent_session_id ──────────────────
// agent_session_id is a Foundry extension on /responses, not a typed property
// on CreateResponseOptions, so it is injected via JsonPatch on the request body.
// Pinning the session id is what guarantees the agent container reads the
// files that this REPL just uploaded.
static async Task AskAsync(string[] parts, string sessionId, ProjectResponsesClient responses)
{
    if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
    {
        Console.WriteLine("Usage: ask <prompt>");
        return;
    }

    string prompt = parts[1];
    var options = new CreateResponseOptions();
    options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));
    options.Patch.Set("$.agent_session_id"u8, BinaryData.FromString($"\"{sessionId}\""));

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("Agent> ");
    Console.ResetColor();

    await foreach (StreamingResponseUpdate update in responses.CreateResponseStreamingAsync(options))
    {
        if (update is StreamingResponseOutputTextDeltaUpdate delta)
        {
            Console.Write(delta.Delta);
        }
    }

    Console.WriteLine();
}

static void PrintHelp()
{
    Console.WriteLine("""
        Commands:
          upload <local> [<remote>]    Upload a local file into the session sandbox.
          ls [<path>]                  List entries (default path: ".").
          download <remote> <local>    Download a session file locally.
          rm <remote>                  Delete a session file.
          ask <prompt>                 Ask the agent. Pinned to this REPL's session id, so
                                       the agent reads files this REPL uploaded.
          help                         Show this help.
          quit                         Delete the session and exit.
        """);
}

static async Task<ProjectsAgentVersion> GetLatestAgentVersionAsync(AgentAdministrationClient client, string agentName)
{
    ProjectsAgentVersion? latest = null;
    await foreach (ProjectsAgentVersion version in client.GetAgentVersionsAsync(agentName))
    {
        if (latest is null || string.CompareOrdinal(version.Version, latest.Version) > 0)
        {
            latest = version;
        }
    }

    return latest
        ?? throw new InvalidOperationException(
            $"No deployed versions found for agent '{agentName}'. Deploy the agent first or set HOSTED_AGENT_VERSION.");
}

// ── FeaturePolicy ─────────────────────────────────────────────────────────────

internal sealed class FeaturePolicy(string feature) : PipelinePolicy
{
    private const string FeatureHeader = "Foundry-Features";
    private readonly string _feature = feature;

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Add(FeatureHeader, this._feature);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Add(FeatureHeader, this._feature);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }
}
