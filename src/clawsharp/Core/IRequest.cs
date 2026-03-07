using System.Text.Json.Serialization.Metadata;

namespace Clawsharp.Core;

/// <summary>
/// Base interface for self-describing HTTP API requests. Carries the endpoint URL,
/// request serialization metadata, and response deserialization metadata.
/// <para>
/// This non-generic-request form exists so that <c>ExecuteAsync&lt;TResponse&gt;</c> helper methods
/// can infer <typeparamref name="TResponse"/> from a single type parameter while still accessing
/// <see cref="Url"/> and <see cref="RequestTypeInfo"/> for serialization.
/// </para>
/// </summary>
/// <typeparam name="TResponse">The response type this request produces.</typeparam>
public interface IRequest<TResponse>
{
    /// <summary>Full URL (or base-relative path) to send this request to.</summary>
    string Url { get; }

    /// <summary>
    /// JSON type info used to serialize this request body (non-generic base).
    /// <para>
    /// Concrete implementations should return a strongly-typed <see cref="JsonTypeInfo{T}"/>
    /// from their source-generated context (e.g. <c>MyJsonContext.Default.MyRequest</c>),
    /// which implicitly converts to the base <see cref="JsonTypeInfo"/>. The strongly-typed
    /// return is enforced by the derived <see cref="IRequest{TSelf,TResponse}"/> interface.
    /// </para>
    /// </summary>
    JsonTypeInfo RequestTypeInfo { get; }

    /// <summary>JSON type info used to deserialize the HTTP response into <typeparamref name="TResponse"/>.</summary>
    JsonTypeInfo<TResponse> ResponseTypeInfo { get; }
}

/// <summary>
/// Strongly-typed extension of <see cref="IRequest{TResponse}"/> that uses the CRTP
/// (Curiously Recurring Template Pattern) to enforce compile-time type safety on
/// <see cref="RequestTypeInfo"/>. Implementors cannot accidentally return a
/// <see cref="JsonTypeInfo"/> for the wrong type.
/// <para>
/// Consumer methods (e.g. <c>ExecuteAsync</c>) accept <see cref="IRequest{TResponse}"/>
/// for type inference. The compile-time safety is enforced at the implementor site:
/// the <c>new</c> keyword narrows <see cref="IRequest{TResponse}.RequestTypeInfo"/>
/// from <see cref="JsonTypeInfo"/> to <see cref="JsonTypeInfo{TSelf}"/>, so returning
/// the wrong context member is a compile error.
/// </para>
/// </summary>
/// <typeparam name="TSelf">
/// The concrete request type. Constrains <see cref="RequestTypeInfo"/> to
/// <see cref="JsonTypeInfo{TSelf}"/>, preventing mismatched serialization metadata.
/// </typeparam>
/// <typeparam name="TResponse">The response type this request produces.</typeparam>
public interface IRequest<TSelf, TResponse> : IRequest<TResponse>
    where TSelf : IRequest<TSelf, TResponse>
{
    /// <summary>
    /// Strongly-typed JSON type info for serializing this request body.
    /// Hides the base <see cref="IRequest{TResponse}.RequestTypeInfo"/> with a
    /// <see cref="JsonTypeInfo{TSelf}"/> return, enforcing correct type metadata at compile time.
    /// </summary>
    new JsonTypeInfo<TSelf> RequestTypeInfo { get; }

    /// <inheritdoc />
    JsonTypeInfo IRequest<TResponse>.RequestTypeInfo => RequestTypeInfo;
}