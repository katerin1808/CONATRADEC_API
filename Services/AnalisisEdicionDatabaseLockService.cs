using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Services;

/// <summary>
/// Utiliza sp_getapplock para serializar la edición de un cálculo incluso
/// cuando la API está publicada en más de una instancia.
/// </summary>
public sealed class AnalisisEdicionDatabaseLockService
{
    private readonly DBContext db;

    public AnalisisEdicionDatabaseLockService(DBContext db)
    {
        this.db = db;
    }

    public async ValueTask<IAsyncDisposable> AdquirirAsync(
        int analisisSueloCalculoId,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool cerrarAlFinal = connection.State != ConnectionState.Open;

        if (cerrarAlFinal)
            await connection.OpenAsync(cancellationToken);

        string recurso =
            $"CONATRADEC_ANALISIS_{analisisSueloCalculoId}";

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = """
DECLARE @resultado INT;
EXEC @resultado = sys.sp_getapplock
    @Resource = @recurso,
    @LockMode = N'Exclusive',
    @LockOwner = N'Session',
    @LockTimeout = 30000,
    @DbPrincipal = N'public';
SELECT @resultado;
""";

            AgregarParametro(command, "@recurso", recurso);

            object? value =
                await command.ExecuteScalarAsync(cancellationToken);

            int resultado = value == null || value == DBNull.Value
                ? -999
                : Convert.ToInt32(value);

            if (resultado < 0)
            {
                throw new TimeoutException(
                    "Otro proceso está actualizando el análisis. " +
                    "Intente nuevamente cuando termine la operación actual.");
            }

            return new Releaser(
                connection,
                recurso,
                cerrarAlFinal);
        }
        catch
        {
            if (cerrarAlFinal &&
                connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }

            throw;
        }
    }

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object valor)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = nombre;
        parameter.Value = valor;
        command.Parameters.Add(parameter);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly DbConnection connection;
        private readonly string recurso;
        private readonly bool cerrarAlFinal;
        private int liberado;

        public Releaser(
            DbConnection connection,
            string recurso,
            bool cerrarAlFinal)
        {
            this.connection = connection;
            this.recurso = recurso;
            this.cerrarAlFinal = cerrarAlFinal;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref liberado, 1) != 0)
                return;

            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using DbCommand command =
                        connection.CreateCommand();

                    command.CommandText = """
DECLARE @resultado INT;
EXEC @resultado = sys.sp_releaseapplock
    @Resource = @recurso,
    @LockOwner = N'Session',
    @DbPrincipal = N'public';
SELECT @resultado;
""";

                    AgregarParametro(
                        command,
                        "@recurso",
                        recurso);

                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (cerrarAlFinal &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
