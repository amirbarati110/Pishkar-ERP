using System.Data;
using ERP.Application.Identity;
using ERP.Domain.Identity;
using ERP.Persistence.Database;
using FirebirdSql.Data.FirebirdClient;

namespace ERP.Persistence.Identity;

public sealed class FirebirdUserRepository : IUserRepository
{
    private const string SelectColumns = "ID, USERNAME, DISPLAY_NAME, PASSWORD_HASH, ROLE, STATUS";

    private readonly FirebirdUnitOfWork _unitOfWork;

    public FirebirdUserRepository(FirebirdUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM APP_USER WHERE ID = @ID");
        command.Parameters.Add("@ID", FbDbType.Char).Value = id.ToString();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {SelectColumns} FROM APP_USER WHERE USERNAME = @USERNAME");
        command.Parameters.Add("@USERNAME", FbDbType.VarChar).Value = (username ?? string.Empty).Trim().ToLowerInvariant();
        return await ReadSingleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AnyExistsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("SELECT FIRST 1 1 FROM APP_USER");
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task SaveAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var existing = await GetByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await using var insert = CreateCommand(
                """
                INSERT INTO APP_USER (ID, USERNAME, DISPLAY_NAME, PASSWORD_HASH, ROLE, STATUS)
                VALUES (@ID, @USERNAME, @DISPLAY_NAME, @PASSWORD_HASH, @ROLE, @STATUS)
                """);
            AddCommonParameters(insert, user);
            insert.Parameters.Add("@ID", FbDbType.Char).Value = user.Id.ToString();
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var update = CreateCommand(
            """
            UPDATE APP_USER SET
                USERNAME = @USERNAME,
                DISPLAY_NAME = @DISPLAY_NAME,
                PASSWORD_HASH = @PASSWORD_HASH,
                ROLE = @ROLE,
                STATUS = @STATUS
            WHERE ID = @ID
            """);
        AddCommonParameters(update, user);
        update.Parameters.Add("@ID", FbDbType.Char).Value = user.Id.ToString();
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddCommonParameters(FbCommand command, User user)
    {
        command.Parameters.Add("@USERNAME", FbDbType.VarChar).Value = user.Username;
        command.Parameters.Add("@DISPLAY_NAME", FbDbType.VarChar).Value = user.DisplayName;
        command.Parameters.Add("@PASSWORD_HASH", FbDbType.VarChar).Value = user.PasswordHash.Encoded;
        command.Parameters.Add("@ROLE", FbDbType.SmallInt).Value = (short)user.Role;
        command.Parameters.Add("@STATUS", FbDbType.SmallInt).Value = (short)user.Status;
    }

    private static async Task<User?> ReadSingleAsync(FbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return User.Rehydrate(
            UserId.From(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            reader.GetString(2),
            PasswordHash.FromEncoded(reader.GetString(3)),
            (UserRole)reader.GetInt16(4),
            (UserStatus)reader.GetInt16(5));
    }

    private FbCommand CreateCommand(string commandText)
    {
        return new FbCommand(commandText, _unitOfWork.Connection, _unitOfWork.Transaction)
        {
            CommandType = CommandType.Text,
        };
    }
}
