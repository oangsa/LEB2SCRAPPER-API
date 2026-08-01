using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using Npgsql;

namespace LEB2SCRAPPER.Repository.Master;

public sealed class AccessKeyRepository : IAccessKeyRepository
{
    private const string AuditActor = "leb2scrapper-api";

    private const string AccessKeyStateSql = """
        SELECT u.id, u.student_id
        FROM keys AS k
        LEFT JOIN user_keys AS uk ON uk.key_id = k.id
        LEFT JOIN users AS u ON u.id = uk.user_id
        WHERE k.id = @key_id;
        """;

    private const string LockKeySql = """
        SELECT id
        FROM keys
        WHERE id = @key_id
        FOR UPDATE;
        """;

    private const string AssignedUserSql = """
        SELECT u.id, u.student_id
        FROM user_keys AS uk
        INNER JOIN users AS u ON u.id = uk.user_id
        WHERE uk.key_id = @key_id
        LIMIT 1;
        """;

    private const string UpsertUserSql = """
        INSERT INTO users (
            id,
            name,
            student_id,
            created_by,
            updated_by,
            created_at,
            updated_at)
        VALUES (
            @user_id,
            @name,
            @student_id,
            @audit_actor,
            @audit_actor,
            CURRENT_TIMESTAMP,
            CURRENT_TIMESTAMP)
        ON CONFLICT (student_id)
        DO UPDATE SET
            name = EXCLUDED.name,
            updated_by = EXCLUDED.updated_by,
            updated_at = CURRENT_TIMESTAMP
        RETURNING id;
        """;

    private const string ClaimKeySql = """
        INSERT INTO user_keys (
            user_id,
            key_id,
            created_by,
            updated_by,
            created_at,
            updated_at)
        VALUES (
            @user_id,
            @key_id,
            @audit_actor,
            @audit_actor,
            CURRENT_TIMESTAMP,
            CURRENT_TIMESTAMP)
        ON CONFLICT (user_id, key_id) DO NOTHING;
        """;

    private readonly string? _connectionString;

    public AccessKeyRepository(string? connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<AccessKeyState?> GetAccessKeyStateAsync(
        Guid keyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                AccessKeyStateSql,
                connection);
            command.Parameters.AddWithValue("key_id", keyId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var userId = reader.IsDBNull(0)
                ? (Guid?)null
                : reader.GetGuid(0);
            var studentId = reader.IsDBNull(1)
                ? null
                : reader.GetString(1);

            return new AccessKeyState(keyId, userId, studentId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (!IsAccessKeyException(exception))
        {
            throw CreateDatabaseException(exception);
        }
    }

    public async Task UpsertUserAndClaimKeyAsync(
        Guid keyId,
        string studentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                cancellationToken);

            await using (var lockCommand = new NpgsqlCommand(
                LockKeySql,
                connection,
                transaction))
            {
                lockCommand.Parameters.AddWithValue("key_id", keyId);

                var key = await lockCommand.ExecuteScalarAsync(cancellationToken);

                if (key is null || key == DBNull.Value)
                {
                    throw new AccessKeyInvalidException();
                }
            }

            Guid? assignedUserId = null;
            string? assignedStudentId = null;

            await using (var assignedCommand = new NpgsqlCommand(
                AssignedUserSql,
                connection,
                transaction))
            {
                assignedCommand.Parameters.AddWithValue("key_id", keyId);
                await using var reader = await assignedCommand.ExecuteReaderAsync(
                    cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    assignedUserId = reader.GetGuid(0);
                    assignedStudentId = reader.GetString(1);
                }
            }

            if (assignedUserId.HasValue
                && !string.Equals(
                    assignedStudentId,
                    studentId,
                    StringComparison.Ordinal))
            {
                throw new AccessKeyAlreadyAssignedException();
            }

            var userId = Guid.NewGuid();

            await using (var userCommand = new NpgsqlCommand(
                UpsertUserSql,
                connection,
                transaction))
            {
                userCommand.Parameters.AddWithValue("user_id", userId);
                userCommand.Parameters.AddWithValue("name", name);
                userCommand.Parameters.AddWithValue("student_id", studentId);
                userCommand.Parameters.AddWithValue("audit_actor", AuditActor);

                var resolvedUserId = await userCommand.ExecuteScalarAsync(
                    cancellationToken);

                if (resolvedUserId is not Guid resolvedGuid)
                {
                    throw new InvalidOperationException(
                        "User upsert did not return a user identifier.");
                }

                userId = resolvedGuid;
            }

            await using (var claimCommand = new NpgsqlCommand(
                ClaimKeySql,
                connection,
                transaction))
            {
                claimCommand.Parameters.AddWithValue("user_id", userId);
                claimCommand.Parameters.AddWithValue("key_id", keyId);
                claimCommand.Parameters.AddWithValue("audit_actor", AuditActor);

                await claimCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AccessKeyAlreadyAssignedException();
        }
        catch (Exception exception) when (!IsAccessKeyException(exception))
        {
            throw CreateDatabaseException(exception);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new AccessKeyDatabaseException(
                false,
                new InvalidOperationException(
                    "ConnectionStrings:Supabase is not configured."));
        }

        var connection = new NpgsqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static bool IsAccessKeyException(Exception exception)
    {
        return exception is AccessKeyInvalidException
            or AccessKeyAlreadyAssignedException
            or AccessKeyDatabaseException;
    }

    private static AccessKeyDatabaseException CreateDatabaseException(
        Exception exception)
    {
        var isTransient = exception is NpgsqlException
            && exception is not PostgresException
            || exception is TimeoutException;

        return new AccessKeyDatabaseException(isTransient, exception);
    }
}
