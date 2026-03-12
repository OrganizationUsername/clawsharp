using System.Threading.Channels;

namespace Clawsharp.Core.Services;

public interface IMessageBus
{
    ValueTask PublishAsync(InboundMessage message, CancellationToken ct = default);

    IAsyncEnumerable<InboundMessage> ReadAllAsync(CancellationToken ct = default);
}

public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly Channel<InboundMessage> _channel =
        Channel.CreateUnbounded<InboundMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask PublishAsync(InboundMessage message, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(
            message with { ArrivedAt = message.ArrivedAt ?? DateTimeOffset.UtcNow },
            ct);
    }

    public IAsyncEnumerable<InboundMessage> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}