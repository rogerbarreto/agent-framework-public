// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ClientModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.TypeSafe.UnitTests;

public sealed class JevAIToolBuilderTests
{
    [Fact]
    public void Build_Defaults_DescribeTheJevContract()
    {
        // Act
        AIFunction tool = new JevAIToolBuilder(new ApiKeyCredential("test-key")).Build();

        // Assert
        Assert.Equal("evaluate_with_jev", tool.Name);
        Assert.Contains("'choice'", tool.Description, StringComparison.Ordinal);
        Assert.Contains("'score'", tool.Description, StringComparison.Ordinal);
        Assert.Contains("'noul'", tool.Description, StringComparison.Ordinal);

        JsonElement schema = tool.JsonSchema;
        Assert.Equal(["state", "questions"], schema.GetProperty("required").EnumerateArray().Select(static r => r.GetString()));
        JsonElement question = schema.GetProperty("properties").GetProperty("questions").GetProperty("additionalProperties");
        Assert.Equal(
            ["choice", "score", "noul"],
            question.GetProperty("anyOf").EnumerateArray().Select(static b => b.GetProperty("properties").GetProperty("type").GetProperty("const").GetString()));

        JsonElement returnSchema = tool.ReturnJsonSchema!.Value;
        Assert.Equal(["answers"], returnSchema.GetProperty("required").EnumerateArray().Select(static r => r.GetString()));
        Assert.True(returnSchema.GetProperty("properties").GetProperty("usage").GetProperty("properties").TryGetProperty("input_tokens", out _));
    }

    [Fact]
    public void Options_Defaults_MatchTheTypeSafeApi()
    {
        // Act
        var options = new JevAIToolOptions();

        // Assert
        Assert.Null(options.Endpoint);
        Assert.Equal("jev-latest", options.ModelId);
        Assert.Equal("evaluate_with_jev", options.Name);
        Assert.Null(options.RetryPolicy);
    }

    [Fact]
    public void Build_WithTheTypeSafeApi_AssignsTheDefaultRetryPolicyOnlyWhenNoneIsSet()
    {
        // Arrange
        var defaults = new JevAIToolOptions();
        var custom = new JevAIToolOptions { RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 1) };

        // Act
        _ = new JevAIToolBuilder(new ApiKeyCredential("test-key"), defaults).Build();
        _ = new JevAIToolBuilder(new ApiKeyCredential("test-key"), custom).Build();

        // Assert
        Assert.IsType<JevRetryPolicy>(defaults.RetryPolicy);
        Assert.IsType<System.ClientModel.Primitives.ClientRetryPolicy>(custom.RetryPolicy);
    }

    [Fact]
    public void Build_WithACustomEvaluator_IgnoresTheApiOnlyOptions()
    {
        // Arrange
        var options = new JevAIToolOptions { ModelId = " ", Endpoint = new Uri("/relative", UriKind.Relative) };

        // Act
        AIFunction tool = new JevAIToolBuilder(options).UseEvaluator(static (_, _, _) => Task.FromResult(JevTestData.CreateResponse())).Build();

        // Assert
        Assert.Equal("evaluate_with_jev", tool.Name);
    }

    [Fact]
    public void Build_WithNameAndDescription_UsesTheOptions()
    {
        // Arrange
        var options = new JevAIToolOptions { Name = "triage_ticket", Description = "Classifies support tickets." };

        // Act
        AIFunction tool = new JevAIToolBuilder(options).UseEvaluator(static (_, _, _) => Task.FromResult(JevTestData.CreateResponse())).Build();

        // Assert
        Assert.Equal("triage_ticket", tool.Name);
        Assert.Equal("Classifies support tickets.", tool.Description);
    }

    [Fact]
    public void Build_WithTheTypeSafeApi_MakesTheOptionsReadOnly()
    {
        // Arrange
        var options = new JevAIToolOptions();

        // Act
        _ = new JevAIToolBuilder(new ApiKeyCredential("test-key"), options).Build();

        // Assert
        Assert.Throws<InvalidOperationException>(() => options.Endpoint = new Uri("https://proxy.contoso.com/jev"));
        Assert.Throws<InvalidOperationException>(() => options.ModelId = "jev-1.13");
    }

    [Fact]
    public void Build_WithACustomEvaluator_LeavesTheOptionsWritable()
    {
        // Arrange
        var options = new JevAIToolOptions();

        // Act
        _ = new JevAIToolBuilder(new ApiKeyCredential("test-key"), options).UseEvaluator(static (_, _, _) => Task.FromResult(JevTestData.CreateResponse())).Build();

        // Assert
        options.ModelId = "jev-1.13";
        Assert.Equal("jev-1.13", options.ModelId);
    }

    [Fact]
    public void Build_WithoutCredentialOrEvaluator_Throws()
    {
        // Act
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new JevAIToolBuilder().Build());

        // Assert
        Assert.Contains("ApiKeyCredential", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("model")]
    [InlineData("endpoint")]
    public void Build_InvalidOptions_Throw(string invalid)
    {
        // Arrange
        var options = new JevAIToolOptions();
        switch (invalid)
        {
            case "name": options.Name = " "; break;
            case "description": options.Description = ""; break;
            case "model": options.ModelId = " "; break;
            case "endpoint": options.Endpoint = new Uri("/v1/systemone", UriKind.Relative); break;
        }

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new JevAIToolBuilder(new ApiKeyCredential("test-key"), options).Build());
    }

    [Fact]
    public void Methods_NullArguments_Throw()
    {
        // Arrange
        var builder = new JevAIToolBuilder();
        Task<string> evaluateAsync(string request, CancellationToken _) => Task.FromResult(request);
        string input(JevRequest _) => "request";
        JevResponse output(string _) => JevTestData.CreateResponse();
        Task<string> evaluateWithClientAsync(object _1, string request, CancellationToken _2) => Task.FromResult(request);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JevAIToolBuilder((ApiKeyCredential)null!));
        Assert.Throws<ArgumentNullException>(() => builder.UseEvaluator(null!));
        Assert.Throws<ArgumentNullException>(() => builder.UseClient(null!, input, (Func<string, JevResponse>)output));
        Assert.Throws<ArgumentNullException>(() => builder.UseClient((Func<string, CancellationToken, Task<string>>)evaluateAsync, null!, output));
        Assert.Throws<ArgumentNullException>(() => builder.UseClient(evaluateAsync, input, null!));
        Assert.Throws<ArgumentNullException>(() => builder.UseClient((Func<object, string, CancellationToken, Task<string>>)evaluateWithClientAsync, null!, output));
    }
}
