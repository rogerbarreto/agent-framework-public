// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable CA1812

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

    /// <summary>
    /// Create message parameters from chat messages and options
    /// </summary>
    public static MessageCreateParams CreateMessageParameters(IChatClient client, IEnumerable<ChatMessage> messages, ChatOptions options)
    {
        if (options.RawRepresentationFactory?.Invoke(client) is not MessageCreateParams parameters)
        {
            parameters = new MessageCreateParams()
            {
                Model = options.ModelId!,
                Messages = [],
                System = (options.Instructions is string instructions) ? new SystemModel(instructions) : null,
                MaxTokens = (options.MaxOutputTokens is int maxOutputTokens) ? maxOutputTokens : 4096,
                Temperature = (options.Temperature is float temperature) ? (double)temperature : null,
            };
        }

        if (options is not null)
        {

            if (options.TopP is float topP)
            {
                parameters.TopP = (decimal)topP;
            }

            if (options.TopK is int topK)
            {
                parameters.TopK = topK;
            }

            if (options.StopSequences is not null)
            {
                parameters.StopSequences = options.StopSequences.ToArray();
            }

            if (options.Tools is { Count: > 0 })
            {
                parameters.ToolChoice ??= new();

                if (options.ToolMode is RequiredChatToolMode r)
                {
                    parameters.ToolChoice.Type = r.RequiredFunctionName is null ? ToolChoiceType.Any : ToolChoiceType.Tool;
                    parameters.ToolChoice.Name = r.RequiredFunctionName;
                }

                IList<Common.Tool> tools = parameters.Tools ??= [];
                foreach (var tool in options.Tools)
                {
                    switch (tool)
                    {
                        case AIFunctionDeclaration f:
                            tools.Add(new Common.Tool(new Function(f.Name, f.Description, JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<FunctionParameters>(f.JsonSchema)))));
                            break;

                        case HostedCodeInterpreterTool:
                            tools.Add(Common.Tool.CodeInterpreter);
                            break;

                        case HostedWebSearchTool:
                            tools.Add(ServerTools.GetWebSearchTool(5));
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
                    }
                }
            }

            // Map thinking parameters from ChatOptions
            var thinkingParameters = options.GetThinkingParameters();
            if (thinkingParameters != null)
            {
                parameters.Thinking = thinkingParameters;
            }
        }

        foreach (ChatMessage message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                (parameters.System ??= []).Add(new SystemMessage(string.Concat(message.Contents.OfType<TextContent>())));
            }
            else
            {
                // Process contents in order, creating new messages when switching between tool results and other content
                // This preserves ordering and handles interleaved tool calls, AI output, and tool results
                Message currentMessage = null;
                bool lastWasToolResult = false;

                foreach (AIContent content in message.Contents)
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
                            Role = isToolResult ? RoleType.User : (message.Role == ChatRole.Assistant ? RoleType.Assistant : RoleType.User),
                            Content = [],
                        };
                        parameters.Messages.Add(currentMessage);
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

        // Avoid errors from completely empty input.
        if (!parameters.Messages.Any(m => m.Content.Count > 0))
        {
            parameters.Messages.Add(new(RoleType.User, "\u200b")); // zero-width space
        }

        return parameters;
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
