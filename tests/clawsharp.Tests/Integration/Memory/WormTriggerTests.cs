using Clawsharp.Memory.MsSql;
using Clawsharp.Memory.Postgres;
using Clawsharp.Memory.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Respawn;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Clawsharp.Tests.Integration.Memory;

/// <summary>
/// Integration tests verifying Layer 3 (database triggers) of the WORM enforcement on HistoryEntry.
/// The three enforcement layers are:
///   1. init-only C# properties (compile-time)
///   2. EF Core ChangeTracker validation in MemoryDbContextBase (runtime, EF-level)
///   3. Database triggers (runtime, DB-level — bypasses EF, tested here)
///
/// These tests insert a HistoryEntry via the normal IMemory API, then attempt raw SQL
/// UPDATE and DELETE against the History table, verifying the triggers reject the operation.
/// </summary>
public static class WormTriggerTests
{
    [TestFixture]
    [Category("Integration")]
    public sealed class SqliteWormTriggerTests
    {
        private string _dbPath = null!;

        private SqliteMemory _memory = null!;

        private SimpleDbContextFactory<SqliteMemoryContext> _factory = null!;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
            var options = new DbContextOptionsBuilder<SqliteMemoryContext>()
                          .UseSqlite($"Data Source={_dbPath}")
                          .Options;
            _factory = new SimpleDbContextFactory<SqliteMemoryContext>(options);
            _memory = new SqliteMemory(_factory, NullLogger<SqliteMemory>.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public async Task RawSqlUpdate_OnHistory_IsRejectedByTrigger()
        {
            // Arrange: insert a history entry via the normal API (triggers schema init + MigrateAsync)
            await _memory.AppendHistoryAsync("conversation snapshot alpha");

            // Act & Assert: raw SQL UPDATE bypasses EF WORM validation; the DB trigger must block it
            await using var context = _factory.CreateDbContext();
            var ex = await Should.ThrowAsync<SqliteException>(async () =>
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE History SET Summary = 'tampered' WHERE Id = 1"));

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlDelete_OnHistory_IsRejectedByTrigger()
        {
            // Arrange
            await _memory.AppendHistoryAsync("conversation snapshot beta");

            // Act & Assert: raw SQL DELETE bypasses EF WORM validation; the DB trigger must block it
            await using var context = _factory.CreateDbContext();
            var ex = await Should.ThrowAsync<SqliteException>(async () =>
                await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM History WHERE Id = 1"));

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlInsert_OnHistory_Succeeds()
        {
            // Arrange: ensure schema is initialized
            await _memory.AppendHistoryAsync("first entry");

            // Act: raw SQL INSERT should be allowed (WORM = write-once, not write-never)
            await using var context = _factory.CreateDbContext();
            await Should.NotThrowAsync(async () =>
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO History (Summary, Ts) VALUES ('raw insert', datetime('now'))"));

            // Verify both entries exist
            var count = await context.History.CountAsync();
            count.ShouldBe(2);
        }
    }

    [TestFixture]
    [Category("Integration")]
    public sealed class PostgresWormTriggerTests
    {
        private PostgreSqlContainer _container = null!;

        private string _connectionString = null!;

        private Respawner _respawner = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();

            // Create schema by instantiating memory once (triggers MigrateAsync)
            var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                          .UseNpgsql(_connectionString)
                          .Options;
            var factory = new SimpleDbContextFactory<PostgresMemoryContext>(options);
            var memory = new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);
            await memory.AppendHistoryAsync("schema init entry");

            // Initialize Respawn — exclude __EFMigrationsHistory so schema survives resets
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
            });
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await _container.DisposeAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await _respawner.ResetAsync(conn);
        }

        private PostgresMemory CreateMemory()
        {
            var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                          .UseNpgsql(_connectionString)
                          .Options;
            var factory = new SimpleDbContextFactory<PostgresMemoryContext>(options);
            return new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);
        }

        [Test]
        public async Task RawSqlUpdate_OnHistory_IsRejectedByTrigger()
        {
            // Arrange
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("conversation snapshot alpha");

            // Act & Assert: raw SQL UPDATE — the Postgres trigger function must raise an exception
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE \"History\" SET \"Summary\" = 'tampered' WHERE \"Id\" = 1";

            var ex = await Should.ThrowAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync());

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlDelete_OnHistory_IsRejectedByTrigger()
        {
            // Arrange
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("conversation snapshot beta");

            // Act & Assert: raw SQL DELETE — the Postgres trigger function must raise an exception
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM \"History\" WHERE \"Id\" = 1";

            var ex = await Should.ThrowAsync<PostgresException>(async () =>
                await cmd.ExecuteNonQueryAsync());

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlInsert_OnHistory_Succeeds()
        {
            // Arrange: ensure schema is initialized
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("first entry");

            // Act: raw SQL INSERT should be allowed
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO \"History\" (\"Summary\", \"Ts\") VALUES ('raw insert', NOW())";

            await Should.NotThrowAsync(async () =>
                await cmd.ExecuteNonQueryAsync());
        }
    }

    [TestFixture]
    [Category("Integration")]
    public sealed class MsSqlWormTriggerTests
    {
        private MsSqlContainer _container = null!;

        private string _connectionString = null!;

        private Respawner _respawner = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();

            // Create schema by instantiating memory once (triggers MigrateAsync)
            var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                          .UseSqlServer(_connectionString)
                          .Options;
            var factory = new SimpleDbContextFactory<MsSqlMemoryContext>(options);
            var memory = new MsSqlMemory(factory, NullLogger<MsSqlMemory>.Instance);
            await memory.AppendHistoryAsync("schema init entry");

            // Initialize Respawn — exclude __EFMigrationsHistory so schema survives resets
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
            });
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            await _container.DisposeAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await _respawner.ResetAsync(conn);
        }

        private MsSqlMemory CreateMemory()
        {
            var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                          .UseSqlServer(_connectionString)
                          .Options;
            var factory = new SimpleDbContextFactory<MsSqlMemoryContext>(options);
            return new MsSqlMemory(factory, NullLogger<MsSqlMemory>.Instance);
        }

        [Test]
        public async Task RawSqlUpdate_OnHistory_IsRejectedByTrigger()
        {
            // Arrange
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("conversation snapshot alpha");

            // Act & Assert: raw SQL UPDATE — the INSTEAD OF trigger must RAISERROR
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE History SET Summary = 'tampered' WHERE Id = 1";

            var ex = await Should.ThrowAsync<SqlException>(async () =>
                await cmd.ExecuteNonQueryAsync());

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlDelete_OnHistory_IsRejectedByTrigger()
        {
            // Arrange
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("conversation snapshot beta");

            // Act & Assert: raw SQL DELETE — the INSTEAD OF trigger must RAISERROR
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM History WHERE Id = 1";

            var ex = await Should.ThrowAsync<SqlException>(async () =>
                await cmd.ExecuteNonQueryAsync());

            ex.Message.ShouldContain("WORM");
        }

        [Test]
        public async Task RawSqlInsert_OnHistory_Succeeds()
        {
            // Arrange: ensure schema is initialized
            var memory = CreateMemory();
            await memory.AppendHistoryAsync("first entry");

            // Act: raw SQL INSERT should be allowed
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO History (Summary, Ts) VALUES ('raw insert', SYSDATETIMEOFFSET())";

            await Should.NotThrowAsync(async () =>
                await cmd.ExecuteNonQueryAsync());
        }
    }
}