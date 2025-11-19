// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Anthropic;

/// <summary>
/// Helper class for chat client implementations
/// </summary>
internal static class ChatClientHelper
{
    /// <summary>
    /// Create usage details from usage
    /// </summary>
    public static UsageDetails CreateUsageDetails(BetaUsage usage)
    {
        UsageDetails usageDetails = new()
        {
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            AdditionalCounts = [],
        };

        if (usage.CacheCreationInputTokens.HasValue)
        {
            usageDetails.AdditionalCounts.Add(nameof(usage.CacheCreationInputTokens), usage.CacheCreationInputTokens.Value);
        }

        if (usage.CacheReadInputTokens.HasValue)
        {
            usageDetails.AdditionalCounts.Add(nameof(usage.CacheReadInputTokens), usage.CacheReadInputTokens.Value);
        }

        return usageDetails;
    }

    private static InputSchema AIFunctionDeclarationToInputSchema(AIFunctionDeclaration function)
        => new(function.JsonSchema.EnumerateObject().ToDictionary(k => k.Name, v => v.Value));

    public static BetaThinkingConfigParam? GetThinkingParameters(this ChatOptions options)
    {
        const string ThinkingParametersKey = "Anthropic.ThinkingParameters";

        if (options?.AdditionalProperties?.TryGetValue(ThinkingParametersKey, out var value) == true)
        {
            return value as BetaThinkingConfigParam;
        }

        return null;
    }

    public static List<BetaRequestMCPServerURLDefinition>? GetMcpServers(ChatOptions? options)
    {
        List<BetaRequestMCPServerURLDefinition>? mcpServerDefinitions = null;

        if (options?.Tools is { Count: > 0 })
        {
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            foreach (var mcpt in options.Tools.OfType<HostedMcpServerTool>())
            {
                (mcpServerDefinitions ??= []).Add(
                    new BetaRequestMCPServerURLDefinition()
                    {
                        Name = mcpt.ServerName,
                        URL = mcpt.ServerAddress,
                        AuthorizationToken = mcpt.AuthorizationToken,
                        ToolConfiguration = new() { AllowedTools = mcpt.AllowedTools?.ToList() }
                    });
            }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        }

        return mcpServerDefinitions;
    }

    /// <summary>
    /// Create message parameters from chat messages and options
    /// </summary>
    internal static MessageCreateParams CreateBetaMessageParameters(IChatClient client, string modelId, IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (options?.RawRepresentationFactory?.Invoke(client) is MessageCreateParams parameters && parameters is not null)
        {
            return parameters;
        }

        List<BetaToolUnion>? tools = null;
        BetaToolChoice? toolChoice = null;

        if (options?.Tools is { Count: > 0 })
        {
            if (options.ToolMode is RequiredChatToolMode r)
            {
                toolChoice = r.RequiredFunctionName is null ? new BetaToolChoice(new BetaToolChoiceAny()) : new BetaToolChoice(new BetaToolChoiceTool(r.RequiredFunctionName));
            }

            tools = [];
            foreach (var tool in options.Tools)
            {
                switch (tool)
                {
                    case AIFunctionDeclaration f:
                        tools.Add(new BetaToolUnion(new BetaTool()
                        {
                            Name = f.Name,
                            Description = f.Description,
                            InputSchema = AIFunctionDeclarationToInputSchema(f)
                        }));
                        break;

                    case HostedCodeInterpreterTool codeTool:
                        if (codeTool.AdditionalProperties?["version"] is string version && version.Contains("20250522"))
                        {
                            tools.Add(new BetaCodeExecutionTool20250522());
                        }
                        else
                        {
                            tools.Add(new BetaCodeExecutionTool20250825());
                        }
                        break;

                    case HostedWebSearchTool webSearchTool:
                        tools.Add(new BetaToolUnion(new BetaWebSearchTool20250305()
                        {
                            MaxUses = (long?)webSearchTool.AdditionalProperties?[nameof(BetaWebSearchTool20250305.MaxUses)],
                            AllowedDomains = (List<string>?)webSearchTool.AdditionalProperties?[nameof(BetaWebSearchTool20250305.AllowedDomains)],
                            BlockedDomains = (List<string>?)webSearchTool.AdditionalProperties?[nameof(BetaWebSearchTool20250305.BlockedDomains)],
                            CacheControl = (BetaCacheControlEphemeral?)webSearchTool.AdditionalProperties?[nameof(BetaWebSearchTool20250305.CacheControl)],
                            Name = JsonSerializer.Deserialize(JsonSerializer.Serialize(webSearchTool.Name, AnthropicClientJsonContext.Default.String), AnthropicClientJsonContext.Default.JsonElement),
                            UserLocation = (UserLocation?)webSearchTool.AdditionalProperties?[nameof(UserLocation)]
                        }));
                        break;
                }
            }
        }

        parameters = new MessageCreateParams()
        {
            Model = modelId,
            Messages = GetMessages(messages),
            System = GetSystem(options, messages),
            MaxTokens = (options?.MaxOutputTokens is int maxOutputTokens) ? maxOutputTokens : 4096,
            Temperature = (options?.Temperature is float temperature) ? (double)temperature : null,
            TopP = (options?.TopP is float topP) ? (double)topP : null,
            TopK = (options?.TopK is int topK) ? topK : null,
            StopSequences = (options?.StopSequences is { Count: > 0 } stopSequences) ? stopSequences.ToList() : null,
            ToolChoice = toolChoice,
            Tools = tools,
            Thinking = options?.GetThinkingParameters(),
            MCPServers = GetMcpServers(options),
        };

        return parameters;
    }

    private static SystemModel? GetSystem(ChatOptions? options, IEnumerable<ChatMessage> messages)
    {
        StringBuilder? fullInstructions = (options?.Instructions is string instructions) ? new(instructions) : null;

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                (fullInstructions ??= new()).AppendLine(string.Concat(message.Contents.OfType<TextContent>()));
            }
        }

        return fullInstructions is not null ? new SystemModel(fullInstructions.ToString()) : null;
    }

    private static List<BetaMessageParam> GetMessages(IEnumerable<ChatMessage> chatMessages)
    {
        List<BetaMessageParam> betaMessages = [];

        foreach (ChatMessage message in chatMessages)
        {
            if (message.Role == ChatRole.System)
            {
                continue;
            }

            // Process contents in order, creating new messages when switching between tool results and other content
            // This preserves ordering and handles interleaved tool calls, AI output, and tool results
            BetaMessageParam? currentMessage = null;
            bool lastWasToolResult = false;

            for (var currentIndex = 0; currentIndex < message.Contents.Count; currentIndex++)
            {
                bool isToolResult = message.Contents[currentIndex] is FunctionResultContent;

                // Create new message if:
                // 1. This is the first content item, OR
                // 2. We're switching between tool result and non-tool result content
                if (currentMessage == null || lastWasToolResult != isToolResult)
                {
                    var messageRole = isToolResult ? Role.User : (message.Role == ChatRole.Assistant ? Role.Assistant : Role.User);
                    currentMessage = new()
                    {
                        // Tool results must always be in User messages, others respect original role
                        Role = messageRole,
                        Content = new BetaMessageParamContent(GetContents(message.Contents, currentIndex, messageRole))
                    };
                    betaMessages.Add(currentMessage);
                    lastWasToolResult = isToolResult;
                }
            }
        }

        betaMessages.RemoveAll(m => m.Content.TryPickBetaContentBlockParams(out var blocks) && blocks.Count == 0);

        // Avoid errors from completely empty input.
        if (betaMessages.Count == 0)
        {
            betaMessages.Add(new BetaMessageParam() { Role = Role.User, Content = "\u200b" }); // zero-width space
        }

        return betaMessages;
    }

    private static List<BetaContentBlockParam> GetContents(IList<AIContent> contents, int currentIndex, Role currentRole)
    {
        bool addedToolResult = false;
        List<BetaContentBlockParam> contentBlocks = [];
        for (var i = currentIndex; i < contents.Count; i++)
        {
            switch (contents[i])
            {
                case FunctionResultContent frc:
                    if (addedToolResult)
                    {
                        // Any subsequent function result needs to be processed as a new message
                        goto end;
                    }
                    addedToolResult = true;
                    contentBlocks.Add(new BetaToolResultBlockParam(frc.CallId)
                    {
                        Content = new BetaToolResultBlockParamContent(frc.Result?.ToString() ?? string.Empty),
                        IsError = frc.Exception is not null,
                        CacheControl = frc.AdditionalProperties?[nameof(BetaToolResultBlockParam.CacheControl)] as BetaCacheControlEphemeral,
                        ToolUseID = frc.CallId,
                    });
                    break;

                case FunctionCallContent fcc:
                    contentBlocks.Add(new BetaToolUseBlockParam()
                    {
                        ID = fcc.CallId,
                        Name = fcc.Name,
                        Input = fcc.Arguments?.ToDictionary(k => k.Key, v => JsonSerializer.SerializeToElement(v.Value, AnthropicClientJsonContext.Default.JsonElement)) ?? new Dictionary<string, JsonElement>()
                    });
                    break;

                case TextReasoningContent reasoningContent:
                    if (string.IsNullOrEmpty(reasoningContent.Text))
                    {
                        contentBlocks.Add(new BetaRedactedThinkingBlockParam(reasoningContent.ProtectedData!));
                    }
                    else
                    {
                        contentBlocks.Add(new BetaThinkingBlockParam()
                        {
                            Signature = reasoningContent.ProtectedData!,
                            Thinking = reasoningContent.Text,
                        });
                    }
                    break;

                case TextContent textContent:
                    string text = textContent.Text;
                    if (currentRole == Role.Assistant)
                    {
                        var trimmedText = text.TrimEnd();
                        if (!string.IsNullOrEmpty(trimmedText))
                        {
                            contentBlocks.Add(new BetaTextBlockParam() { Text = trimmedText });
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(text))
                    {
                        contentBlocks.Add(new BetaTextBlockParam() { Text = text });
                    }

                    break;

                case HostedFileContent hostedFileContent:
                    contentBlocks.Add(
                        new BetaContentBlockParam(
                            new BetaRequestDocumentBlock(
                                new BetaRequestDocumentBlockSource(
                                    new BetaFileDocumentSource(hostedFileContent.FileId)))));
                    break;

                case DataContent imageContent when imageContent.HasTopLevelMediaType("image"):
                    contentBlocks.Add(
                        new BetaImageBlockParam(
                            new BetaImageBlockParamSource(
                                new BetaBase64ImageSource()
                                {
                                    Data = Convert.ToBase64String(imageContent.Data.ToArray()),
                                    MediaType = imageContent.MediaType
                                })));
                    break;

                case DataContent pdfDocumentContent when pdfDocumentContent.MediaType == "application/pdf":
                    contentBlocks.Add(
                        new BetaContentBlockParam(
                            new BetaRequestDocumentBlock(
                                new BetaRequestDocumentBlockSource(
                                    new BetaBase64PDFSource()
                                    {
                                        Data = Convert.ToBase64String(pdfDocumentContent.Data.ToArray()),
                                    }))));
                    break;

                case DataContent textDocumentContent when textDocumentContent.HasTopLevelMediaType("text"):
                    contentBlocks.Add(
                        new BetaContentBlockParam(
                            new BetaRequestDocumentBlock(
                                new BetaRequestDocumentBlockSource(
                                    new BetaPlainTextSource()
                                    {
                                        Data = Convert.ToBase64String(textDocumentContent.Data.ToArray()),
                                    }))));
                    break;

                case UriContent imageUriContent when imageUriContent.HasTopLevelMediaType("image"):
                    contentBlocks.Add(
                        new BetaImageBlockParam(
                            new BetaImageBlockParamSource(
                                new BetaURLImageSource(imageUriContent.Uri.ToString()))));
                    break;

                case UriContent pdfUriContent when pdfUriContent.MediaType == "application/pdf":
                    contentBlocks.Add(
                        new BetaContentBlockParam(
                            new BetaRequestDocumentBlock(
                                new BetaRequestDocumentBlockSource(
                                    new BetaURLPDFSource(pdfUriContent.Uri.ToString())))));
                    break;
            }
        }

end:
        return contentBlocks;
    }

    /// <summary>
    /// Process response content
    /// </summary>
    public static List<AIContent> ProcessResponseContent(BetaMessage response)
    {
        List<AIContent> contents = new();

        foreach (BetaContentBlock content in response.Content)
        {
            switch (content)
            {
                case BetaContentBlock ct when ct.TryPickThinking(out var thinkingBlock):
                    contents.Add(new TextReasoningContent(thinkingBlock.Thinking)
                    {
                        ProtectedData = thinkingBlock.Signature,
                    });
                    break;

                case BetaContentBlock ct when ct.TryPickRedactedThinking(out var redactedThinkingBlock):
                    contents.Add(new TextReasoningContent(null)
                    {
                        ProtectedData = redactedThinkingBlock.Data,
                    });
                    break;

                case BetaContentBlock ct when ct.TryPickText(out var textBlock):
                    var textContent = new TextContent(textBlock.Text);
                    if (textBlock.Citations is { Count: > 0 })
                    {
                        foreach (var tau in textBlock.Citations)
                        {
                            var annotation = new CitationAnnotation()
                            {
                                RawRepresentation = tau,
                                Snippet = tau.CitedText,
                                FileId = tau.Title,
                                AnnotatedRegions = []
                            };

                            switch (tau)
                            {
                                case BetaTextCitation bChar when bChar.TryPickCitationCharLocation(out var charLocation):
                                {
                                    annotation.AnnotatedRegions.Add(new TextSpanAnnotatedRegion { StartIndex = (int?)charLocation?.StartCharIndex, EndIndex = (int?)charLocation?.EndCharIndex });
                                    break;
                                }

                                case BetaTextCitation search when search.TryPickCitationSearchResultLocation(out var searchLocation) && Uri.IsWellFormedUriString(searchLocation.Source, UriKind.RelativeOrAbsolute):
                                {
                                    annotation.Url = new Uri(searchLocation.Source);
                                    break;
                                }

                                case BetaTextCitation search when search.TryPickCitationsWebSearchResultLocation(out var searchLocation):
                                {
                                    annotation.Url = new Uri(searchLocation.URL);
                                    break;
                                }

                                default:
                                {
                                    (textContent.Annotations ?? []).Add(new CitationAnnotation
                                    {
                                        Snippet = tau.CitedText,
                                        Title = tau.Title,
                                        RawRepresentation = tau
                                    });
                                    break;
                                }
                            }

                            (textContent.Annotations ??= []).Add(annotation);
                        }
                    }
                    contents.Add(textContent);
                    break;

                case BetaContentBlock ct when ct.TryPickToolUse(out var toolUse):
                    contents.Add(new FunctionCallContent(toolUse.ID, toolUse.Name)
                    {
                        Arguments = toolUse.Input?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                        RawRepresentation = toolUse
                    });
                    break;

                case BetaContentBlock ct when ct.TryPickMCPToolUse(out var mcpToolUse):
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    contents.Add(new McpServerToolCallContent(mcpToolUse.ID, mcpToolUse.Name, mcpToolUse.ServerName)
                    {
                        Arguments = mcpToolUse.Input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
                        RawRepresentation = mcpToolUse
                    });
                    break;

                case BetaContentBlock ct when ct.TryPickMCPToolResult(out var mcpToolResult):
                {
                    contents.Add(new McpServerToolResultContent(mcpToolResult.ToolUseID)
                    {
                        Output = [mcpToolResult.IsError
                            ? new ErrorContent(mcpToolResult.Content.Value.ToString())
                            : new TextContent(mcpToolResult.Content.Value.ToString())],
                        RawRepresentation = mcpToolResult
                    });
                    break;
                }

                case BetaContentBlock ct when ct.TryPickCodeExecutionToolResult(out var cer):
                {
                    var codeResult = new CodeInterpreterToolResultContent() { Outputs = [] };
                    if (cer.Content.TryPickError(out var cerErr))
                    {
                        codeResult.Outputs.Add(new ErrorContent(null) { ErrorCode = cerErr.ErrorCode.Value().ToString() });
                    }
                    if (cer.Content.TryPickResultBlock(out var cerResult))
                    {
                        codeResult.Outputs.Add(new TextContent(cerResult.Stdout) { RawRepresentation = cerResult });
                        if (!string.IsNullOrWhiteSpace(cerResult.Stderr))
                        {
                            codeResult.Outputs.Add(new TextContent(cerResult.Stderr) { RawRepresentation = cerResult });
                        }
                    }

                    contents.Add(codeResult);
                    break;
                }

                case BetaContentBlock ct when ct.TryPickBashCodeExecutionToolResult(out var bashCer):
                {
                    var codeResult = new CodeInterpreterToolResultContent() { Outputs = [] };
                    if (bashCer.Content.TryPickBetaBashCodeExecutionToolResultError(out var bashCerErr))
                    {
                        codeResult.Outputs.Add(new ErrorContent(null) { ErrorCode = bashCerErr.ErrorCode.Value().ToString() });
                    }
                    if (bashCer.Content.TryPickBetaBashCodeExecutionResultBlock(out var bashCerResult))
                    {
                        codeResult.Outputs.Add(new TextContent(bashCerResult.Stdout) { RawRepresentation = bashCerResult });
                        if (!string.IsNullOrWhiteSpace(bashCerResult.Stderr))
                        {
                            codeResult.Outputs.Add(new TextContent(bashCerResult.Stderr) { RawRepresentation = bashCerResult });
                        }
                    }

                    contents.Add(codeResult);
                    break;
                }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

                default:
                {
                    contents.Add(new AIContent { RawRepresentation = content });
                    break;
                }
            }
        }

        return contents;
    }

    /// <summary>
    /// Function parameters class
    /// </summary>
    private sealed class FunctionParameters
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("required")]
        public List<string> Required { get; set; } = [];

        [JsonPropertyName("properties")]
        public Dictionary<string, JsonElement> Properties { get; set; } = [];
    }
}
