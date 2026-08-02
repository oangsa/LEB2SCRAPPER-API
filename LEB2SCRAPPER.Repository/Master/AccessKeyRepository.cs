using LEB2SCRAPPER.Contracts.Repository;
using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using Npgsql;

namespace LEB2SCRAPPER.Repository.Master;

public sealed class AccessKeyRepository : IAccessKeyRepository
{
    private const string AuditActor = "leb2scrapper-api";
    private const string UserKeyUniqueConstraint = "uq_user_keys_key";
    private const string Leb2UserIdUniqueConstraint = "uq_users_leb2_user_id";
    private const string ActiveDeviceUniqueConstraint =
        "uq_key_device_bindings_active_key";

    private const string AccessKeyStateSql = """
        SELECT u.id, u.student_id, u.leb2_user_id, kdb.device_id_hash
        FROM keys AS k
        LEFT JOIN user_keys AS uk ON uk.key_id = k.id
        LEFT JOIN users AS u ON u.id = uk.user_id
        LEFT JOIN key_device_bindings AS kdb
            ON kdb.key_id = k.id
            AND kdb.unbound_at IS NULL
        WHERE k.id = @key_id;
        """;

    private const string LockKeySql = """
        SELECT id
        FROM keys
        WHERE id = @key_id
        FOR UPDATE;
        """;

    private const string AssignedUserSql = """
        SELECT u.id, u.student_id, u.leb2_user_id
        FROM user_keys AS uk
        INNER JOIN users AS u ON u.id = uk.user_id
        WHERE uk.key_id = @key_id
        LIMIT 1;
        """;

    private const string ExistingUserSql = """
        SELECT id, leb2_user_id
        FROM users
        WHERE student_id = @student_id
        FOR UPDATE;
        """;

    private const string InsertUserSql = """
        INSERT INTO users (
            id,
            name,
            student_id,
            leb2_user_id,
            created_by,
            updated_by,
            created_at,
            updated_at)
        VALUES (
            @user_id,
            @name,
            @student_id,
            @leb2_user_id,
            @audit_actor,
            @audit_actor,
            CURRENT_TIMESTAMP,
            CURRENT_TIMESTAMP)
        ON CONFLICT DO NOTHING
        RETURNING id;
        """;

    private const string UpdateUserSql = """
        UPDATE users
        SET
            name = @name,
            leb2_user_id = @leb2_user_id,
            updated_by = @audit_actor,
            updated_at = CURRENT_TIMESTAMP
        WHERE id = @user_id
            AND (leb2_user_id IS NULL OR leb2_user_id = @leb2_user_id)
        RETURNING id;
        """;

    private const string Leb2IdentitySql = """
        SELECT id
        FROM users
        WHERE leb2_user_id = @leb2_user_id
        LIMIT 1;
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

    private const string ActiveDeviceSql = """
        SELECT device_id_hash
        FROM key_device_bindings
        WHERE key_id = @key_id
            AND unbound_at IS NULL
        ORDER BY bound_at DESC
        LIMIT 1
        FOR UPDATE;
        """;

    private const string InsertDeviceSql = """
        INSERT INTO key_device_bindings (
            key_id,
            device_id_hash,
            device_name,
            platform,
            os_version,
            app_version,
            bound_at,
            created_by,
            updated_by,
            created_at,
            updated_at)
        VALUES (
            @key_id,
            @device_id_hash,
            @device_name,
            @platform,
            @os_version,
            @app_version,
            CURRENT_TIMESTAMP,
            @audit_actor,
            @audit_actor,
            CURRENT_TIMESTAMP,
            CURRENT_TIMESTAMP);
        """;

    private const string UpdateDeviceSql = """
        UPDATE key_device_bindings
        SET
            device_name = @device_name,
            platform = @platform,
            os_version = @os_version,
            app_version = @app_version,
            updated_by = @audit_actor,
            updated_at = CURRENT_TIMESTAMP
        WHERE key_id = @key_id
            AND device_id_hash = @device_id_hash
            AND unbound_at IS NULL;
        """;

    private const string UnbindDeviceSql = """
        UPDATE key_device_bindings
        SET
            unbound_at = CURRENT_TIMESTAMP,
            unbound_reason = @reason,
            updated_by = @audit_actor,
            updated_at = CURRENT_TIMESTAMP
        WHERE key_id = @key_id
            AND device_id_hash = @device_id_hash
            AND unbound_at IS NULL;
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
            var leb2UserId = reader.IsDBNull(2)
                ? (int?)null
                : reader.GetInt32(2);
            var deviceIdHash = reader.IsDBNull(3)
                ? null
                : reader.GetString(3);

            return new AccessKeyState(
                keyId,
                userId,
                studentId,
                leb2UserId,
                deviceIdHash);
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
        int leb2UserId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await UpsertUserAndClaimKeyCoreAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            null,
            cancellationToken);
    }

    public Task UpsertUserAndClaimKeyWithDeviceAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingData deviceBinding,
        CancellationToken cancellationToken = default)
    {
        return UpsertUserAndClaimKeyCoreAsync(
            keyId,
            studentId,
            leb2UserId,
            name,
            deviceBinding,
            cancellationToken);
    }

    private async Task UpsertUserAndClaimKeyCoreAsync(
        Guid keyId,
        string studentId,
        int leb2UserId,
        string name,
        DeviceBindingData? deviceBinding,
        CancellationToken cancellationToken)
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

            var existingUser = await FindUserForUpdateAsync(
                connection,
                transaction,
                studentId,
                cancellationToken);
            Guid userId;

            if (existingUser.HasValue)
            {
                EnsureLeb2IdentityMatches(existingUser.Value.Leb2UserId, leb2UserId);
                userId = existingUser.Value.UserId;
                await UpdateUserAsync(
                    connection,
                    transaction,
                    userId,
                    leb2UserId,
                    name,
                    cancellationToken);
            }
            else
            {
                var insertedUserId = await InsertUserAsync(
                    connection,
                    transaction,
                    studentId,
                    leb2UserId,
                    name,
                    cancellationToken);

                if (insertedUserId.HasValue)
                {
                    userId = insertedUserId.Value;
                }
                else
                {
                    existingUser = await FindUserForUpdateAsync(
                        connection,
                        transaction,
                        studentId,
                        cancellationToken);

                    if (existingUser.HasValue)
                    {
                        EnsureLeb2IdentityMatches(
                            existingUser.Value.Leb2UserId,
                            leb2UserId);
                        userId = existingUser.Value.UserId;
                        await UpdateUserAsync(
                            connection,
                            transaction,
                            userId,
                            leb2UserId,
                            name,
                            cancellationToken);
                    }
                    else if (await Leb2IdentityExistsAsync(
                                 connection,
                                 transaction,
                                 leb2UserId,
                                 cancellationToken))
                    {
                        throw new AccessKeyIdentityConflictException();
                    }
                    else
                    {
                        throw new AccessKeyDatabaseException(
                            false,
                            new InvalidOperationException(
                                "The local user could not be resolved after insertion."));
                    }
                }
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

            if (deviceBinding is not null)
            {
                await BindDeviceWithinTransactionAsync(
                    connection,
                    transaction,
                    keyId,
                    deviceBinding,
                    cancellationToken);
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
            if (string.Equals(
                    exception.ConstraintName,
                    ActiveDeviceUniqueConstraint,
                    StringComparison.Ordinal))
            {
                throw new DeviceBindingMismatchException();
            }

            throw CreateUniqueViolationException(exception);
        }
        catch (Exception exception) when (!IsAccessKeyException(exception))
        {
            throw CreateDatabaseException(exception);
        }
    }

    public async Task UnbindDeviceAsync(
        Guid keyId,
        string deviceIdHash,
        string reason,
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

            await using (var command = new NpgsqlCommand(
                UnbindDeviceSql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("key_id", keyId);
                command.Parameters.AddWithValue("device_id_hash", deviceIdHash);
                command.Parameters.AddWithValue("reason", reason);
                command.Parameters.AddWithValue("audit_actor", AuditActor);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
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

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new AccessKeyDatabaseException(
                false,
                new InvalidOperationException(
                    "The access-key database connection is not configured."));
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

    private static async Task<(Guid UserId, int? Leb2UserId)?> FindUserForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string studentId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            ExistingUserSql,
            connection,
            transaction);
        command.Parameters.AddWithValue("student_id", studentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetGuid(0),
            reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1));
    }

    private static async Task<Guid?> InsertUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string studentId,
        int leb2UserId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            InsertUserSql,
            connection,
            transaction);
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("student_id", studentId);
        command.Parameters.AddWithValue("leb2_user_id", leb2UserId);
        command.Parameters.AddWithValue("audit_actor", AuditActor);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid userId ? userId : null;
    }

    private static async Task UpdateUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        int leb2UserId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            UpdateUserSql,
            connection,
            transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("leb2_user_id", leb2UserId);
        command.Parameters.AddWithValue("audit_actor", AuditActor);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result is not Guid)
        {
            throw new AccessKeyIdentityConflictException();
        }
    }

    private static async Task<bool> Leb2IdentityExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int leb2UserId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            Leb2IdentitySql,
            connection,
            transaction);
        command.Parameters.AddWithValue("leb2_user_id", leb2UserId);

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task BindDeviceWithinTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid keyId,
        DeviceBindingData deviceBinding,
        CancellationToken cancellationToken)
    {
        string? activeDeviceHash;

        await using (var activeCommand = new NpgsqlCommand(
            ActiveDeviceSql,
            connection,
            transaction))
        {
            activeCommand.Parameters.AddWithValue("key_id", keyId);
            activeDeviceHash = await activeCommand.ExecuteScalarAsync(
                cancellationToken) as string;
        }

        if (activeDeviceHash is not null)
        {
            if (!string.Equals(
                    activeDeviceHash,
                    deviceBinding.DeviceIdHash,
                    StringComparison.Ordinal))
            {
                throw new DeviceBindingMismatchException();
            }

            await using var updateCommand = new NpgsqlCommand(
                UpdateDeviceSql,
                connection,
                transaction);
            AddDeviceParameters(updateCommand, keyId, deviceBinding);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var insertCommand = new NpgsqlCommand(
            InsertDeviceSql,
            connection,
            transaction);
        AddDeviceParameters(insertCommand, keyId, deviceBinding);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDeviceParameters(
        NpgsqlCommand command,
        Guid keyId,
        DeviceBindingData deviceBinding)
    {
        command.Parameters.AddWithValue("key_id", keyId);
        command.Parameters.AddWithValue(
            "device_id_hash",
            deviceBinding.DeviceIdHash);
        command.Parameters.AddWithValue(
            "device_name",
            (object?)deviceBinding.DeviceName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "platform",
            (object?)deviceBinding.Platform ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "os_version",
            (object?)deviceBinding.OsVersion ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "app_version",
            (object?)deviceBinding.AppVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("audit_actor", AuditActor);
    }

    private static void EnsureLeb2IdentityMatches(
        int? existingLeb2UserId,
        int requestedLeb2UserId)
    {
        if (existingLeb2UserId.HasValue
            && existingLeb2UserId.Value != requestedLeb2UserId)
        {
            throw new AccessKeyIdentityConflictException();
        }
    }

    private static bool IsAccessKeyException(Exception exception)
    {
        return exception is AccessKeyInvalidException
            or AccessKeyAlreadyAssignedException
            or AccessKeyIdentityConflictException
            or AccessKeyIdentityMismatchException
            or AccessKeyReauthenticationRequiredException
            or DeviceBindingMismatchException
            or AccessKeyDatabaseException;
    }

    private static Exception CreateUniqueViolationException(
        PostgresException exception)
    {
        if (IsUserKeyAssignmentConflict(exception))
        {
            return new AccessKeyAlreadyAssignedException();
        }

        if (IsLeb2UserIdentityConflict(exception))
        {
            return new AccessKeyIdentityConflictException();
        }

        return CreateDatabaseException(exception);
    }

    private static bool IsUserKeyAssignmentConflict(PostgresException exception)
    {
        if (string.Equals(
                exception.ConstraintName,
                UserKeyUniqueConstraint,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
                exception.TableName,
                "user_keys",
                StringComparison.Ordinal)
            && string.Equals(
                exception.ColumnName,
                "key_id",
                StringComparison.Ordinal);
    }

    private static bool IsLeb2UserIdentityConflict(PostgresException exception)
    {
        if (string.Equals(
                exception.ConstraintName,
                Leb2UserIdUniqueConstraint,
                StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
                exception.TableName,
                "users",
                StringComparison.Ordinal)
            && string.Equals(
                exception.ColumnName,
                "leb2_user_id",
                StringComparison.Ordinal);
    }

    internal static AccessKeyDatabaseException CreateDatabaseException(
        Exception exception)
    {
        var isTransient = (exception is NpgsqlException npgsqlException
                && npgsqlException.IsTransient)
            || exception is TimeoutException;

        return new AccessKeyDatabaseException(isTransient, exception);
    }
}
