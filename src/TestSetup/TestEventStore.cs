using Marten;
using Npgsql;
using Weasel.Core;

namespace TestSetup;

public class PostgresAdministration
{
  private readonly string _connectionString;

  public PostgresAdministration(
    string connectionString
  )
  {
    _connectionString = connectionString;
  }

  public async Task CreateDatabaseAsync(
    string? databaseName
  )
  {
    await using var connection = new NpgsqlConnection();
    connection.ConnectionString = _connectionString;
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"CREATE DATABASE {databaseName}",
      connection
    );
    await command.ExecuteNonQueryAsync();
    await connection.CloseAsync();
  }

  public async Task DropDatabase(
    string? databaseName
  )
  {
    await using var connection = new NpgsqlConnection();
    connection.ConnectionString = _connectionString;
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);",
      connection
    );
    await command.ExecuteNonQueryAsync();
    await connection.CloseAsync();
  }

  public async Task<bool> EnsureDatabaseExists(
    string databaseName
  )
  {
    await using var connection = new NpgsqlConnection();
    connection.ConnectionString = _connectionString;

    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
      $"SELECT 1 FROM pg_database WHERE datname LIKE '{databaseName}'",
      connection
    );

    var result = await command.ExecuteScalarAsync();
    await connection.CloseAsync();

    return result != null;
  }
}

public class TestDatabase
{
  public string ConnectionString { get; }
  private readonly PostgresAdministration _postgresAdministration;
  private string? _testDatabaseName;

  private TestDatabase(
    string testDatabaseName,
    string connectionString
  )
  {
    ConnectionString = connectionString;
    _postgresAdministration = new PostgresAdministration(
      GetMasterDbConnectionString()
    );

    _testDatabaseName = testDatabaseName;
  }

  public static string GetMasterDbConnectionString() =>
    new NpgsqlConnectionStringBuilder
    {
      Pooling = false,
      Port = 5435,
      Host = "localhost",
      CommandTimeout = 20,
      Database = "marten",
      Password = "marten",
      Username = "marten"
    }.ToString();

  public static async Task<TestDatabase> InitializeAsync(
  )
  {
    Task
      .Delay(new Random().Next(50, 100))
      .Wait();

    var dbId = DateTime.UtcNow.ToString("yyyy_MM_dd_HH_mm_ss_fff");
    var testDatabaseName = $"example_es_test_{dbId}_{Guid.NewGuid().ToString()[..4]}";
    var postgresAdministration = new PostgresAdministration(GetMasterDbConnectionString());
    await postgresAdministration.CreateDatabaseAsync(
      testDatabaseName
    );

    var npgsqlConnectionStringBuilder = new NpgsqlConnectionStringBuilder
    {
      Pooling = false,
      Port = 5435,
      Host = "localhost",
      CommandTimeout = 20,
      Database = testDatabaseName,
      Password = "marten",
      Username = "marten"
    };

    var testDatabaseConnectionString = npgsqlConnectionStringBuilder.ToString();

    return new TestDatabase(testDatabaseName, testDatabaseConnectionString);
  }

  public async Task DropAsync() => await _postgresAdministration.DropDatabase(_testDatabaseName);
}

public static class StoreConfiguration
{
  public static StoreOptions Configure(
    StoreOptions options
  )
  {
    options.AutoCreateSchemaObjects = AutoCreate.All;
    return options;
  }
}

public class TestEventStore
{
  public string MasterDbConnectionString { get; }
  public IDocumentStore Store { get; }
  private TestDatabase TestDatabase { get; }

  private TestEventStore(
    IDocumentStore store,
    TestDatabase testDatabase,
    string masterDbConnectionString
  )
  {
    Store = store;
    TestDatabase = testDatabase;
    MasterDbConnectionString = masterDbConnectionString;
  }

  public static async Task<TestEventStore> InitializeAsync(
    Action<StoreOptions>? configureStoreOptions = null
  )
  {
    var testDatabase = await TestDatabase.InitializeAsync();

    var store = DocumentStore.For(
      options =>
      {
        options.Connection(testDatabase.ConnectionString);
        StoreConfiguration.Configure(options);
        options.DisableNpgsqlLogging = true;
        configureStoreOptions?.Invoke(options);
      }
    );

    return new TestEventStore(
      store,
      testDatabase,
      testDatabase.ConnectionString
    );
  }

  public async Task DisposeAsync()
  {
    await TestDatabase.DropAsync();
    Store.Dispose();
  }
}
