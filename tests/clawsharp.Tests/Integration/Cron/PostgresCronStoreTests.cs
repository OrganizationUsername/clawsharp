using Clawsharp.Cron;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Clawsharp.Tests.Integration.Cron;

[TestFixture]
public sealed class PostgresCronStoreTests : CronStoreContractTests
{
    private static PostgreSqlContainer _container = null!;

    private static string _connectionString = null!;

    private static Respawner _respawner = null!;

    [OneTimeSetUp]
    public async Task ContainerSetUp()
    {
        _container = new PostgreSqlBuilder("postgres:18").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Initialize schema
        var store = new PostgresCronStore(_connectionString);
        await store.InitAsync();

        // Initialize Respawn
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"]
        });
    }

    [OneTimeTearDown]
    public async Task ContainerTearDown()
    {
        await _container.DisposeAsync();
    }

    protected override Task<ICronStore> CreateStoreAsync()
    {
        return Task.FromResult<ICronStore>(new PostgresCronStore(_connectionString));
    }

    protected override async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }
}