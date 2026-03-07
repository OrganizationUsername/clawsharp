using Microsoft.EntityFrameworkCore;

namespace Clawsharp.Tests.Integration.Helpers;

/// <summary>Minimal IDbContextFactory implementation for integration tests.</summary>
internal sealed class SimpleDbContextFactory<T>(DbContextOptions<T> options) : IDbContextFactory<T>
    where T : DbContext
{
    public T CreateDbContext() => (T)Activator.CreateInstance(typeof(T), options)!;
}