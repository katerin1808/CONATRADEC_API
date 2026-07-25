using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Verifica si SQL Server tiene disponible y habilitado el índice Full-Text
    /// de publicaciones. El resultado se conserva para evitar una consulta de
    /// metadatos en cada búsqueda.
    /// </summary>
    public sealed class BusquedaTextoCompletoNoticiasService
    {
        private static readonly SemaphoreSlim Bloqueo = new(1, 1);
        private static bool? disponible;

        private readonly NoticiasDbContext db;

        public BusquedaTextoCompletoNoticiasService(
            NoticiasDbContext db)
        {
            this.db = db;
        }

        public async Task<bool> EstaDisponibleAsync(
            CancellationToken cancellationToken = default)
        {
            if (disponible.HasValue)
                return disponible.Value;

            await Bloqueo.WaitAsync(cancellationToken);

            try
            {
                if (disponible.HasValue)
                    return disponible.Value;

                var connection = db.Database.GetDbConnection();
                bool cerrarConexion =
                    connection.State != ConnectionState.Open;

                if (cerrarConexion)
                    await connection.OpenAsync(cancellationToken);

                try
                {
                    await using var command =
                        connection.CreateCommand();

                    command.CommandText = """
                        SELECT CASE
                            WHEN FULLTEXTSERVICEPROPERTY(
                                'IsFullTextInstalled') = 1
                             AND EXISTS
                             (
                                 SELECT 1
                                 FROM sys.fulltext_indexes
                                 WHERE object_id =
                                     OBJECT_ID(N'[dbo].[publicacion]')
                                   AND is_enabled = 1
                             )
                            THEN 1
                            ELSE 0
                        END;
                        """;

                    object? resultado =
                        await command.ExecuteScalarAsync(
                            cancellationToken);

                    disponible =
                        resultado != null &&
                        resultado != DBNull.Value &&
                        Convert.ToInt32(resultado) == 1;
                }
                finally
                {
                    if (cerrarConexion)
                        await connection.CloseAsync();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                /*
                 * La búsqueda del módulo no debe dejar de funcionar si el
                 * hosting no posee Full-Text o la cuenta no puede consultarlo.
                 */
                disponible = false;
            }
            finally
            {
                Bloqueo.Release();
            }

            return disponible ?? false;
        }
    }
}
