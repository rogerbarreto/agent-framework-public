// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Builds an <see cref="AIFunction"/> that lets an agent classify, route, score, and check content with Jev, the
/// TypeSafe System One model.
/// </summary>
/// <remarks>
/// <para>
/// Jev does not write text. It answers typed questions about a state and returns calibrated probabilities, which
/// makes it a fast and inexpensive classifier for an agent whose own model writes the replies. The function created by
/// <see cref="Build"/> is a thin wrapper around an evaluator: its arguments are a <see cref="JevRequest"/> written by
/// the agent's model, and its result is the evaluator's <see cref="JevResponse"/>, as JSON.
/// </para>
/// <para>
/// With an <see cref="ApiKeyCredential"/>, the evaluator is the TypeSafe System One API, called through a
/// System.ClientModel pipeline configured by <see cref="JevAIToolOptions"/>. Any other client can be used instead with
/// <see cref="UseEvaluator"/> or one of the <c>UseClient</c> methods.
/// </para>
/// <para>
/// Every evaluator receives the <see cref="IServiceProvider"/> of the agent that calls the tool
/// (<see cref="AIFunctionArguments.Services"/>, which <c>FunctionInvokingChatClient</c> sets from the services
/// the agent was created with), so it can resolve its dependencies per call. In a host, register the tool with
/// <c>services.AddAIAgent(...).WithAITool(sp =&gt; new JevAIToolBuilder(credential, options).Build())</c>.
/// </para>
/// </remarks>
public sealed class JevAIToolBuilder
{
    private readonly ApiKeyCredential? _credential;
    private readonly JevAIToolOptions _options;
    private Func<JevRequest, IServiceProvider?, CancellationToken, Task<JevResponse>>? _evaluateAsync;

    /// <summary>
    /// Initializes a new instance of the <see cref="JevAIToolBuilder"/> class that evaluates requests with the
    /// TypeSafe System One API.
    /// </summary>
    /// <param name="credential">The TypeSafe API key, sent as a bearer token.</param>
    /// <param name="options">The tool and client pipeline options. Defaults are used when <see langword="null"/>.</param>
    public JevAIToolBuilder(ApiKeyCredential credential, JevAIToolOptions? options = null)
    {
        this._credential = Throw.IfNull(credential);
        this._options = options ?? new JevAIToolOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JevAIToolBuilder"/> class for a custom evaluator, which must be set
    /// with <see cref="UseEvaluator"/> or a <c>UseClient</c> method before <see cref="Build"/>.
    /// </summary>
    /// <param name="options">
    /// The tool options. Only <see cref="JevAIToolOptions.Name"/> and <see cref="JevAIToolOptions.Description"/> apply
    /// to a custom evaluator, which uses its own client configuration. Defaults are used when <see langword="null"/>.
    /// </param>
    public JevAIToolBuilder(JevAIToolOptions? options = null)
    {
        this._options = options ?? new JevAIToolOptions();
    }

    /// <summary>
    /// Sets the evaluator that answers each request, replacing the TypeSafe API.
    /// </summary>
    /// <param name="evaluateAsync">
    /// The evaluator. It receives the <see cref="JevRequest"/> written by the model and the agent's services, which are
    /// <see langword="null"/> when the caller provides none, and returns one answer per question.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The agent's services are often the application's root provider, because hosted agents are singletons by
    /// default. To use a scoped service, create a scope inside the evaluator, or use
    /// <see cref="UseClient{TClient, TClientRequest, TClientResponse}"/>, which does so for every call.
    /// </remarks>
    public JevAIToolBuilder UseEvaluator(Func<JevRequest, IServiceProvider?, CancellationToken, Task<JevResponse>> evaluateAsync)
    {
        this._evaluateAsync = Throw.IfNull(evaluateAsync);
        return this;
    }

    /// <summary>
    /// Evaluates requests with another client, converting between the Jev contract types and the client's own types.
    /// </summary>
    /// <typeparam name="TClientRequest">The request type of the client.</typeparam>
    /// <typeparam name="TClientResponse">The response type of the client.</typeparam>
    /// <param name="evaluateAsync">The client operation that evaluates a request.</param>
    /// <param name="inputMapper">Converts the <see cref="JevRequest"/> written by the model into the client's request.</param>
    /// <param name="outputMapper">Converts the client's response into a <see cref="JevResponse"/>.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The mapped response is checked against the request before it reaches the model: every question needs an answer
    /// of the same kind, a Choice answer and its probabilities may only use the question's options, and probabilities
    /// and scores must be in range. A mapper mistake therefore fails the call instead of showing the model values it
    /// never offered.
    /// </remarks>
    public JevAIToolBuilder UseClient<TClientRequest, TClientResponse>(
        Func<TClientRequest, CancellationToken, Task<TClientResponse>> evaluateAsync,
        Func<JevRequest, TClientRequest> inputMapper,
        Func<TClientResponse, JevResponse> outputMapper)
    {
        _ = Throw.IfNull(evaluateAsync);
        _ = Throw.IfNull(inputMapper);
        _ = Throw.IfNull(outputMapper);

        return this.UseEvaluator(async (request, _, cancellationToken) =>
        {
            TClientRequest clientRequest = inputMapper(request);
            TClientResponse clientResponse = await evaluateAsync(clientRequest, cancellationToken).ConfigureAwait(false);
            return outputMapper(clientResponse);
        });
    }

    /// <summary>
    /// Evaluates requests with a client registered in the agent's services, converting between the Jev contract types
    /// and the client's own types.
    /// </summary>
    /// <typeparam name="TClient">The client type, resolved from the agent's services on every call.</typeparam>
    /// <typeparam name="TClientRequest">The request type of the client.</typeparam>
    /// <typeparam name="TClientResponse">The response type of the client.</typeparam>
    /// <param name="evaluateAsync">The client operation that evaluates a request.</param>
    /// <param name="inputMapper">Converts the <see cref="JevRequest"/> written by the model into the client's request.</param>
    /// <param name="outputMapper">Converts the client's response into a <see cref="JevResponse"/>.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The client is resolved from a new service scope on every call, and the scope is disposed when the call ends.
    /// Scoped clients therefore live for one call, and disposable transient clients are released after it, even when
    /// the agent's services are the application's root provider. The call fails when the agent has no services or
    /// <typeparamref name="TClient"/> is not registered. The mapped response is checked as described for
    /// <see cref="UseClient{TClientRequest, TClientResponse}(Func{TClientRequest, CancellationToken, Task{TClientResponse}}, Func{JevRequest, TClientRequest}, Func{TClientResponse, JevResponse})"/>.
    /// </remarks>
    public JevAIToolBuilder UseClient<TClient, TClientRequest, TClientResponse>(
        Func<TClient, TClientRequest, CancellationToken, Task<TClientResponse>> evaluateAsync,
        Func<JevRequest, TClientRequest> inputMapper,
        Func<TClientResponse, JevResponse> outputMapper)
        where TClient : notnull
    {
        _ = Throw.IfNull(evaluateAsync);
        _ = Throw.IfNull(inputMapper);
        _ = Throw.IfNull(outputMapper);

        return this.UseEvaluator(async (request, services, cancellationToken) =>
        {
            if (services is null)
            {
                Throw.InvalidOperationException(
                    $"The Jev tool resolves {typeof(TClient).Name} from the agent's services, but this call has none. " +
                    "Create the agent with an IServiceProvider, for example with AddAIAgent, or pass the client operation to UseClient directly.");
            }

            // Hosted agents are singletons that receive the root provider by default. Resolving from it directly would
            // reject scoped clients (or keep one forever when scope validation is off) and hold every disposable
            // transient client until shutdown, so each call gets its own scope.
            if (services.GetService<IServiceScopeFactory>() is not { } scopeFactory)
            {
                return await EvaluateWithClientAsync(services).ConfigureAwait(false);
            }

            AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                return await EvaluateWithClientAsync(scope.ServiceProvider).ConfigureAwait(false);
            }

            async Task<JevResponse> EvaluateWithClientAsync(IServiceProvider provider)
            {
                if (provider.GetService(typeof(TClient)) is not TClient client)
                {
                    Throw.InvalidOperationException($"{typeof(TClient).Name} is not registered in the agent's services.");
                    return default!;
                }

                TClientRequest clientRequest = inputMapper(request);
                TClientResponse clientResponse = await evaluateAsync(client, clientRequest, cancellationToken).ConfigureAwait(false);
                return outputMapper(clientResponse);
            }
        });
    }

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <returns>
    /// An <see cref="AIFunction"/> whose JSON schema is the schema of <see cref="JevRequest"/> and whose result is the
    /// evaluator's <see cref="JevResponse"/> serialized as JSON.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The builder has neither an <see cref="ApiKeyCredential"/> nor a custom evaluator, or an option that it uses is
    /// empty or invalid.
    /// </exception>
    /// <remarks>
    /// With the TypeSafe API, building creates the client pipeline, which makes the options read-only. Invalid arguments
    /// from the model fail with an <see cref="ArgumentException"/> that says what to fix; a function-invoking chat client
    /// includes that message in the tool result only when its detailed errors are enabled.
    /// </remarks>
    public AIFunction Build()
    {
        if (string.IsNullOrWhiteSpace(this._options.Name) || string.IsNullOrWhiteSpace(this._options.Description))
        {
            Throw.InvalidOperationException($"{nameof(JevAIToolOptions)}.{nameof(JevAIToolOptions.Name)} and {nameof(JevAIToolOptions.Description)} must not be empty.");
        }

        Func<JevRequest, IServiceProvider?, CancellationToken, Task<JevResponse>>? evaluateAsync = this._evaluateAsync;
        if (evaluateAsync is null)
        {
            if (this._credential is null)
            {
                Throw.InvalidOperationException($"Pass an {nameof(ApiKeyCredential)} to the constructor to use the TypeSafe API, or call {nameof(UseEvaluator)} or UseClient.");
            }

            // The model and endpoint only matter to the TypeSafe API; a custom evaluator uses its own configuration.
            if (string.IsNullOrWhiteSpace(this._options.ModelId))
            {
                Throw.InvalidOperationException($"{nameof(JevAIToolOptions)}.{nameof(JevAIToolOptions.ModelId)} must not be empty.");
            }

            if (this._options.Endpoint?.IsAbsoluteUri is false)
            {
                Throw.InvalidOperationException($"{nameof(JevAIToolOptions)}.{nameof(JevAIToolOptions.Endpoint)} must be an absolute URI.");
            }

            evaluateAsync = new JevHttpEvaluator(this._credential, this._options).EvaluateAsync;
        }

        return new JevAIFunction(this._options.Name, this._options.Description, evaluateAsync);
    }
}
