// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Microsoft.Agents.AI.TypeSafe;

/// <summary>
/// Configures the <see cref="Extensions.AI.AIFunction"/> built by <see cref="JevAIToolBuilder"/> and the
/// client pipeline that sends its requests to the TypeSafe System One API.
/// </summary>
/// <remarks>
/// <para>
/// This type follows the <see cref="ClientPipelineOptions"/> pattern of the OpenAI and Azure AI client libraries:
/// <see cref="ClientPipelineOptions.Transport"/> selects the HTTP stack (for example an
/// <see cref="HttpClientPipelineTransport"/> over an <c>HttpClient</c> from <c>IHttpClientFactory</c>),
/// <see cref="ClientPipelineOptions.RetryPolicy"/> controls retries, and
/// <see cref="ClientPipelineOptions.AddPolicy(PipelinePolicy, PipelinePosition)"/> adds custom policies. The pipeline settings,
/// <see cref="Endpoint"/>, and <see cref="ModelId"/> apply only when the builder uses the TypeSafe API, that is, when
/// it receives an <see cref="ApiKeyCredential"/>; <see cref="Name"/> and <see cref="Description"/> apply to every
/// evaluator.
/// </para>
/// <para>
/// When <see cref="ClientPipelineOptions.RetryPolicy"/> is <see langword="null"/>, the pipeline retries up to three
/// times on the statuses that TypeSafe documents as transient (429 and 529, besides the standard 408 and 5xx ones), and
/// fails at once when the API asks for a wait longer than one minute, because the wait would block the agent run
/// without any feedback. That default follows <see cref="ClientPipelineOptions.ClientLoggingOptions"/>.
/// </para>
/// <para>
/// Like other client options, an instance becomes read-only once a tool has been built with it.
/// </para>
/// </remarks>
public sealed class JevAIToolOptions : ClientPipelineOptions
{
    private const string DefaultDescription =
        "Evaluates content with Jev, a classification model. Jev does not write text: it answers typed questions about the " +
        "given state and returns calibrated probabilities. Use it to classify, route, score, or check facts about text or " +
        "JSON data, and ask all related questions in one call. A 'choice' question picks exactly one option from its " +
        "criteria. A 'score' question rates the state against 2 to 10 ordered levels and can land between levels. A 'noul' " +
        "question returns the probability, from 0 to 1, that its yes or no statement is true. Choice and score answers " +
        "include a confidence from 0 to 1; treat a low confidence as uncertain.";

    /// <inheritdoc />
    public override void Freeze()
    {
        // The default policy is assigned here, not in the constructor, because ClientLoggingOptions may be set after
        // construction; ClientPipeline.Create freezes the options before it reads RetryPolicy, so the policy is in place.
        this.RetryPolicy ??= new JevRetryPolicy(
            JevRetryPolicy.DefaultMaxRetries,
            this.ClientLoggingOptions?.EnableLogging ?? true,
            this.ClientLoggingOptions?.LoggerFactory);

        base.Freeze();
    }

    /// <summary>
    /// Gets or sets the System One endpoint. When <see langword="null"/>, <c>https://api.typesafe.ai/v1/systemone</c> is used.
    /// </summary>
    public Uri? Endpoint
    {
        get;
        set
        {
            this.AssertNotFrozen();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the Jev model that answers the questions. Defaults to <c>jev-latest</c>.
    /// </summary>
    public string ModelId
    {
        get;
        set
        {
            this.AssertNotFrozen();
            field = value;
        }
    } = "jev-latest";

    /// <summary>
    /// Gets or sets the name of the function that the model sees. Defaults to <c>evaluate_with_jev</c>.
    /// </summary>
    public string Name
    {
        get;
        set
        {
            this.AssertNotFrozen();
            field = value;
        }
    } = "evaluate_with_jev";

    /// <summary>
    /// Gets or sets the description of the function that the model sees.
    /// </summary>
    /// <remarks>
    /// The default description explains the Choice, Score, and Noul question kinds. A replacement should explain them
    /// too, together with when the agent should call the function.
    /// </remarks>
    public string Description
    {
        get;
        set
        {
            this.AssertNotFrozen();
            field = value;
        }
    } = DefaultDescription;
}
