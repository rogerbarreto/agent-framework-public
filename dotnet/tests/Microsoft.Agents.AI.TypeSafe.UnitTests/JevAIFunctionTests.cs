// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Agents.AI.TypeSafe.UnitTests;

public sealed class JevAIFunctionTests
{
    [Fact]
    public async Task InvokeAsync_ModelArguments_ReachTheEvaluatorAsTypedRequestAsync()
    {
        // Arrange
        JevRequest? received = null;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((request, _, _) =>
        {
            received = request;
            return Task.FromResult(JevTestData.CreateResponse());
        }).Build();

        // Act
        await tool.InvokeAsync(JevTestData.Arguments(JevTestData.RequestJson));

        // Assert
        Assert.NotNull(received);
        Assert.Equal(JevTestData.TicketText, received.State.GetString());

        JevChoiceQuestion department = Assert.IsType<JevChoiceQuestion>(received.Questions["department"]);
        Assert.Equal("Which team should handle this", department.Instructions);
        Assert.Equal(["billing", "technical", "sales"], department.Criteria.Keys);
        Assert.Null(department.Criteria["sales"]);

        JevScoreQuestion frustration = Assert.IsType<JevScoreQuestion>(received.Questions["frustration"]);
        Assert.Equal(3, frustration.Criteria.Count);
        Assert.Equal("Calm, just stating facts", frustration.Criteria[0]);

        JevNoulQuestion urgent = Assert.IsType<JevNoulQuestion>(received.Questions["is_urgent"]);
        Assert.Equal("Explicitly time-sensitive", urgent.Criteria!.True);
        Assert.Equal("No urgency expressed", urgent.Criteria.False);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsTheResponseAsJsonWithWireNamesAsync()
    {
        // Arrange
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, _) => Task.FromResult(JevTestData.CreateResponse())).Build();

        // Act
        object? result = await tool.InvokeAsync(JevTestData.Arguments(JevTestData.RequestJson));

        // Assert
        JsonElement json = Assert.IsType<JsonElement>(result);
        Assert.Equal("custom-model", json.GetProperty("model").GetString());
        JsonElement answers = json.GetProperty("answers");
        Assert.Equal("choice", answers.GetProperty("department").GetProperty("type").GetString());
        Assert.Equal("technical", answers.GetProperty("department").GetProperty("choice").GetString());
        Assert.Equal(0.78, answers.GetProperty("department").GetProperty("confidence").GetDouble());
        Assert.Equal("score", answers.GetProperty("frustration").GetProperty("type").GetString());
        Assert.Equal("Frustrated", answers.GetProperty("frustration").GetProperty("legend").GetProperty("1").GetString());
        Assert.Equal(0.97, answers.GetProperty("is_urgent").GetProperty("noul").GetDouble());
        Assert.Equal(10, json.GetProperty("usage").GetProperty("input_tokens").GetInt64());
        Assert.Equal(5, json.GetProperty("usage").GetProperty("output_tokens").GetInt64());
    }

    [Fact]
    public async Task InvokeAsync_TypeWrittenLastAndObjectState_AreAcceptedAsync()
    {
        // Arrange
        JevRequest? received = null;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((request, _, _) =>
        {
            received = request;
            return Task.FromResult(new JevResponse { Answers = new Dictionary<string, JevAnswer> { ["paid"] = new JevNoulAnswer { Noul = 0.1 } } });
        }).Build();

        // Act
        await tool.InvokeAsync(JevTestData.Arguments("""
            {
              "questions": { "paid": { "instructions": "Was `order` paid?", "type": "noul" } },
              "state": { "order": { "id": "A-104", "status": "failed" } }
            }
            """));

        // Assert
        Assert.Equal(JsonValueKind.Object, received!.State.ValueKind);
        Assert.Equal("A-104", received.State.GetProperty("order").GetProperty("id").GetString());
        Assert.IsType<JevNoulQuestion>(received.Questions["paid"]);
    }

    [Fact]
    public async Task InvokeAsync_DotNetObjectArguments_AreAcceptedAsync()
    {
        // Arrange
        JevRequest? received = null;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((request, _, _) =>
        {
            received = request;
            return Task.FromResult(new JevResponse { Answers = new Dictionary<string, JevAnswer> { ["q"] = new JevNoulAnswer { Noul = 0.5 } } });
        }).Build();

        // Act
        await tool.InvokeAsync(new AIFunctionArguments
        {
            ["state"] = "The payment failed.",
            ["questions"] = new Dictionary<string, JevQuestion> { ["q"] = new JevNoulQuestion { Instructions = "Did the payment fail?" } },
        });

        // Assert
        Assert.Equal("The payment failed.", received!.State.GetString());
        Assert.Equal("Did the payment fail?", Assert.IsType<JevNoulQuestion>(received.Questions["q"]).Instructions);
    }

    [Theory]
    [InlineData("""{"questions":{"q":{"type":"noul","instructions":"x"}}}""", "state")]
    [InlineData("""{"state":42,"questions":{"q":{"type":"noul","instructions":"x"}}}""", "The state must be a string")]
    [InlineData("""{"state":"x","questions":{}}""", "at least one question")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"rank","instructions":"x"}}}""", "not a valid Jev request")]
    [InlineData("""{"state":"x","questions":{"q":{"instructions":"x"}}}""", "not a valid Jev request")]
    [InlineData("""{"state":"x","questions":{"q":null}}""", "Question 'q' is null")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"noul","instructions":" "}}}""", "Question 'q' must have instructions")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"choice","instructions":"x","criteria":{}}}}""", "has 0 options; it needs from 1 to 255")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"choice","instructions":"x","criteria":{"":"empty"}}}}""", "empty option name")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"score","instructions":"x","criteria":["only"]}}}""", "has 1 level; it needs from 2 to 10")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"score","instructions":"x","criteria":["0","1","2","3","4","5","6","7","8","9","10"]}}}""", "has 11 levels")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"score","instructions":"x","criteria":["low",""]}}}""", "empty level description")]
    public async Task InvokeAsync_InvalidArguments_FailBeforeEvaluatingAsync(string arguments, string expectedMessage)
    {
        // Arrange
        int calls = 0;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, _) =>
        {
            calls++;
            return Task.FromResult(JevTestData.CreateResponse());
        }).Build();

        // Act
        ArgumentException exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => tool.InvokeAsync(JevTestData.Arguments(arguments)).AsTask());

        // Assert
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvokeAsync_TooManyChoiceOptions_FailBeforeEvaluatingAsync()
    {
        // Arrange
        string options = string.Join(",", Enumerable.Range(0, 256).Select(static i => $"\"o{i}\":null"));
        string arguments = "{\"state\":\"x\",\"questions\":{\"q\":{\"type\":\"choice\",\"instructions\":\"x\",\"criteria\":{" + options + "}}}}";
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, _) => Task.FromResult(JevTestData.CreateResponse())).Build();

        // Act
        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() => tool.InvokeAsync(JevTestData.Arguments(arguments)).AsTask());

        // Assert
        Assert.Contains("has 256 options", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, Func<JevResponse, JevResponse>, string> InvalidResponses => new()
    {
        { "no answer", static r => Without(r, "is_urgent"), "no answer for question 'is_urgent'" },
        { "wrong kind", static r => With(r, "department", new JevNoulAnswer { Noul = 0.5 }), "is a Noul answer, but the question is a Choice question" },
        { "unknown option", static r => With(r, "department", Choice("refunds", 0.9)), "chose 'refunds', which is not one of its options" },
        { "confidence out of range", static r => With(r, "department", Choice("technical", 1.2)), "has confidence 1.2, which is outside the range 0 to 1" },
        { "score out of range", static r => With(r, "frustration", Score(3)), "has score 3, which is outside the range 0 to 2" },
        { "noul as percent", static r => With(r, "is_urgent", new JevNoulAnswer { Noul = 97 }), "has noul 97, which is outside the range 0 to 1" },
        { "no answers", static r => new JevResponse { Answers = null! }, "has no answers" },
        { "probability for an option not offered", static r => With(r, "department", ChoiceWith("technical", new() { ["TECHNICAL"] = 1 })), "has probabilities for 'TECHNICAL', which is not one of its options" },
        { "probability above one", static r => With(r, "department", ChoiceWith("technical", new() { ["technical"] = 42 })), "has probability 'technical' 42, which is outside the range 0 to 1" },
        { "probability not a number", static r => With(r, "department", ChoiceWith("technical", new() { ["technical"] = double.NaN })), "has probability 'technical' NaN" },
        { "score probability for a missing level", static r => With(r, "frustration", ScoreWith(new() { ["3"] = 1 }, [])), "has probabilities for '3', which is not a level number from 0 to 2" },
        { "legend for a missing level", static r => With(r, "frustration", ScoreWith([], new() { ["high"] = "Very angry" })), "has legend for 'high', which is not a level number from 0 to 2" },
    };

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task InvokeAsync_InvalidEvaluatorResponse_FailsAsync(string scenario, Func<JevResponse, JevResponse> corrupt, string expectedMessage)
    {
        // Arrange
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, _) => Task.FromResult(corrupt(JevTestData.CreateResponse()))).Build();

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.InvokeAsync(JevTestData.Arguments(JevTestData.RequestJson)).AsTask());

        // Assert
        Assert.True(exception.Message.Contains(expectedMessage, StringComparison.Ordinal), $"{scenario}: {exception.Message}");
    }

    [Fact]
    public async Task InvokeAsync_EvaluatorReturnsNull_FailsAsync()
    {
        // Arrange
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, _) => Task.FromResult<JevResponse>(null!)).Build();

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.InvokeAsync(JevTestData.Arguments(JevTestData.RequestJson)).AsTask());

        // Assert
        Assert.Contains("returned no response", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_PassesTheCancellationTokenToTheEvaluatorAsync()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        CancellationToken received = default;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, _, cancellationToken) =>
        {
            received = cancellationToken;
            return Task.FromResult(JevTestData.CreateResponse());
        }).Build();

        // Act
        await tool.InvokeAsync(JevTestData.Arguments(JevTestData.RequestJson), cancellation.Token);

        // Assert
        Assert.Equal(cancellation.Token, received);
    }

    [Fact]
    public async Task UseClient_MapsBetweenTheContractAndTheClientTypesAsync()
    {
        // Arrange
        var client = new LabelClient(label: "technical");
        AIFunction tool = new JevAIToolBuilder().UseClient(
            client.ClassifyAsync,
            inputMapper: static request =>
            {
                (string id, JevQuestion question) = request.Questions.Single();
                return new LabelRequest(id, request.State.GetString()!, ((JevChoiceQuestion)question).Criteria.Keys.ToList());
            },
            outputMapper: static response => new JevResponse
            {
                Answers = new Dictionary<string, JevAnswer>
                {
                    [response.QuestionId] = Choice(response.Label, response.Score),
                },
            }).Build();

        // Act
        object? result = await tool.InvokeAsync(JevTestData.Arguments("""
            {"state":"My integration keeps failing","questions":{"team":{"type":"choice","instructions":"Which team?","criteria":{"billing":null,"technical":null}}}}
            """));

        // Assert
        Assert.Equal(new LabelRequest("team", "My integration keeps failing", ["billing", "technical"]), client.Received, LabelRequestComparer.Instance);
        JsonElement answer = Assert.IsType<JsonElement>(result).GetProperty("answers").GetProperty("team");
        Assert.Equal("technical", answer.GetProperty("choice").GetString());
    }

    [Fact]
    public async Task UseClient_MapperReturnsAnOptionThatWasNotOffered_FailsAsync()
    {
        // Arrange
        var client = new LabelClient(label: "TECHNICAL");
        AIFunction tool = new JevAIToolBuilder().UseClient(
            client.ClassifyAsync,
            static request => new LabelRequest("team", request.State.GetString()!, ["billing", "technical"]),
            static response => new JevResponse { Answers = new Dictionary<string, JevAnswer> { ["team"] = Choice(response.Label, response.Score) } }).Build();

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.InvokeAsync(JevTestData.Arguments("""
            {"state":"x","questions":{"team":{"type":"choice","instructions":"Which team?","criteria":{"billing":null,"technical":null}}}}
            """)).AsTask());

        // Assert
        Assert.Contains("chose 'TECHNICAL'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEvaluator_ReceivesTheServicesOfTheCallAsync()
    {
        // Arrange
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        IServiceProvider? received = null;
        AIFunction tool = new JevAIToolBuilder().UseEvaluator((_, callServices, _) =>
        {
            received = callServices;
            return Task.FromResult(JevTestData.CreateResponse());
        }).Build();

        AIFunctionArguments arguments = JevTestData.Arguments(JevTestData.RequestJson);
        arguments.Services = services;

        // Act
        await tool.InvokeAsync(arguments);

        // Assert
        Assert.Same(services, received);
    }

    [Fact]
    public async Task UseClient_RegisteredClient_IsResolvedFromTheServicesOnEveryCallAsync()
    {
        // Arrange
        int created = 0;
        using ServiceProvider services = new ServiceCollection()
            .AddTransient(_ =>
            {
                created++;
                return new LabelClient(label: "technical");
            })
            .BuildServiceProvider();

        AIFunction tool = new JevAIToolBuilder().UseClient<LabelClient, LabelRequest, LabelResponse>(
            static (client, request, cancellationToken) => client.ClassifyAsync(request, cancellationToken),
            static request => new LabelRequest("team", request.State.GetString()!, ["billing", "technical"]),
            static response => new JevResponse { Answers = new Dictionary<string, JevAnswer> { ["team"] = Choice(response.Label, response.Score) } }).Build();

        AIFunctionArguments arguments = JevTestData.Arguments("""
            {"state":"x","questions":{"team":{"type":"choice","instructions":"Which team?","criteria":{"billing":null,"technical":null}}}}
            """);
        arguments.Services = services;

        // Act
        await tool.InvokeAsync(arguments);
        object? result = await tool.InvokeAsync(arguments);

        // Assert
        Assert.Equal(2, created);
        Assert.Equal("technical", Assert.IsType<JsonElement>(result).GetProperty("answers").GetProperty("team").GetProperty("choice").GetString());
    }

    [Theory]
    [InlineData(false, "this call has none")]
    [InlineData(true, "LabelClient is not registered")]
    public async Task UseClient_ClientNotAvailable_FailsWithWhatIsMissingAsync(bool withServices, string expectedMessage)
    {
        // Arrange
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        AIFunction tool = new JevAIToolBuilder().UseClient<LabelClient, LabelRequest, LabelResponse>(
            static (client, request, cancellationToken) => client.ClassifyAsync(request, cancellationToken),
            static request => new LabelRequest("q", "x", []),
            static response => JevTestData.CreateResponse()).Build();

        AIFunctionArguments arguments = JevTestData.Arguments(JevTestData.RequestJson);
        arguments.Services = withServices ? services : null;

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.InvokeAsync(arguments).AsTask());

        // Assert
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseClient_ScopedClientFromTheRootProvider_GetsAScopePerCallAsync()
    {
        // Arrange
        var created = new List<ScopedLabelClient>();
        using ServiceProvider root = new ServiceCollection()
            .AddScoped(_ =>
            {
                var client = new ScopedLabelClient();
                created.Add(client);
                return client;
            })
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        AIFunction tool = new JevAIToolBuilder().UseClient<ScopedLabelClient, string, string>(
            static (client, text, _) => Task.FromResult(client.IsDisposed ? "disposed" : "technical"),
            static request => request.State.GetString()!,
            static label => new JevResponse { Answers = new Dictionary<string, JevAnswer> { ["team"] = Choice(label, 0.9) } }).Build();

        AIFunctionArguments arguments = JevTestData.Arguments("""
            {"state":"x","questions":{"team":{"type":"choice","instructions":"Which team?","criteria":{"billing":null,"technical":null}}}}
            """);
        arguments.Services = root;

        // Act
        await tool.InvokeAsync(arguments);
        await tool.InvokeAsync(arguments);

        // Assert
        Assert.Equal(2, created.Count);
        Assert.All(created, static client => Assert.True(client.IsDisposed));
    }

    private static JevChoiceAnswer Choice(string choice, double confidence) => new()
    {
        Choice = choice,
        Confidence = confidence,
        Probabilities = new Dictionary<string, double> { [choice] = confidence },
    };

    private static JevScoreAnswer Score(double score) => new()
    {
        Score = score,
        Confidence = 1,
        Legend = new Dictionary<string, string>(),
        Probabilities = new Dictionary<string, double>(),
    };

    private static JevChoiceAnswer ChoiceWith(string choice, Dictionary<string, double> probabilities) => new()
    {
        Choice = choice,
        Confidence = 0.5,
        Probabilities = probabilities,
    };

    private static JevScoreAnswer ScoreWith(Dictionary<string, double> probabilities, Dictionary<string, string> legend) => new()
    {
        Score = 1,
        Confidence = 1,
        Legend = legend,
        Probabilities = probabilities,
    };

    private static JevResponse With(JevResponse response, string id, JevAnswer answer)
    {
        var answers = new Dictionary<string, JevAnswer>(response.Answers) { [id] = answer };
        return new JevResponse { Answers = answers };
    }

    private static JevResponse Without(JevResponse response, string id)
    {
        var answers = new Dictionary<string, JevAnswer>(response.Answers);
        answers.Remove(id);
        return new JevResponse { Answers = answers };
    }

    /// <summary>The request type of a hypothetical third-party classification client.</summary>
    private sealed record LabelRequest(string QuestionId, string Text, List<string> Labels);

    /// <summary>The response type of a hypothetical third-party classification client.</summary>
    private sealed record LabelResponse(string QuestionId, string Label, double Score);

    private sealed class LabelClient(string label)
    {
        public LabelRequest? Received { get; private set; }

        public Task<LabelResponse> ClassifyAsync(LabelRequest request, CancellationToken cancellationToken)
        {
            this.Received = request;
            return Task.FromResult(new LabelResponse(request.QuestionId, label, 0.9));
        }
    }

    /// <summary>A disposable client registered as scoped, as clients holding per-request resources usually are.</summary>
    private sealed class ScopedLabelClient : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => this.IsDisposed = true;
    }

    private sealed class LabelRequestComparer : IEqualityComparer<LabelRequest?>
    {
        public static LabelRequestComparer Instance { get; } = new();

        public bool Equals(LabelRequest? x, LabelRequest? y) =>
            x is not null && y is not null && x.QuestionId == y.QuestionId && x.Text == y.Text && x.Labels.SequenceEqual(y.Labels);

        public int GetHashCode(LabelRequest? obj) => obj?.QuestionId.GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
