using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Mantiene el nombre, el cierre de la etapa técnica y el cierre definitivo
    /// de cada inspección. La inicialización es idempotente y no requiere
    /// ejecutar scripts manuales.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlDatabaseInitializer
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaControlDatabaseInitializer(
            DiagnosticoIADbContext db)
        {
            this.db = db;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            if (inicializada)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (inicializada)
                    return;

                const string sql = """
IF COL_LENGTH(N'dbo.diagnosticoIA', N'NombreInspeccion') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [NombreInspeccion] NVARCHAR(150) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_nombreInspeccion] DEFAULT(N'');

IF COL_LENGTH(N'dbo.diagnosticoIA', N'CerradaTecnico') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [CerradaTecnico] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_cerradaTecnico] DEFAULT(0);

IF COL_LENGTH(N'dbo.diagnosticoIA', N'FechaCierreTecnicoUtc') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [FechaCierreTecnicoUtc] DATETIME2(0) NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'UsuarioCierreTecnicoId') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [UsuarioCierreTecnicoId] INT NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'EtapaTecnicaFinalizada') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [EtapaTecnicaFinalizada] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_etapaTecnicaFinalizada] DEFAULT(0);

IF COL_LENGTH(N'dbo.diagnosticoIA', N'FechaFinEtapaTecnicaUtc') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [FechaFinEtapaTecnicaUtc] DATETIME2(0) NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'UsuarioFinEtapaTecnicaId') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [UsuarioFinEtapaTecnicaId] INT NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'CerradaDefinitiva') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [CerradaDefinitiva] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_cerradaDefinitiva] DEFAULT(0);

IF COL_LENGTH(N'dbo.diagnosticoIA', N'FechaCierreDefinitivoUtc') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [FechaCierreDefinitivoUtc] DATETIME2(0) NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'UsuarioCierreDefinitivoId') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [UsuarioCierreDefinitivoId] INT NULL;

EXEC(N'
UPDATE dbo.diagnosticoIA
SET NombreInspeccion = N''Inspección #'' +
    CONVERT(NVARCHAR(20), DiagnosticoIAId)
WHERE LEN(LTRIM(RTRIM(NombreInspeccion))) = 0;');

/*
 * Compatibilidad con cierres creados por la versión anterior: en esa versión
 * CerradaTecnico representaba un cierre global. Solo se migran como cierre
 * definitivo los expedientes que ya tenían un estado final.
 */
EXEC(N'
UPDATE dbo.diagnosticoIA
SET EtapaTecnicaFinalizada = 1,
    FechaFinEtapaTecnicaUtc =
        COALESCE(FechaFinEtapaTecnicaUtc, FechaCierreTecnicoUtc),
    UsuarioFinEtapaTecnicaId =
        COALESCE(UsuarioFinEtapaTecnicaId, UsuarioCierreTecnicoId),
    CerradaDefinitiva = 1,
    FechaCierreDefinitivoUtc =
        COALESCE(FechaCierreDefinitivoUtc, FechaCierreTecnicoUtc),
    UsuarioCierreDefinitivoId =
        COALESCE(UsuarioCierreDefinitivoId, UsuarioCierreTecnicoId)
WHERE CerradaTecnico = 1
  AND ISNULL(CerradaDefinitiva, 0) = 0
  AND UPPER(ISNULL(Estado, N'''')) IN
      (N''FINALIZADA'', N''FINALIZADA_PARCIALMENTE'');');

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_diagnosticoIA_cierre_bandeja'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIA]')
)
BEGIN
    EXEC(N'CREATE INDEX [IX_diagnosticoIA_cierre_bandeja]
        ON [dbo].[diagnosticoIA]
           ([CerradaDefinitiva], [EtapaTecnicaFinalizada],
            [CerradaTecnico], [FechaSolicitudUtc] DESC,
            [DiagnosticoIAId] DESC)
        INCLUDE ([NombreInspeccion], [UsuarioSolicitanteId], [Activo]);');
END;
""";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                inicializada = true;
            }
            catch
            {
                inicializada = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        public async Task<InspeccionFitosanitariaControlRegistro?> ObtenerAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            Dictionary<int, InspeccionFitosanitariaControlRegistro> registros =
                await ObtenerPorInspeccionesAsync(
                    [inspeccionId],
                    cancellationToken);

            return registros.GetValueOrDefault(inspeccionId);
        }

        /// <summary>
        /// Obtiene el control de varias inspecciones en una sola consulta. Se
        /// utiliza en las bandejas para evitar consultas N+1.
        /// </summary>
        public async Task<Dictionary<int, InspeccionFitosanitariaControlRegistro>>
            ObtenerPorInspeccionesAsync(
                IEnumerable<int> inspeccionIds,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            int[] ids = (inspeccionIds ?? [])
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            var resultado =
                new Dictionary<int, InspeccionFitosanitariaControlRegistro>();

            if (ids.Length == 0)
                return resultado;

            string nombresParametros = string.Join(
                ",",
                ids.Select((_, indice) => $"@id{indice}"));

            string sql = $"""
SELECT
    DiagnosticoIAId,
    UsuarioSolicitanteId,
    NombreInspeccion,
    CerradaTecnico,
    FechaCierreTecnicoUtc,
    UsuarioCierreTecnicoId,
    EtapaTecnicaFinalizada,
    FechaFinEtapaTecnicaUtc,
    UsuarioFinEtapaTecnicaId,
    CerradaDefinitiva,
    FechaCierreDefinitivoUtc,
    UsuarioCierreDefinitivoId,
    Activo
FROM dbo.diagnosticoIA
WHERE DiagnosticoIAId IN ({nombresParametros});
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                for (int indice = 0; indice < ids.Length; indice++)
                {
                    AgregarParametro(
                        comando,
                        $"@id{indice}",
                        ids[indice]);
                }

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var registro = new InspeccionFitosanitariaControlRegistro
                    {
                        InspeccionId = reader.GetInt32(0),
                        UsuarioSolicitanteId = reader.GetInt32(1),
                        NombreInspeccion = reader.IsDBNull(2)
                            ? string.Empty
                            : reader.GetString(2),
                        CerradaTecnico = reader.GetBoolean(3),
                        FechaCierreTecnicoUtc = reader.IsDBNull(4)
                            ? null
                            : DateTime.SpecifyKind(
                                reader.GetDateTime(4),
                                DateTimeKind.Utc),
                        UsuarioCierreTecnicoId = reader.IsDBNull(5)
                            ? null
                            : reader.GetInt32(5),
                        EtapaTecnicaFinalizada = reader.GetBoolean(6),
                        FechaFinEtapaTecnicaUtc = reader.IsDBNull(7)
                            ? null
                            : DateTime.SpecifyKind(
                                reader.GetDateTime(7),
                                DateTimeKind.Utc),
                        UsuarioFinEtapaTecnicaId = reader.IsDBNull(8)
                            ? null
                            : reader.GetInt32(8),
                        CerradaDefinitiva = reader.GetBoolean(9),
                        FechaCierreDefinitivoUtc = reader.IsDBNull(10)
                            ? null
                            : DateTime.SpecifyKind(
                                reader.GetDateTime(10),
                                DateTimeKind.Utc),
                        UsuarioCierreDefinitivoId = reader.IsDBNull(11)
                            ? null
                            : reader.GetInt32(11),
                        Activo = reader.GetBoolean(12)
                    };

                    resultado[registro.InspeccionId] = registro;
                }

                return resultado;
            }, cancellationToken);
        }

        public async Task ActualizarNombreAsync(
            int inspeccionId,
            string? nombre,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string valor = (nombre ?? string.Empty).Trim();
            if (valor.Length == 0)
                valor = $"Inspección #{inspeccionId}";
            if (valor.Length > 150)
                valor = valor[..150];

            const string sql = """
UPDATE dbo.diagnosticoIA
SET NombreInspeccion = @nombre
WHERE DiagnosticoIAId = @id
  AND Activo = 1
  AND CerradaTecnico = 0
  AND EtapaTecnicaFinalizada = 0
  AND CerradaDefinitiva = 0;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@nombre", valor);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return 0;
            }, cancellationToken);
        }

        /// <summary>
        /// Resume si todas las fotografías fueron enviadas a revisión o
        /// descartadas. Esta condición habilita el cierre de la etapa técnica.
        /// </summary>
        public async Task<InspeccionFitosanitariaEstadoEtapaTecnica>
            ObtenerEstadoEtapaTecnicaAsync(
                int inspeccionId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    COUNT_BIG(1) AS TotalActivas,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) IN
        (
            N'PENDIENTE_ANALIZADOR',
            N'EN_ANALISIS_HUMANO',
            N'DEVUELTO_PARA_CORRECCION',
            N'DEVUELTA_AL_ANALIZADOR',
            N'PENDIENTE_APROBACION',
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'PUBLICADA_ALBUM'
        ) THEN 1 ELSE 0 END) AS TotalEnviadasRevision,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) = N'DESCARTADA'
        THEN 1 ELSE 0 END) AS TotalDescartadas,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) IN
            (N'PENDIENTE_IA', N'ANALIZANDO_IA')
        THEN 1 ELSE 0 END) AS TotalProcesando,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) NOT IN
        (
            N'PENDIENTE_ANALIZADOR',
            N'EN_ANALISIS_HUMANO',
            N'DEVUELTO_PARA_CORRECCION',
            N'DEVUELTA_AL_ANALIZADOR',
            N'PENDIENTE_APROBACION',
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'DESCARTADA',
            N'PUBLICADA_ALBUM'
        ) THEN 1 ELSE 0 END) AS TotalNoPreparadas
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAId = @id
  AND ISNULL(Activo, 1) = 1;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new InspeccionFitosanitariaEstadoEtapaTecnica(
                        0, 0, 0, 0, 0);
                }

                return new InspeccionFitosanitariaEstadoEtapaTecnica(
                    Convert.ToInt32(reader.GetInt64(0)),
                    reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
            }, cancellationToken);
        }

        /// <summary>
        /// Resume la condición del cierre definitivo sin cargar imágenes ni
        /// historiales. Cuenta únicamente fotografías activas.
        /// </summary>
        public async Task<InspeccionFitosanitariaEstadoCierre>
            ObtenerEstadoCierreAsync(
                int inspeccionId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    COUNT_BIG(1) AS TotalActivas,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) IN
        (
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'DESCARTADA',
            N'PUBLICADA_ALBUM'
        ) THEN 1 ELSE 0 END) AS TotalFinalizadas,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) IN
            (N'PENDIENTE_IA', N'ANALIZANDO_IA')
        THEN 1 ELSE 0 END) AS TotalProcesando,
    SUM(CASE
        WHEN UPPER(ISNULL(Estado, N'BORRADOR')) NOT IN
        (
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'DESCARTADA',
            N'PUBLICADA_ALBUM'
        ) THEN 1 ELSE 0 END) AS TotalPendientes
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAId = @id
  AND ISNULL(Activo, 1) = 1;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new InspeccionFitosanitariaEstadoCierre(
                        0, 0, 0, 0);
                }

                return new InspeccionFitosanitariaEstadoCierre(
                    Convert.ToInt32(reader.GetInt64(0)),
                    reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
            }, cancellationToken);
        }

        public async Task<Dictionary<int, string>> ObtenerEstadosFotografiasAsync(
            int inspeccionId,
            IEnumerable<int> fotografiaIds,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            int[] ids = (fotografiaIds ?? [])
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            var resultado = new Dictionary<int, string>();
            if (ids.Length == 0)
                return resultado;

            string parametros = string.Join(
                ",",
                ids.Select((_, indice) => $"@foto{indice}"));

            string sql = $"""
SELECT DiagnosticoIAImagenId, UPPER(ISNULL(Estado, N'BORRADOR'))
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAId = @id
  AND ISNULL(Activo, 1) = 1
  AND DiagnosticoIAImagenId IN ({parametros});
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                for (int indice = 0; indice < ids.Length; indice++)
                    AgregarParametro(comando, $"@foto{indice}", ids[indice]);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    resultado[reader.GetInt32(0)] = reader.IsDBNull(1)
                        ? "BORRADOR"
                        : reader.GetString(1);
                }

                return resultado;
            }, cancellationToken);
        }

        public async Task<bool> TieneProcesamientoActivoAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            InspeccionFitosanitariaEstadoEtapaTecnica estado =
                await ObtenerEstadoEtapaTecnicaAsync(
                    inspeccionId,
                    cancellationToken);

            return estado.TotalProcesando > 0;
        }

        public async Task<bool> CerrarEtapaTecnicaAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE dbo.diagnosticoIA
SET EtapaTecnicaFinalizada = 1,
    FechaFinEtapaTecnicaUtc = SYSUTCDATETIME(),
    UsuarioFinEtapaTecnicaId = @usuarioId,
    Estado = N'PENDIENTE_ANALIZADOR'
WHERE DiagnosticoIAId = @id
  AND UsuarioSolicitanteId = @usuarioId
  AND Activo = 1
  AND CerradaTecnico = 0
  AND EtapaTecnicaFinalizada = 0
  AND CerradaDefinitiva = 0
  AND EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagen i
      WHERE i.DiagnosticoIAId = dbo.diagnosticoIA.DiagnosticoIAId
        AND ISNULL(i.Activo, 1) = 1
        AND UPPER(ISNULL(i.Estado, N'BORRADOR')) <> N'DESCARTADA'
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagen i
      WHERE i.DiagnosticoIAId = dbo.diagnosticoIA.DiagnosticoIAId
        AND ISNULL(i.Activo, 1) = 1
        AND UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
        (
            N'PENDIENTE_ANALIZADOR',
            N'EN_ANALISIS_HUMANO',
            N'DEVUELTO_PARA_CORRECCION',
            N'DEVUELTA_AL_ANALIZADOR',
            N'PENDIENTE_APROBACION',
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'DESCARTADA',
            N'PUBLICADA_ALBUM'
        )
  );
SELECT @@ROWCOUNT;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                object? valor =
                    await comando.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(valor ?? 0) == 1;
            }, cancellationToken);
        }

        public async Task<bool> CerrarDefinitivamenteAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE dbo.diagnosticoIA
SET CerradaTecnico = 1,
    FechaCierreTecnicoUtc = SYSUTCDATETIME(),
    UsuarioCierreTecnicoId = @usuarioId,
    CerradaDefinitiva = 1,
    FechaCierreDefinitivoUtc = SYSUTCDATETIME(),
    UsuarioCierreDefinitivoId = @usuarioId,
    Estado = CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.diagnosticoIAImagen i
            WHERE i.DiagnosticoIAId =
                  dbo.diagnosticoIA.DiagnosticoIAId
              AND ISNULL(i.Activo, 1) = 1
              AND UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
                  (N'RECHAZADA', N'NO_CONCLUYENTE')
        ) THEN N'FINALIZADA_PARCIALMENTE'
        ELSE N'FINALIZADA'
    END
WHERE DiagnosticoIAId = @id
  AND Activo = 1
  AND EtapaTecnicaFinalizada = 1
  AND CerradaTecnico = 0
  AND CerradaDefinitiva = 0
  AND EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagen i
      WHERE i.DiagnosticoIAId = dbo.diagnosticoIA.DiagnosticoIAId
        AND ISNULL(i.Activo, 1) = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagen i
      WHERE i.DiagnosticoIAId = dbo.diagnosticoIA.DiagnosticoIAId
        AND ISNULL(i.Activo, 1) = 1
        AND UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
        (
            N'APROBADA',
            N'APROBADA_CON_CORRECCION',
            N'RECHAZADA',
            N'NO_CONCLUYENTE',
            N'DESCARTADA',
            N'PUBLICADA_ALBUM'
        )
  );
SELECT @@ROWCOUNT;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                object? valor =
                    await comando.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(valor ?? 0) == 1;
            }, cancellationToken);
        }

        private async Task<T> EjecutarAsync<T>(
            Func<DbConnection, Task<T>> accion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                return await accion(conexion);
            }
            finally
            {
                if (cerrar && db.Database.CurrentTransaction == null)
                    await conexion.CloseAsync();
            }
        }

        private DbCommand CrearComando(
            DbConnection conexion,
            string sql)
        {
            DbCommand comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 180;

            if (db.Database.CurrentTransaction is not null)
            {
                comando.Transaction =
                    db.Database.CurrentTransaction.GetDbTransaction();
            }

            return comando;
        }

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor)
        {
            DbParameter parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }
    }

    public sealed class InspeccionFitosanitariaControlRegistro
    {
        public int InspeccionId { get; set; }
        public int UsuarioSolicitanteId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public bool CerradaTecnico { get; set; }
        public DateTime? FechaCierreTecnicoUtc { get; set; }
        public int? UsuarioCierreTecnicoId { get; set; }
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public int? UsuarioFinEtapaTecnicaId { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public DateTime? FechaCierreDefinitivoUtc { get; set; }
        public int? UsuarioCierreDefinitivoId { get; set; }
        public bool Activo { get; set; }
    }

    public sealed record InspeccionFitosanitariaEstadoEtapaTecnica(
        int TotalActivas,
        int TotalEnviadasRevision,
        int TotalDescartadas,
        int TotalProcesando,
        int TotalNoPreparadas)
    {
        public bool ListaParaCerrar =>
            TotalActivas > 0 &&
            TotalEnviadasRevision > 0 &&
            TotalProcesando == 0 &&
            TotalNoPreparadas == 0;
    }

    public sealed record InspeccionFitosanitariaEstadoCierre(
        int TotalActivas,
        int TotalFinalizadas,
        int TotalProcesando,
        int TotalPendientes)
    {
        public bool TodasFinalizadas =>
            TotalActivas > 0 &&
            TotalFinalizadas == TotalActivas &&
            TotalPendientes == 0;
    }
}
