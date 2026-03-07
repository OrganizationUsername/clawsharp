using Remora.Rest.Core;

namespace Clawsharp.Channels.Discord;

/// <summary>Holds mutable bot state populated from the READY gateway event.</summary>
/// <remarks>
/// <see cref="SelfId"/> is written on the gateway thread (READY event) and read from
/// message-handler threads. The lock ensures cross-thread visibility of the 16-byte
/// <see cref="Snowflake"/>? struct.
/// </remarks>
public sealed class DiscordBotState
{
    private readonly Lock _lock = new();

    private Snowflake? _selfId;

    public Snowflake? SelfId
    {
        get
        {
            lock (_lock)
            {
                return _selfId;
            }
        }
        set
        {
            lock (_lock)
            {
                _selfId = value;
            }
        }
    }
}