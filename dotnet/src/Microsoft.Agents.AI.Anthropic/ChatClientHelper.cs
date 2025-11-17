// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Client.Models.Messages;
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
    public static UsageDetails CreateUsageDetails(Usage usage)
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

    public static ThinkingConfigParam? GetThinkingParameters(this ChatOptions options)
    {
        const string ThinkingParametersKey = "Anthropic.ThinkingParameters";

        if (options?.AdditionalProperties?.TryGetValue(ThinkingParametersKey, out var value) == true)
        {
            return value as ThinkingConfigParam;
        }

        return null;
    }

    /// <summary>
    /// Create message parameters from chat messages and options
    /// </summary>
    public static MessageCreateParams CreateMessageParameters(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options)
    {
        if (options.RawRepresentationFactory?.Invoke(client) is not MessageCreateParams parameters)
        {
            List<ToolUnion>? tools = null;
            ToolChoice? toolChoice = null;

            if (options.Tools is { Count: > 0 })
            {
                if (options.ToolMode is RequiredChatToolMode r)
                {
                    toolChoice = r.RequiredFunctionName is null ? new ToolChoice(new ToolChoiceAny()) : new ToolChoice(new ToolChoiceTool(r.RequiredFunctionName));
                }

                tools = [];
                foreach (var tool in options.Tools)
                {
                    switch (tool)
                    {
                        case AIFunctionDeclaration f:
                            tools.Add(new ToolUnion(new Tool()
                            {
                                Name = f.Name,
                                Description = f.Description,
                                InputSchema = AIFunctionDeclarationToInputSchema(f)
                            }));
                            break;

                            /*
                            case HostedCodeInterpreterTool:
                                tools.Add(new ToolUnion(CodeInterpreter));
                                break;
                            case HostedWebSearchTool:
                                tools.Add(new ToolUnion(ServerTools.GetWebSearchTool(5)));
                                break;
                            #pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
                        case HostedMcpServerTool mcpt:
                            MCPServer mcpServer = new()
                            {
                                Url = mcpt.ServerAddress,
                                Name = mcpt.ServerName,
                            };

                            if (mcpt.AllowedTools is not null)
                            {
                                mcpServer.ToolConfiguration.AllowedTools.AddRange(mcpt.AllowedTools);
                            }

                            mcpServer.AuthorizationToken = mcpt.AuthorizationToken;

                            (parameters.MCPServers ??= []).Add(mcpServer);
                            break;
#pragma warning restore MEAI001
                            */
                    }
                }
            }

            parameters = new MessageCreateParams()
            {
                Model = options.ModelId!,
                Messages = GetMessages(messages),
                System = GetSystem(options, messages),
                MaxTokens = (options.MaxOutputTokens is int maxOutputTokens) ? maxOutputTokens : 4096,
                Temperature = (options.Temperature is float temperature) ? (double)temperature : null,
                TopP = (options.TopP is float topP) ? (double)topP : null,
                TopK = (options.TopK is int topK) ? topK : null,
                StopSequences = (options.StopSequences is { Count: > 0 } stopSequences) ? stopSequences.ToList() : null,
                ToolChoice = toolChoice,
                Tools = tools,
                Thinking = options.GetThinkingParameters(),
            };
        }

        // Avoid errors from completely empty input.
        if (!parameters.Messages.Any(m => m.Content.Count > 0))
        {
            parameters.Messages.Add(new(RoleType.User, "\u200b")); // zero-width space
        }

        return parameters;
    }

    private static SystemModel? GetSystem(ChatOptions options, IEnumerable<ChatMessage> messages)
    {
        StringBuilder? fullInstructions = (options.Instructions is string instructions) ? new(instructions) : null;

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                (fullInstructions ??= new()).AppendLine(string.Concat(message.Contents.OfType<TextContent>()));
            }
        }

        return fullInstructions is not null ? new SystemModel(fullInstructions.ToString()) : null;
    }

    private static List<MessageParam> GetMessages(IEnumerable<ChatMessage> chatMessages)
    {
        List<MessageParam> messages = [];

        foreach (ChatMessage chatMessage in chatMessages)
        {
            if (chatMessage.Role != ChatRole.System)
            {
                // Process contents in order, creating new messages when switching between tool results and other content
                // This preserves ordering and handles interleaved tool calls, AI output, and tool results
                MessageParam? currentMessage = null;
                bool lastWasToolResult = false;

                foreach (AIContent content in chatMessage.Contents)
                {
                    bool isToolResult = content is FunctionResultContent;

                    // Create new message if:
                    // 1. This is the first content item, OR
                    // 2. We're switching between tool result and non-tool result content
                    if (currentMessage == null || lastWasToolResult != isToolResult)
                    {
                        currentMessage = new()
                        {
                            // Tool results must always be in User messages, others respect original role
                            Role = isToolResult ? RoleType.User : (chatMessage.Role == ChatRole.Assistant ? RoleType.Assistant : RoleType.User),
                            Content = new ContentModel(),
                        };
                        messages.Add(currentMessage);
                        lastWasToolResult = isToolResult;
                    }

                    // Add content to current message
                    switch (content)
                    {
                        case FunctionResultContent frc:
                            currentMessage.Content.Add(new ToolResultContent()
                            {
                                ToolUseId = frc.CallId,
                                Content = new List<ContentBase>() { new TextContent() { Text = frc.Result?.ToString() ?? string.Empty } },
                                IsError = frc.Exception is not null,
                            });
                            break;

                        case TextReasoningContent reasoningContent:
                            if (string.IsNullOrEmpty(reasoningContent.Text))
                            {
                                currentMessage.Content.Add(new Messaging.RedactedThinkingContent() { Data = reasoningContent.ProtectedData });
                            }
                            else
                            {
                                currentMessage.Content.Add(new Messaging.ThinkingContent()
                                {
                                    Thinking = reasoningContent.Text,
                                    Signature = reasoningContent.ProtectedData,
                                });
                            }
                            break;

                        case TextContent textContent:
                            string text = textContent.Text;
                            if (currentMessage.Role == RoleType.Assistant)
                            {
                                text.TrimEnd();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    currentMessage.Content.Add(new Anthropic.Client.Models.MessagesTextContent() { Text = text });
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(text))
                            {
                                currentMessage.Content.Add(new TextContent() { Text = text });
                            }

                            break;

                        case DataContent imageContent when imageContent.HasTopLevelMediaType("image"):
                            currentMessage.Content.Add(new ContentBlock()
                            {
                                Source = new()
                                {
                                    Data = Convert.ToBase64String(imageContent.Data.ToArray()),
                                    MediaType = imageContent.MediaType,
                                }
                            });
                            break;

                        case DataContent documentContent when documentContent.HasTopLevelMediaType("application"):
                            currentMessage.Content.Add(new DocumentContent()
                            {
                                Source = new()
                                {
                                    Data = Convert.ToBase64String(documentContent.Data.ToArray()),
                                    MediaType = documentContent.MediaType,
                                }
                            });
                            break;

                        case FunctionCallContent fcc:
                            currentMessage.Content.Add(new ToolUseContent()
                            {
                                Id = fcc.CallId,
                                Name = fcc.Name,
                                Input = JsonSerializer.SerializeToNode(fcc.Arguments)
                            });
                            break;
                    }
                }
            }
        }

        parameters.Messages.RemoveAll(m => m.Content.Count == 0);
    }

    /// <summary>
    /// Process response content
    /// </summary>
    public static List<AIContent> ProcessResponseContent(MessageResponse response)
    {
        List<AIContent> contents = new();

        foreach (ContentBase content in response.Content)
        {
            switch (content)
            {
                case Messaging.ThinkingContent thinkingContent:
                    contents.Add(new TextReasoningContent(thinkingContent.Thinking)
                    {
                        ProtectedData = thinkingContent.Signature,
                    });
                    break;

                case Messaging.RedactedThinkingContent redactedThinkingContent:
                    contents.Add(new TextReasoningContent(null)
                    {
                        ProtectedData = redactedThinkingContent.Data,
                    });
                    break;

                case TextContent tc:
                    var textContent = new TextContent(tc.Text);
                    if (tc.Citations != null && tc.Citations.Any())
                    {
                        foreach (var tau in tc.Citations)
                        {
                            (textContent.Annotations ?? []).Add(new CitationAnnotation
                            {
                                RawRepresentation = tau,
                                AnnotatedRegions =
                                [
                                    new TextSpanAnnotatedRegion
                                            { StartIndex = (int?)tau.StartPageNumber, EndIndex = (int?)tau.EndPageNumber }
                                ],
                                FileId = tau.Title
                            });
                        }
                    }
                    contents.Add(textContent);
                    break;

                case ImageContent ic:
                    contents.Add(new DataContent(ic.Source.Data, ic.Source.MediaType));
                    break;

                case ToolUseContent tuc:
                    contents.Add(new FunctionCallContent(
                        tuc.Id,
                        tuc.Name,
                        tuc.Input is not null ? tuc.Input.Deserialize<Dictionary<string, object>>() : null));
                    break;

                case ToolResultContent trc:
                    contents.Add(new FunctionResultContent(
                        trc.ToolUseId,
                        trc.Content));
                    break;
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
