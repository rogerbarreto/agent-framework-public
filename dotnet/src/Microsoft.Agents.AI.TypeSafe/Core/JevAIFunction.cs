// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// The <see cref="AIFunction"/> created by <see cref="JevAIToolBuilder"/>: its arguments are a <see cref="JevRequest"/>
/// and its result is the <see cref="JevResponse"/> of the configured evaluator.
/// </summary>
internal sealed class JevAIFunction : AIFunction
{
    private readonly Func<JevRequest, IServiceProvider?, CancellationToken, Task<JevResponse>> _evaluateAsync;

    public JevAIFunction(string name, string description, Func<JevRequest, IServiceProvider?, CancellationToken, Task<JevResponse>> evaluateAsync)
    {
        this.Name = name;
        this.Description = description;
        this._evaluateAsync = evaluateAsync;
    }

    public override string Name { get; }

    public override string Description { get; }

    public override JsonElement JsonSchema => JevJsonUtilities.RequestSchema;

    public override JsonElement? ReturnJsonSchema => JevJsonUtilities.ResponseSchema;

    public override JsonSerializerOptions JsonSerializerOptions => JevJsonUtilities.Options;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        JevRequest request = ReadRequest(arguments);
        JevContractValidator.ValidateRequest(request);

        // FunctionInvokingChatClient sets Services to the IServiceProvider of the agent (ChatClientAgent passes its
        // services through), so evaluators can resolve per-call dependencies such as a registered client.
        JevResponse? response = await this._evaluateAsync(request, arguments.Services, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            Throw.InvalidOperationException("The Jev evaluator returned no response.");
        }

        JevContractValidator.ValidateResponse(request, response);

        // The result is returned as JSON, as AIFunctionFactory does. Chat clients serialize any other result object
        // with AIJsonUtilities.DefaultOptions, which has no metadata for these types when reflection is disabled
        // (for example under native AOT); the model would then receive an empty tool output.
        try
        {
            return JsonSerializer.SerializeToElement(response, JevJsonContext.Default.JevResponse);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or NotSupportedException)
        {
            // A response that cannot be written is an evaluator fault. Reported as ArgumentException, it would look
            // like a mistake in the model's arguments and invite the model to retry the same call.
            throw new InvalidOperationException($"The Jev response could not be serialized: {ex.Message}", ex);
        }
    }

    private static JevRequest ReadRequest(AIFunctionArguments arguments)
    {
        // The arguments are the top-level properties of the request, so they are reassembled into one object.
        var json = new JsonObject();
        foreach (KeyValuePair<string, object?> argument in arguments)
        {
            json[argument.Key] = JevJsonUtilities.ToNode(argument.Value);
        }

        try
        {
            return json.Deserialize(JevJsonContext.Default.JevRequest)
                ?? throw new JsonException("The arguments are null.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // An unknown or missing question type surfaces as NotSupportedException rather than JsonException.
            throw new ArgumentException($"The arguments are not a valid Jev request: {ex.Message}", nameof(arguments), ex);
        }
    }
}
