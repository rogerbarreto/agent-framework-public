// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.Channels;

namespace Microsoft.Agents.AI.Hosting.Channels.UnitTests;

public class ResponseTargetTests
{
    [Fact]
    public void Singletons_AreCorrectVariants()
    {
        // Arrange / Act / Assert
        Assert.IsType<ResponseTarget.OriginatingResponseTarget>(ResponseTarget.Originating);
        Assert.IsType<ResponseTarget.ActiveResponseTarget>(ResponseTarget.Active);
        Assert.IsType<ResponseTarget.AllLinkedResponseTarget>(ResponseTarget.AllLinked);
        Assert.IsType<ResponseTarget.NoneResponseTarget>(ResponseTarget.None);
    }

    [Fact]
    public void Channel_FactoryProducesChannelTarget()
    {
        // Arrange / Act
        var target = ResponseTarget.Channel("telegram", echoInput: true);

        // Assert
        var typed = Assert.IsType<ResponseTarget.ChannelResponseTarget>(target);
        Assert.Equal("telegram", typed.ChannelName);
        Assert.True(typed.EchoInput);
    }

    [Fact]
    public void Identities_FactoryAcceptsSingleAndList()
    {
        // Arrange
        var alice = new ChannelIdentity("telegram", "1");
        var bob = new ChannelIdentity("invocations", "2");

        // Act
        var single = ResponseTarget.Identity(alice);
        var many = ResponseTarget.Identities([alice, bob], echoInput: true);

        // Assert
        var typedSingle = Assert.IsType<ResponseTarget.IdentitiesResponseTarget>(single);
        Assert.Single(typedSingle.Targets, alice);
        Assert.False(typedSingle.EchoInput);

        var typedMany = Assert.IsType<ResponseTarget.IdentitiesResponseTarget>(many);
        Assert.Equal(2, typedMany.Targets.Count);
        Assert.True(typedMany.EchoInput);
    }
}