using LEB2SCRAPPER.Entity.Exceptions.AccessKey;
using LEB2SCRAPPER.Entity.Models.AccessKey;
using LEB2SCRAPPER.Repository.Master;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace LEB2SCRAPPER.Tests.Repository;

public sealed class AccessKeyRepositoryTests : IClassFixture<AccessKeyDatabaseFixture>
{
    private static readonly Guid KeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondKeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ExistingUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid ConflictingUserId =
        Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid RollbackKeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000009");

    private readonly AccessKeyDatabaseFixture _fixture;

    public AccessKeyRepositoryTests(AccessKeyDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Lookup_ReturnsProvisionedUnassignedState()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);

        var state = await CreateRepository().GetAccessKeyStateAsync(KeyId);

        Assert.NotNull(state);
        Assert.Equal(KeyId, state.KeyId);
        Assert.False(state.IsAssigned);
        Assert.Null(state.UserId);
        Assert.Null(state.StudentId);
        Assert.Null(state.Leb2UserId);
    }

    [Fact]
    public async Task Lookup_ReturnsStoredStudentAndLeb2Identity()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertUserAsync(
            ExistingUserId,
            "student-001",
            1001,
            "Example Student");
        await LinkKeyAsync(ExistingUserId, KeyId);

        var state = await CreateRepository().GetAccessKeyStateAsync(KeyId);

        Assert.NotNull(state);
        Assert.Equal(KeyId, state.KeyId);
        Assert.Equal(ExistingUserId, state.UserId);
        Assert.Equal("student-001", state.StudentId);
        Assert.Equal(1001, state.Leb2UserId);
    }

    [Fact]
    public async Task UpsertUserAndClaimKey_CreatesUserAndAssignment()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);

        await CreateRepository().UpsertUserAndClaimKeyAsync(
            KeyId,
            "student-001",
            1001,
            "Example Student");

        var state = await CreateRepository().GetAccessKeyStateAsync(KeyId);

        Assert.NotNull(state);
        Assert.True(state.IsAssigned);
        Assert.Equal("student-001", state.StudentId);
        Assert.Equal(1001, state.Leb2UserId);
        Assert.Equal(
            "Example Student",
            await ScalarAsync<string>(
                "SELECT name FROM users WHERE student_id = 'student-001';"));
    }

    [Fact]
    public async Task UpsertUserAndClaimKey_PopulatesNullExistingLeb2Identity()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertUserAsync(
            ExistingUserId,
            "student-001",
            null,
            "Old Name");

        await CreateRepository().UpsertUserAndClaimKeyAsync(
            KeyId,
            "student-001",
            1001,
            "Updated Name");

        var state = await CreateRepository().GetAccessKeyStateAsync(KeyId);

        Assert.Equal(ExistingUserId, state?.UserId);
        Assert.Equal(1001, state?.Leb2UserId);
        Assert.Equal(
            "Updated Name",
            await ScalarAsync<string>(
                "SELECT name FROM users WHERE id = @user_id;",
                command => command.Parameters.AddWithValue("user_id", ExistingUserId)));
    }

    [Fact]
    public async Task SameKeyConcurrency_AllowsOnlyOneStudent()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        var repository = CreateRepository();

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One")),
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-002",
                1002,
                "Student Two")));

        Assert.Single(outcomes, exception => exception is null);
        Assert.Single(
            outcomes,
            exception => exception is AccessKeyAlreadyAssignedException);
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE key_id = @key_id;",
            command => command.Parameters.AddWithValue("key_id", KeyId)));
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users;"));
    }

    [Fact]
    public async Task SameKeySameStudentConcurrency_AllowsBothClaims()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        var repository = CreateRepository();

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One")),
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One Updated")));

        Assert.All(outcomes, exception => Assert.Null(exception));
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE student_id = 'student-001';"));
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE key_id = @key_id;",
            command => command.Parameters.AddWithValue("key_id", KeyId)));
    }

    [Fact]
    public async Task SameStudentDifferentKeysConcurrency_AllowsBothClaims()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertKeyAsync(SecondKeyId);
        var repository = CreateRepository();

        var outcomes = await Task.WhenAll(
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One")),
            CaptureAsync(() => repository.UpsertUserAndClaimKeyAsync(
                SecondKeyId,
                "student-001",
                1001,
                "Student One Updated")));

        Assert.All(outcomes, exception => Assert.Null(exception));
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE student_id = 'student-001';"));
        var state = await CreateRepository().GetAccessKeyStateAsync(KeyId);
        Assert.NotNull(state?.UserId);
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE user_id = @user_id;",
            command => command.Parameters.AddWithValue("user_id", state!.UserId!.Value)));
    }

    [Fact]
    public void DatabaseException_UsesNpgsqlTransientClassification()
    {
        var transient = new NpgsqlException(
            "synthetic transient failure",
            new TimeoutException());
        var nonTransient = new NpgsqlException("synthetic non-transient failure");
        var postgres = new PostgresException(
            "synthetic unique violation",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);

        Assert.True(transient.IsTransient);
        Assert.False(nonTransient.IsTransient);
        Assert.False(postgres.IsTransient);
        Assert.Equal(
            transient.IsTransient,
            AccessKeyRepository.CreateDatabaseException(transient).IsTransient);
        Assert.Equal(
            nonTransient.IsTransient,
            AccessKeyRepository.CreateDatabaseException(nonTransient).IsTransient);
        Assert.Equal(
            postgres.IsTransient,
            AccessKeyRepository.CreateDatabaseException(postgres).IsTransient);
    }

    [Fact]
    public async Task DifferentStudentOnSameKey_IsRejected()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        var repository = CreateRepository();

        await repository.UpsertUserAndClaimKeyAsync(
            KeyId,
            "student-001",
            1001,
            "Student One");

        await Assert.ThrowsAsync<AccessKeyAlreadyAssignedException>(() =>
            repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-002",
                1002,
                "Student Two"));
    }

    [Fact]
    public async Task SameStudentOnDifferentKeys_ReusesExistingUser()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertKeyAsync(SecondKeyId);
        var repository = CreateRepository();

        await repository.UpsertUserAndClaimKeyAsync(
            KeyId,
            "student-001",
            1001,
            "Student One");
        await repository.UpsertUserAndClaimKeyAsync(
            SecondKeyId,
            "student-001",
            1001,
            "Student One Updated");

        var firstState = await repository.GetAccessKeyStateAsync(KeyId);
        var secondState = await repository.GetAccessKeyStateAsync(SecondKeyId);

        Assert.Equal(firstState?.UserId, secondState?.UserId);
        Assert.Equal(1, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE student_id = 'student-001';"));
        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE user_id = @user_id;",
            command => command.Parameters.AddWithValue("user_id", firstState!.UserId!.Value)));
    }

    [Fact]
    public async Task EstablishedDifferentLeb2Identity_FailsClosedWithoutUpdate()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertUserAsync(
            ExistingUserId,
            "student-001",
            1001,
            "Original Name");

        await Assert.ThrowsAsync<AccessKeyIdentityConflictException>(() =>
            CreateRepository().UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1002,
                "Changed Name"));

        Assert.Equal(
            "Original Name",
            await ScalarAsync<string>(
                "SELECT name FROM users WHERE student_id = 'student-001';"));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE key_id = @key_id;",
            command => command.Parameters.AddWithValue("key_id", KeyId)));
    }

    [Fact]
    public async Task ClaimUniqueViolation_MapsAndRollsBackUserUpsert()
    {
        await ResetAsync();
        await InsertKeyAsync(RollbackKeyId);
        await InsertUserAsync(
            ConflictingUserId,
            "competing-student",
            2001,
            "Competing Student");

        await Assert.ThrowsAsync<AccessKeyAlreadyAssignedException>(() =>
            CreateRepository().UpsertUserAndClaimKeyAsync(
                RollbackKeyId,
                "student-001",
                1001,
                "Student One"));

        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE student_id = 'student-001';"));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE key_id = @key_id;",
            command => command.Parameters.AddWithValue("key_id", RollbackKeyId)));
    }

    [Fact]
    public async Task Leb2UserIdentityUniqueViolation_MapsToControlledConflict()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertUserAsync(
            ExistingUserId,
            "existing-student",
            1001,
            "Existing Student");

        await Assert.ThrowsAsync<AccessKeyIdentityConflictException>(() =>
            CreateRepository().UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One"));

        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM users WHERE student_id = 'student-001';"));
    }

    [Fact]
    public async Task Leb2UserIdentityUniqueViolationDuringUpdate_RollsBackTargetUser()
    {
        await ResetAsync();
        await InsertKeyAsync(KeyId);
        await InsertUserAsync(
            ExistingUserId,
            "student-001",
            null,
            "Original Name");
        await InsertUserAsync(
            ConflictingUserId,
            "existing-student",
            1001,
            "Existing Student");

        await Assert.ThrowsAsync<AccessKeyIdentityConflictException>(() =>
            CreateRepository().UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Changed Name"));

        Assert.Equal(
            "Original Name",
            await ScalarAsync<string>(
                "SELECT name FROM users WHERE student_id = 'student-001';"));
        Assert.True(await ScalarAsync<bool>(
            "SELECT leb2_user_id IS NULL FROM users "
            + "WHERE student_id = 'student-001';"));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT COUNT(*) FROM user_keys WHERE key_id = @key_id;",
            command => command.Parameters.AddWithValue("key_id", KeyId)));
    }

    [Fact]
    public async Task UnknownKey_IsRejectedForLookupAndClaim()
    {
        await ResetAsync();
        var repository = CreateRepository();

        Assert.Null(await repository.GetAccessKeyStateAsync(KeyId));
        await Assert.ThrowsAsync<AccessKeyInvalidException>(() =>
            repository.UpsertUserAndClaimKeyAsync(
                KeyId,
                "student-001",
                1001,
                "Student One"));
    }

    private AccessKeyRepository CreateRepository()
    {
        return new AccessKeyRepository(_fixture.ConnectionString);
    }

    private async Task ResetAsync()
    {
        await _fixture.ExecuteAsync(
            "TRUNCATE TABLE user_keys, users, keys CASCADE;");
    }

    private async Task InsertKeyAsync(Guid keyId)
    {
        await _fixture.ExecuteAsync(
            "INSERT INTO keys (id, created_by, updated_by) "
            + "VALUES (@id, 'test', 'test');",
            command => command.Parameters.AddWithValue("id", keyId));
    }

    private async Task InsertUserAsync(
        Guid userId,
        string studentId,
        int? leb2UserId,
        string name)
    {
        await _fixture.ExecuteAsync(
            "INSERT INTO users (id, name, student_id, leb2_user_id, "
            + "created_by, updated_by, created_at, updated_at) "
            + "VALUES (@id, @name, @student_id, @leb2_user_id, "
            + "'test', 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);",
            command =>
            {
                command.Parameters.AddWithValue("id", userId);
                command.Parameters.AddWithValue("name", name);
                command.Parameters.AddWithValue("student_id", studentId);
                var parameter = command.Parameters.Add(
                    "leb2_user_id",
                    NpgsqlDbType.Integer);
                parameter.Value = leb2UserId.HasValue
                    ? leb2UserId.Value
                    : DBNull.Value;
            });
    }

    private async Task LinkKeyAsync(Guid userId, Guid keyId)
    {
        await _fixture.ExecuteAsync(
            "INSERT INTO user_keys (user_id, key_id, created_by, updated_by, "
            + "created_at, updated_at) VALUES (@user_id, @key_id, "
            + "'test', 'test', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);",
            command =>
            {
                command.Parameters.AddWithValue("user_id", userId);
                command.Parameters.AddWithValue("key_id", keyId);
            });
    }

    private async Task<T> ScalarAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure = null)
    {
        return await _fixture.ScalarAsync<T>(sql, configure);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}

public sealed class AccessKeyDatabaseFixture : IAsyncLifetime
{
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer _container;

    public AccessKeyDatabaseFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("access_key_tests")
            .WithUsername("postgres")
            .WithPassword(_password)
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await CreateSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task ExecuteAsync(
        string sql,
        Action<NpgsqlCommand>? configure = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure?.Invoke(command);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        configure?.Invoke(command);
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value, typeof(T))!;
    }

    private async Task CreateSchemaAsync()
    {
        await ExecuteAsync(
            """
            CREATE TABLE users (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                student_id text NOT NULL UNIQUE,
                leb2_user_id integer,
                created_by text NOT NULL,
                updated_by text NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL
            );

            CREATE UNIQUE INDEX uq_users_leb2_user_id
                ON users (leb2_user_id)
                WHERE leb2_user_id IS NOT NULL;

            CREATE TABLE keys (
                id uuid PRIMARY KEY,
                created_by text NOT NULL,
                updated_by text NOT NULL
            );

            CREATE TABLE user_keys (
                user_id uuid NOT NULL,
                key_id uuid NOT NULL,
                created_by text NOT NULL,
                updated_by text NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                CONSTRAINT pk_user_keys PRIMARY KEY (user_id, key_id),
                CONSTRAINT fk_user_keys_user_id
                    FOREIGN KEY (user_id)
                    REFERENCES users(id)
                    ON DELETE CASCADE,
                CONSTRAINT fk_user_keys_key_id
                    FOREIGN KEY (key_id)
                    REFERENCES keys(id)
                    ON DELETE CASCADE,
                CONSTRAINT uq_user_keys_key UNIQUE (key_id)
            );

            CREATE OR REPLACE FUNCTION force_claim_conflict()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.key_id = '00000000-0000-0000-0000-000000000009'::uuid
                    AND pg_trigger_depth() = 1 THEN
                    INSERT INTO user_keys (
                        user_id,
                        key_id,
                        created_by,
                        updated_by,
                        created_at,
                        updated_at)
                    VALUES (
                        '00000000-0000-0000-0000-000000000004'::uuid,
                        NEW.key_id,
                        'test',
                        'test',
                        CURRENT_TIMESTAMP,
                        CURRENT_TIMESTAMP);
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER force_claim_conflict_trigger
                BEFORE INSERT ON user_keys
                FOR EACH ROW
                EXECUTE FUNCTION force_claim_conflict();
            """);
    }
}
