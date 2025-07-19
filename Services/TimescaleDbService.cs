using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SignalBus.Models;

namespace SignalBus.Services;

public interface ITimescaleDbService
{
    Task QueueMessageAsync(MessageRecord message);
    Task<int> GetQueueSizeAsync();
    Task InitializeDatabaseAsync();
    Task<bool> EnsureDatabaseExistsAsync();
}

public class TimescaleDbService : BackgroundService, ITimescaleDbService
{
    private readonly ILogger<TimescaleDbService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Channel<MessageRecord> _messageQueue;
    private readonly ChannelWriter<MessageRecord> _writer;
    private readonly ChannelReader<MessageRecord> _reader;
    private readonly string _connectionString;
    private readonly int _batchSize;
    private readonly TimeSpan _batchTimeout;
    private readonly SemaphoreSlim _connectionSemaphore;

    public TimescaleDbService(ILogger<TimescaleDbService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Configure batch processing parameters
        _batchSize = int.Parse(_configuration["TIMESCALE_BATCH_SIZE"] ?? "100");
        _batchTimeout = TimeSpan.FromSeconds(int.Parse(_configuration["TIMESCALE_BATCH_TIMEOUT_SECONDS"] ?? "5"));
        
        // Build connection string
        var host = _configuration["TIMESCALE_HOST"] ?? "localhost";
        var port = _configuration["TIMESCALE_PORT"] ?? "5432";
        var database = _configuration["TIMESCALE_DATABASE"] ?? "signalbus";
        var username = _configuration["TIMESCALE_USERNAME"] ?? "postgres";
        var password = _configuration["TIMESCALE_PASSWORD"] ?? throw new InvalidOperationException("TIMESCALE_PASSWORD configuration value is required");
        
        _connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};";
        
        // Create bounded channel for message queue
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        
        _messageQueue = Channel.CreateBounded<MessageRecord>(options);
        _writer = _messageQueue.Writer;
        _reader = _messageQueue.Reader;
        
        // Limit concurrent database connections
        _connectionSemaphore = new SemaphoreSlim(5, 5);
        
        _logger.LogInformation("TimescaleDB service initialized with batch size: {BatchSize}, timeout: {BatchTimeout}s", 
            _batchSize, _batchTimeout.TotalSeconds);
    }

    public async Task QueueMessageAsync(MessageRecord message)
    {
        try
        {
            await _writer.WriteAsync(message);
            _logger.LogDebug("Message queued for batch insertion");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue message for TimescaleDB insertion");
            throw;
        }
    }

    public async Task<int> GetQueueSizeAsync()
    {
        // This is an approximation since Channel doesn't expose exact count
        // We can't get the exact count, so we return a simple indicator
        return await Task.FromResult(0);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TimescaleDB batch processor started");

        var batch = new List<MessageRecord>();
        var lastBatchTime = DateTime.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(_batchTimeout);

                try
                {
                    // Try to read a message with timeout
                    if (await _reader.WaitToReadAsync(timeoutCts.Token))
                    {
                        while (_reader.TryRead(out var message) && batch.Count < _batchSize)
                        {
                            batch.Add(message);
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    // Timeout occurred, process current batch if any
                }

                // Process batch if we have messages and either batch is full or timeout occurred
                var timeSinceLastBatch = DateTime.UtcNow - lastBatchTime;
                if (batch.Count > 0 && (batch.Count >= _batchSize || timeSinceLastBatch >= _batchTimeout))
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                    batch.Clear();
                    lastBatchTime = DateTime.UtcNow;
                }

                timeoutCts.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TimescaleDB batch processor");
        }

        // Process any remaining messages in batch
        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, stoppingToken);
        }

        _logger.LogInformation("TimescaleDB batch processor stopped");
    }

    private async Task ProcessBatchAsync(List<MessageRecord> batch, CancellationToken cancellationToken)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string insertSql = @"
                INSERT INTO signal_messages (
                    timestamp, signal_received_timestamp, signal_delivered_timestamp,
                    target, source, group_chat, mentions, content, created_at
                ) VALUES (
                    @timestamp, @signal_received_timestamp, @signal_delivered_timestamp,
                    @target, @source, @group_chat, @mentions, @content, @created_at
                )";

            using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            
            try
            {
                foreach (var message in batch)
                {
                    using var command = new NpgsqlCommand(insertSql, connection, transaction);
                    
                    command.Parameters.AddWithValue("@timestamp", message.Timestamp);
                    command.Parameters.AddWithValue("@signal_received_timestamp", message.SignalReceivedTimestamp);
                    command.Parameters.AddWithValue("@signal_delivered_timestamp", (object?)message.SignalDeliveredTimestamp ?? DBNull.Value);
                    command.Parameters.AddWithValue("@target", message.Target);
                    command.Parameters.AddWithValue("@source", message.Source);
                    command.Parameters.AddWithValue("@group_chat", (object?)message.GroupChat ?? DBNull.Value);
                    command.Parameters.AddWithValue("@mentions", (object?)message.Mentions ?? DBNull.Value);
                    command.Parameters.AddWithValue("@content", (object?)message.Content ?? DBNull.Value);
                    command.Parameters.AddWithValue("@created_at", message.CreatedAt);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Successfully inserted batch of {Count} messages into TimescaleDB", batch.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to insert batch of {Count} messages, transaction rolled back", batch.Count);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch of {Count} messages", batch.Count);
            throw;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            // Now connect to the database and initialize schema
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Create table if it doesn't exist
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS signal_messages (
                    id BIGSERIAL,
                    timestamp TIMESTAMPTZ NOT NULL,
                    signal_received_timestamp TIMESTAMPTZ NOT NULL,
                    signal_delivered_timestamp TIMESTAMPTZ,
                    target VARCHAR(255) NOT NULL,
                    source VARCHAR(255) NOT NULL,
                    group_chat VARCHAR(255),
                    mentions TEXT,
                    content TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );";

            using var command = new NpgsqlCommand(createTableSql, connection);
            await command.ExecuteNonQueryAsync();

            // Create TimescaleDB hypertable if it doesn't exist
            const string createHypertableSql = @"
                SELECT create_hypertable('signal_messages', 'timestamp', if_not_exists => TRUE);";

            try
            {
                using var hypertableCommand = new NpgsqlCommand(createHypertableSql, connection);
                await hypertableCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("TimescaleDB hypertable created or already exists");

                // Add composite primary key that includes the partitioning column
                const string addPrimaryKeySql = @"
                    ALTER TABLE signal_messages ADD CONSTRAINT signal_messages_pkey PRIMARY KEY (id, timestamp);";

                try
                {
                    using var pkCommand = new NpgsqlCommand(addPrimaryKeySql, connection);
                    await pkCommand.ExecuteNonQueryAsync();
                    _logger.LogInformation("Composite primary key created successfully");
                }
                catch (Exception pkEx)
                {
                    _logger.LogWarning(pkEx, "Failed to create composite primary key - it may already exist");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create hypertable - this is normal if TimescaleDB extension is not installed");
            }

            // Create indexes for better query performance
            const string createIndexesSql = @"
                CREATE INDEX IF NOT EXISTS idx_signal_messages_timestamp ON signal_messages (timestamp);
                CREATE INDEX IF NOT EXISTS idx_signal_messages_source ON signal_messages (source);
                CREATE INDEX IF NOT EXISTS idx_signal_messages_target ON signal_messages (target);
                CREATE INDEX IF NOT EXISTS idx_signal_messages_created_at ON signal_messages (created_at);";

            using var indexCommand = new NpgsqlCommand(createIndexesSql, connection);
            await indexCommand.ExecuteNonQueryAsync();

            _logger.LogInformation("TimescaleDB schema initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TimescaleDB schema");
            throw;
        }
    }

    public async Task<bool> EnsureDatabaseExistsAsync()
    {
        try
        {
            // Create connection string to connect to postgres database (default database)
            var host = _configuration["TIMESCALE_HOST"] ?? "localhost";
            var port = _configuration["TIMESCALE_PORT"] ?? "5432";
            var database = _configuration["TIMESCALE_DATABASE"] ?? "signalbus";
            var username = _configuration["TIMESCALE_USERNAME"] ?? "postgres";
            var password = _configuration["TIMESCALE_PASSWORD"] ?? throw new InvalidOperationException("TIMESCALE_PASSWORD configuration value is required");

            var postgresConnectionString = $"Host={host};Port={port};Database=postgres;Username={username};Password={password};";

            using var connection = new NpgsqlConnection(postgresConnectionString);
            await connection.OpenAsync();

            // Check if database exists
            string checkDatabaseSql = $@"SELECT 1 FROM pg_database WHERE datname = '{database}';";

            using var checkCommand = new NpgsqlCommand(checkDatabaseSql, connection);
            var result = await checkCommand.ExecuteScalarAsync();

            if (result == null)
            {
                // Database doesn't exist, create it
                string createDatabaseSql = $@"CREATE DATABASE {database};";

                using var createCommand = new NpgsqlCommand(createDatabaseSql, connection);
                await createCommand.ExecuteNonQueryAsync();

                _logger.LogInformation("Created database");
                return false;
            }
            else
            {
                _logger.LogInformation("database already exists, skipping creation");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure database exists");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping TimescaleDB service...");
        
        // Signal no more writes
        _writer.Complete();
        
        // Wait for background service to finish processing
        await base.StopAsync(cancellationToken);
        
        _connectionSemaphore.Dispose();
        _logger.LogInformation("TimescaleDB service stopped");
    }
}
