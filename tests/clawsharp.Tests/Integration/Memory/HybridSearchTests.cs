using Clawsharp.Memory.MsSql;
using Clawsharp.Memory.Postgres;
using Clawsharp.Memory.Redis;
using Clawsharp.Memory.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Respawn;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Clawsharp.Tests.Integration.Memory;

/// <summary>
/// Integration tests for SearchHybridAsync across all database backends.
/// Tests the LIKE-fallback path (no embedding provider configured), ClearAsync
/// behavior (facts cleared, history preserved), and access count tracking.
/// </summary>
public static class HybridSearchTests
{
    [TestFixture]
    [Category("Integration")]
    public sealed class SqliteHybridSearchTests
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
            // No embedding provider: SearchHybridAsync falls back to LIKE search
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
        public async Task SearchHybridAsync_MatchingQuery_FindsResults()
        {
            await _memory.AppendFactAsync("user prefers dark mode");
            await _memory.AppendFactAsync("user enjoys hiking on weekends");
            await _memory.AppendFactAsync("user likes pizza with extra cheese");

            var results = await _memory.SearchHybridAsync("pizza");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("pizza"));
        }

        [Test]
        public async Task SearchHybridAsync_NonMatchingQuery_ReturnsEmpty()
        {
            await _memory.AppendFactAsync("user prefers dark mode");
            await _memory.AppendFactAsync("user likes pizza");

            var results = await _memory.SearchHybridAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }

        [Test]
        public async Task SearchHybridAsync_NullEmbedding_FallsBackToLike()
        {
            await _memory.AppendFactAsync("user prefers dark mode");
            await _memory.AppendFactAsync("user enjoys hiking");

            // Explicit null embedding — must fall back to LIKE
            var results = await _memory.SearchHybridAsync("dark", queryEmbedding: null);

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("dark"));
        }

        [Test]
        public async Task SearchHybridAsync_EmptyEmbedding_FallsBackToLike()
        {
            await _memory.AppendFactAsync("user prefers dark mode");

            // Empty array embedding — must fall back to LIKE
            var results = await _memory.SearchHybridAsync("dark", queryEmbedding: []);

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("dark"));
        }

        [Test]
        public async Task SearchHybridAsync_RespectsTopKLimit()
        {
            for (var i = 0; i < 10; i++)
            {
                await _memory.AppendFactAsync($"fact about cats number {i}");
            }

            var results = await _memory.SearchHybridAsync("cats", topK: 3);

            results.Count.ShouldBeLessThanOrEqualTo(3);
        }

        [Test]
        public async Task SearchHybridAsync_IncrementsAccessCount()
        {
            await _memory.AppendFactAsync("user prefers dark mode");

            // Search once to trigger access count increment
            var firstResults = await _memory.SearchHybridAsync("dark");
            firstResults.ShouldNotBeEmpty();
            var factId = firstResults[0].Id;

            // Read the fact directly from DB to verify AccessCount was incremented
            await using var context = _factory.CreateDbContext();
            var fact = await context.Facts.FirstAsync(f => f.Id == factId);
            fact.AccessCount.ShouldBeGreaterThanOrEqualTo(1);
            fact.LastAccessedAt.ShouldNotBeNull();
        }

        [Test]
        public async Task ClearAsync_RemovesFacts_PreservesHistory()
        {
            // Arrange: store both facts and history
            await _memory.AppendFactAsync("user likes cats");
            await _memory.AppendFactAsync("user likes dogs");
            await _memory.AppendHistoryAsync("conversation about pets");

            // Verify facts exist before clear
            var factsBefore = await _memory.ListFactsAsync();
            factsBefore.Count.ShouldBe(2);

            // Act
            await _memory.ClearAsync();

            // Assert: facts gone
            var factsAfter = await _memory.ListFactsAsync();
            factsAfter.ShouldBeEmpty();

            // Assert: history preserved (WORM)
            await using var context = _factory.CreateDbContext();
            var historyCount = await context.History.CountAsync();
            historyCount.ShouldBeGreaterThanOrEqualTo(1);
        }

        [Test]
        public async Task SearchHybridAsync_CaseInsensitive_FindsResults()
        {
            await _memory.AppendFactAsync("User Prefers DARK Mode");

            var results = await _memory.SearchHybridAsync("dark");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("DARK"));
        }

        [Test]
        public async Task FtsSearch_MatchingQuery_FindsResults()
        {
            await _memory.AppendFactAsync("user likes pizza");
            await _memory.AppendFactAsync("user dislikes broccoli");

            // SearchAsync uses FTS5 MATCH on SQLite
            var results = await _memory.SearchAsync("pizza");

            results.ShouldNotBeEmpty();
            results[0].ShouldContain("pizza");
        }

        [Test]
        public async Task FtsSearch_NonMatchingQuery_ReturnsEmpty()
        {
            await _memory.AppendFactAsync("user likes pizza");

            var results = await _memory.SearchAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }
    }

    [TestFixture]
    [Category("Integration")]
    public sealed class PostgresHybridSearchTests
    {
        private PostgreSqlContainer _container = null!;

        private string _connectionString = null!;

        private Respawner _respawner = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg18-trixie").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();

            // Create schema by instantiating memory once (triggers MigrateAsync + InitSchemaAsync)
            var memory = CreateMemory();
            await memory.AppendFactAsync("schema init fact");

            // Initialize Respawn
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore =
                [
                    new Respawn.Graph.Table("__EFMigrationsHistory"),
                    new Respawn.Graph.Table("History"),
                ],
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
                          .UseNpgsql(_connectionString, o => o.UseVector())
                          .Options;
            var factory = new SimpleDbContextFactory<PostgresMemoryContext>(options);
            return new PostgresMemory(factory, NullLogger<PostgresMemory>.Instance);
        }

        [Test]
        public async Task SearchHybridAsync_MatchingQuery_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");
            await memory.AppendFactAsync("user enjoys hiking on weekends");
            await memory.AppendFactAsync("user likes pizza with extra cheese");

            var results = await memory.SearchHybridAsync("pizza");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("pizza"));
        }

        [Test]
        public async Task SearchHybridAsync_NonMatchingQuery_ReturnsEmpty()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");
            await memory.AppendFactAsync("user likes pizza");

            var results = await memory.SearchHybridAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }

        [Test]
        public async Task SearchHybridAsync_NullEmbedding_FallsBackToILike()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var results = await memory.SearchHybridAsync("dark", queryEmbedding: null);

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("dark"));
        }

        [Test]
        public async Task SearchHybridAsync_RespectsTopKLimit()
        {
            var memory = CreateMemory();
            for (var i = 0; i < 10; i++)
            {
                await memory.AppendFactAsync($"fact about cats number {i}");
            }

            var results = await memory.SearchHybridAsync("cats", topK: 3);

            results.Count.ShouldBeLessThanOrEqualTo(3);
        }

        [Test]
        public async Task SearchHybridAsync_IncrementsAccessCount()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            // Search once to trigger access count increment
            var firstResults = await memory.SearchHybridAsync("dark");
            firstResults.ShouldNotBeEmpty();
            var factId = firstResults[0].Id;

            // Read the fact directly from DB to verify AccessCount was incremented
            var options = new DbContextOptionsBuilder<PostgresMemoryContext>()
                          .UseNpgsql(_connectionString, o => o.UseVector())
                          .Options;
            await using var context = new PostgresMemoryContext(options);
            var fact = await context.Facts.FirstAsync(f => f.Id == factId);
            fact.AccessCount.ShouldBeGreaterThanOrEqualTo(1);
            fact.LastAccessedAt.ShouldNotBeNull();
        }

        [Test]
        public async Task ClearAsync_RemovesFacts_PreservesHistory()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes cats");
            await memory.AppendFactAsync("user likes dogs");
            await memory.AppendHistoryAsync("conversation about pets");

            var factsBefore = await memory.ListFactsAsync();
            factsBefore.Count.ShouldBe(2);

            await memory.ClearAsync();

            var factsAfter = await memory.ListFactsAsync();
            factsAfter.ShouldBeEmpty();

            // History preserved — verify directly via raw SQL (WORM triggers block delete)
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM \"History\"";
            var historyCount = (long)(await cmd.ExecuteScalarAsync())!;
            historyCount.ShouldBeGreaterThanOrEqualTo(1);
        }

        [Test]
        public async Task SearchHybridAsync_CaseInsensitive_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("User Prefers DARK Mode");

            var results = await memory.SearchHybridAsync("dark");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("DARK"));
        }

        [Test]
        public async Task TsQuerySearch_MatchingQuery_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes pizza");
            await memory.AppendFactAsync("user dislikes broccoli");

            // SearchAsync uses tsquery on Postgres (ILIKE fallback if tsquery returns nothing)
            var results = await memory.SearchAsync("pizza");

            results.ShouldNotBeEmpty();
            results[0].ShouldContain("pizza");
        }

        [Test]
        public async Task TsQuerySearch_NonMatchingQuery_ReturnsEmpty()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes pizza");

            var results = await memory.SearchAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }
    }

    [TestFixture]
    [Category("Integration")]
    public sealed class MsSqlHybridSearchTests
    {
        private MsSqlContainer _container = null!;

        private string _connectionString = null!;

        private Respawner _respawner = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();

            // Create schema by instantiating memory once (triggers MigrateAsync + InitSchemaAsync)
            var memory = CreateMemory();
            await memory.AppendFactAsync("schema init fact");

            // Initialize Respawn
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore =
                [
                    new Respawn.Graph.Table("__EFMigrationsHistory"),
                    new Respawn.Graph.Table("History"),
                ],
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
        public async Task SearchHybridAsync_MatchingQuery_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");
            await memory.AppendFactAsync("user likes pizza with extra cheese");

            var results = await memory.SearchHybridAsync("pizza");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("pizza"));
        }

        [Test]
        public async Task SearchHybridAsync_NonMatchingQuery_ReturnsEmpty()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var results = await memory.SearchHybridAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }

        [Test]
        public async Task SearchHybridAsync_RespectsTopKLimit()
        {
            var memory = CreateMemory();
            for (var i = 0; i < 10; i++)
            {
                await memory.AppendFactAsync($"fact about cats number {i}");
            }

            var results = await memory.SearchHybridAsync("cats", topK: 3);

            results.Count.ShouldBeLessThanOrEqualTo(3);
        }

        [Test]
        public async Task SearchHybridAsync_IncrementsAccessCount()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var firstResults = await memory.SearchHybridAsync("dark");
            firstResults.ShouldNotBeEmpty();
            var factId = firstResults[0].Id;

            // Read the fact directly from DB to verify AccessCount was incremented
            var options = new DbContextOptionsBuilder<MsSqlMemoryContext>()
                          .UseSqlServer(_connectionString)
                          .Options;
            await using var context = new MsSqlMemoryContext(options);
            var fact = await context.Facts.FirstAsync(f => f.Id == factId);
            fact.AccessCount.ShouldBeGreaterThanOrEqualTo(1);
            fact.LastAccessedAt.ShouldNotBeNull();
        }

        [Test]
        public async Task SearchHybridAsync_CaseInsensitive_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("User Prefers DARK Mode");

            var results = await memory.SearchHybridAsync("dark");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("DARK"));
        }

        [Test]
        public async Task ClearAsync_RemovesFacts_PreservesHistory()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes cats");
            await memory.AppendFactAsync("user likes dogs");
            await memory.AppendHistoryAsync("conversation about pets");

            var factsBefore = await memory.ListFactsAsync();
            factsBefore.Count.ShouldBe(2);

            await memory.ClearAsync();

            var factsAfter = await memory.ListFactsAsync();
            factsAfter.ShouldBeEmpty();

            // History preserved — verify directly via raw SQL
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM History";
            var historyCount = (int)(await cmd.ExecuteScalarAsync())!;
            historyCount.ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    [TestFixture]
    [Category("Integration")]
    public sealed class RedisHybridSearchTests
    {
        private RedisContainer _container = null!;

        private IConnectionMultiplexer _redis = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _container = new RedisBuilder("redis/redis-stack:latest").Build();
            await _container.StartAsync();
            _redis = await ConnectionMultiplexer.ConnectAsync(
                $"{_container.GetConnectionString()},allowAdmin=true");

            // Init schema — trigger lazy index creation
            var memory = CreateMemory();
            await memory.AppendFactAsync("schema init fact");
            await memory.ClearAsync();
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            _redis.Dispose();
            await _container.DisposeAsync();
        }

        [SetUp]
        public async Task SetUp()
        {
            // Flush all keys except the RediSearch index (which is recreated on init)
            var db = _redis.GetDatabase();
            var server = _redis.GetServers()[0];
            await server.FlushDatabaseAsync();

            // Re-initialize schema after flush
            var memory = CreateMemory();
            await memory.AppendFactAsync("setup fact");
            await memory.ClearAsync();
        }

        private RedisMemory CreateMemory()
        {
            return new RedisMemory(_redis, NullLogger<RedisMemory>.Instance);
        }

        [Test]
        public async Task SearchHybridAsync_MatchingQuery_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");
            await memory.AppendFactAsync("user enjoys hiking on weekends");
            await memory.AppendFactAsync("user likes pizza with extra cheese");

            var results = await memory.SearchHybridAsync("pizza");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("pizza"));
        }

        [Test]
        public async Task SearchHybridAsync_NonMatchingQuery_ReturnsEmpty()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");
            await memory.AppendFactAsync("user likes pizza");

            var results = await memory.SearchHybridAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }

        [Test]
        public async Task SearchHybridAsync_NullEmbedding_FallsBackToText()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var results = await memory.SearchHybridAsync("dark", queryEmbedding: null);

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("dark"));
        }

        [Test]
        public async Task SearchHybridAsync_EmptyEmbedding_FallsBackToText()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var results = await memory.SearchHybridAsync("dark", queryEmbedding: []);

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("dark"));
        }

        [Test]
        public async Task SearchHybridAsync_RespectsTopKLimit()
        {
            var memory = CreateMemory();
            for (var i = 0; i < 10; i++)
            {
                await memory.AppendFactAsync($"fact about cats number {i}");
            }

            var results = await memory.SearchHybridAsync("cats", topK: 3);

            results.Count.ShouldBeLessThanOrEqualTo(3);
        }

        [Test]
        public async Task SearchHybridAsync_IncrementsAccessCount()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user prefers dark mode");

            var firstResults = await memory.SearchHybridAsync("dark");
            firstResults.ShouldNotBeEmpty();

            // Search again to verify access count was incremented
            var allFacts = await memory.ListFactsAsync();
            var fact = allFacts.First(f => f.Content.Contains("dark"));
            fact.AccessCount.ShouldBeGreaterThanOrEqualTo(1);
            fact.LastAccessedAt.ShouldNotBeNull();
        }

        [Test]
        public async Task SearchHybridAsync_CaseInsensitive_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("User Prefers DARK Mode");

            var results = await memory.SearchHybridAsync("dark");

            results.ShouldNotBeEmpty();
            results.ShouldContain(f => f.Content.Contains("DARK"));
        }

        [Test]
        public async Task ClearAsync_RemovesFacts_PreservesHistory()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes cats");
            await memory.AppendFactAsync("user likes dogs");
            await memory.AppendHistoryAsync("conversation about pets");

            var factsBefore = await memory.ListFactsAsync();
            factsBefore.Count.ShouldBe(2);

            await memory.ClearAsync();

            var factsAfter = await memory.ListFactsAsync();
            factsAfter.ShouldBeEmpty();

            // History preserved — verify via Redis directly (WORM)
            var db = _redis.GetDatabase();
            var historyKeys = _redis.GetServers()[0]
                .Keys(pattern: "clawsharp:history:*")
                .Where(k => !k.ToString().EndsWith(":seq"))
                .ToList();
            historyKeys.Count.ShouldBeGreaterThanOrEqualTo(1);
        }

        [Test]
        public async Task RediSearchFtSearch_MatchingQuery_FindsResults()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes pizza");
            await memory.AppendFactAsync("user dislikes broccoli");

            // SearchAsync uses RediSearch FT.SEARCH on Redis
            var results = await memory.SearchAsync("pizza");

            results.ShouldNotBeEmpty();
            results[0].ShouldContain("pizza");
        }

        [Test]
        public async Task RediSearchFtSearch_NonMatchingQuery_ReturnsEmpty()
        {
            var memory = CreateMemory();
            await memory.AppendFactAsync("user likes pizza");

            var results = await memory.SearchAsync("xyzzyplugh");

            results.ShouldBeEmpty();
        }
    }
}