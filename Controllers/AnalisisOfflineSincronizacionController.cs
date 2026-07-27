using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Recibe los análisis calculados localmente.
    ///
    /// La operaciónLocalId funciona como llave idempotente. Si el dispositivo
    /// reintenta por una respuesta perdida, el servidor devuelve el resultado
    /// anterior sin crear un análisis duplicado.
    /// </summary>
    [ApiController]
    [Route("api/analisis-offline")]
    public sealed class AnalisisOfflineSincronizacionController :
        ControllerBase
    {
        private readonly DBContext db;

        private static readonly SemaphoreSlim tablaLock =
            new(1, 1);

        private static bool tablaVerificada;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        public AnalisisOfflineSincronizacionController(DBContext db)
        {
            this.db = db;
        }

        [HttpPost("sincronizar")]
        public async Task<IActionResult> Sincronizar(
            [FromBody] AnalisisOfflineSincronizarDto dto,
            CancellationToken cancellationToken = default)
        {
            (IActionResult? Error, int UsuarioId) acceso =
                await ValidarAccesoAsync(
                    dto,
                    cancellationToken);

            if (acceso.Error != null)
                return acceso.Error;

            if (dto.operacionLocalId == Guid.Empty)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La operación local no contiene un identificador válido."
                });
            }

            if (dto.solicitud == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibió la solicitud completa del análisis."
                });
            }

            if (string.IsNullOrWhiteSpace(
                    dto.versionMotor) ||
                string.IsNullOrWhiteSpace(
                    dto.hashPaquete))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El análisis no contiene la versión y el hash del motor utilizado."
                });
            }

            await AsegurarTablaAsync(cancellationToken);

            OperacionServidor? anterior =
                await ObtenerOperacionAsync(
                    dto.operacionLocalId,
                    cancellationToken);

            if (anterior?.Estado == "COMPLETADO" &&
                !string.IsNullOrWhiteSpace(
                    anterior.RespuestaJson))
            {
                return Content(
                    anterior.RespuestaJson,
                    "application/json");
            }

            if (anterior?.Estado == "PROCESANDO")
            {
                bool operacionVencida =
                    anterior.FechaRecepcionUtc.HasValue &&
                    anterior.FechaRecepcionUtc.Value <
                        DateTime.UtcNow.AddMinutes(-5);

                if (!operacionVencida)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La operación ya se está procesando. Se volverá a consultar automáticamente."
                    });
                }

                /*
                 * El servidor pudo completar GuardarTodo y reiniciarse antes de
                 * marcar la operación. Se busca el identificador único antes de
                 * liberar la reserva para impedir un duplicado.
                 */
                if (!string.Equals(
                        dto.tipoOperacion,
                        "EDITAR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    string identificador =
                        dto.solicitud
                            .datosAnalisis
                            .identificadorAnalisisSuelo?
                            .Trim() ??
                        string.Empty;

                    var existente =
                        await (
                            from analisis
                            in db.AnalisisSuelos.AsNoTracking()
                            join calculo
                            in db.AnalisisSueloCalculos.AsNoTracking()
                            on analisis.analisisSueloId
                            equals calculo.analisisSueloId
                            where
                                analisis.activo &&
                                calculo.activo &&
                                analisis.identificadorAnalisisSuelo ==
                                    identificador
                            orderby
                                calculo.analisisSueloCalculoId
                                descending
                            select new
                            {
                                analisis.analisisSueloId,
                                calculo.analisisSueloCalculoId
                            }
                        )
                        .FirstOrDefaultAsync(
                            cancellationToken);

                    if (existente != null)
                    {
                        string respuestaRecuperada =
                            JsonSerializer.Serialize(
                                new
                                {
                                    success = true,
                                    message =
                                        "La operación ya había sido aplicada y fue recuperada correctamente.",
                                    data = new
                                    {
                                        existente.analisisSueloId,
                                        existente
                                            .analisisSueloCalculoId,
                                        formulaNutricionalId =
                                            (int?)null,
                                        enmiendaCalcareaId =
                                            (int?)null,
                                        fertilizacionMixtaId =
                                            (int?)null
                                    }
                                },
                                JsonOptions);

                        await CompletarOperacionAsync(
                            dto.operacionLocalId,
                            respuestaRecuperada,
                            cancellationToken);

                        return Content(
                            respuestaRecuperada,
                            "application/json");
                    }

                }

                await EliminarReservaAsync(
                    dto.operacionLocalId,
                    cancellationToken);
            }

            bool reservada =
                await ReservarOperacionAsync(
                    dto,
                    cancellationToken);

            if (!reservada)
            {
                anterior =
                    await ObtenerOperacionAsync(
                        dto.operacionLocalId,
                        cancellationToken);

                if (anterior?.Estado == "COMPLETADO" &&
                    !string.IsNullOrWhiteSpace(
                        anterior.RespuestaJson))
                {
                    return Content(
                        anterior.RespuestaJson,
                        "application/json");
                }

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible reservar la operación para sincronizarla."
                });
            }

            try
            {
                var guardarController =
                    new GuardarTodoController(db);

                IActionResult resultado;

                if (string.Equals(
                        dto.tipoOperacion,
                        "EDITAR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (dto.analisisSueloCalculoId is not > 0)
                    {
                        await EliminarReservaAsync(
                            dto.operacionLocalId,
                            cancellationToken);

                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "No se recibió el identificador del análisis que se debe editar."
                        });
                    }

                    resultado =
                        await guardarController.Editar(
                            dto.analisisSueloCalculoId.Value,
                            dto.solicitud);
                }
                else
                {
                    resultado =
                        await guardarController.GuardarTodo(
                            dto.solicitud);
                }

                int statusCode =
                    ObtenerStatusCode(resultado);

                if (statusCode < 200 ||
                    statusCode >= 300)
                {
                    /*
                     * Un error funcional no se memoriza como completado. La
                     * operación queda disponible para corregirse o reintentarse.
                     */
                    await EliminarReservaAsync(
                        dto.operacionLocalId,
                        cancellationToken);

                    return resultado;
                }

                object? value =
                    (resultado as ObjectResult)?.Value;

                string respuestaJson =
                    JsonSerializer.Serialize(
                        value ??
                        new
                        {
                            success = true,
                            message =
                                "El análisis fue sincronizado correctamente."
                        },
                        JsonOptions);

                await CompletarOperacionAsync(
                    dto.operacionLocalId,
                    respuestaJson,
                    cancellationToken);

                return resultado;
            }
            catch (Exception)
            {
                await EliminarReservaAsync(
                    dto.operacionLocalId,
                    cancellationToken);

                throw;
            }
        }

        private async Task<(IActionResult? Error, int UsuarioId)>
            ValidarAccesoAsync(
                AnalisisOfflineSincronizarDto dto,
                CancellationToken cancellationToken)
        {
            string usuarioIdTexto =
                Request.Headers["X-Usuario-Id"]
                    .ToString();

            if (!int.TryParse(
                    usuarioIdTexto,
                    out int usuarioId) ||
                usuarioId <= 0)
            {
                return (
                    Unauthorized(new
                    {
                        success = false,
                        message =
                            "No se recibió una sesión válida."
                    }),
                    0);
            }

            var usuario =
                await db.Usuarios
                    .AsNoTracking()
                    .Where(item =>
                        item.UsuarioId ==
                            usuarioId &&
                        item.activo)
                    .Select(item => new
                    {
                        item.UsuarioId,
                        item.rolId
                    })
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (usuario == null)
            {
                return (
                    Unauthorized(new
                    {
                        success = false,
                        message =
                            "La sesión no pertenece a un usuario activo."
                    }),
                    0);
            }

            bool permitido =
                await (
                    from relacion
                    in db.RolInterfaz.AsNoTracking()
                    join interfaz
                    in db.Interfaz.AsNoTracking()
                    on relacion.interfazId
                    equals interfaz.interfazId
                    where
                        relacion.rolId ==
                            usuario.rolId &&
                        interfaz.activo &&
                        interfaz.nombreInterfaz ==
                            "datosSinConexionPage" &&
                        relacion.leer == true
                    select relacion.rolInterfazId
                )
                .AnyAsync(cancellationToken);

            if (!permitido)
            {
                return (
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Su usuario no tiene habilitada la sincronización de análisis sin conexión."
                        }),
                    0);
            }

            int usuarioSolicitud =
                dto.solicitud?
                    .datosAnalisis?
                    .usuarioId ??
                0;

            if (usuarioSolicitud != usuarioId)
            {
                return (
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "El análisis no pertenece al usuario de la sesión."
                        }),
                    0);
            }

            return (null, usuarioId);
        }

        private async Task AsegurarTablaAsync(
            CancellationToken cancellationToken)
        {
            if (tablaVerificada)
                return;

            await tablaLock.WaitAsync(
                cancellationToken);

            try
            {
                if (tablaVerificada)
                    return;

                const string sql = """
                    IF OBJECT_ID(N'dbo.analisisOfflineOperacion', N'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.analisisOfflineOperacion
                        (
                            operacionLocalId UNIQUEIDENTIFIER NOT NULL
                                CONSTRAINT PK_analisisOfflineOperacion PRIMARY KEY,
                            tipoOperacion NVARCHAR(20) NOT NULL,
                            analisisSueloCalculoId INT NULL,
                            usuarioId INT NULL,
                            identificadorAnalisis NVARCHAR(50) NULL,
                            versionMotor NVARCHAR(100) NULL,
                            hashPaquete NVARCHAR(128) NULL,
                            fechaCalculoLocalUtc DATETIME2 NULL,
                            fechaRecepcionUtc DATETIME2 NOT NULL,
                            fechaCompletadoUtc DATETIME2 NULL,
                            estado NVARCHAR(20) NOT NULL,
                            respuestaJson NVARCHAR(MAX) NULL
                        );
                    END
                    """;

                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);

                tablaVerificada = true;
            }
            finally
            {
                tablaLock.Release();
            }
        }

        private async Task<bool> ReservarOperacionAsync(
            AnalisisOfflineSincronizarDto dto,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    INSERT INTO dbo.analisisOfflineOperacion
                    (
                        operacionLocalId,
                        tipoOperacion,
                        analisisSueloCalculoId,
                        usuarioId,
                        identificadorAnalisis,
                        versionMotor,
                        hashPaquete,
                        fechaCalculoLocalUtc,
                        fechaRecepcionUtc,
                        estado
                    )
                    VALUES
                    (
                        @operacionLocalId,
                        @tipoOperacion,
                        @analisisSueloCalculoId,
                        @usuarioId,
                        @identificadorAnalisis,
                        @versionMotor,
                        @hashPaquete,
                        @fechaCalculoLocalUtc,
                        SYSUTCDATETIME(),
                        N'PROCESANDO'
                    );
                    """;

                AgregarParametro(
                    command,
                    "@operacionLocalId",
                    dto.operacionLocalId);

                AgregarParametro(
                    command,
                    "@tipoOperacion",
                    NormalizarTipo(dto.tipoOperacion));

                AgregarParametro(
                    command,
                    "@analisisSueloCalculoId",
                    dto.analisisSueloCalculoId);

                AgregarParametro(
                    command,
                    "@usuarioId",
                    dto.solicitud.datosAnalisis?.usuarioId);

                AgregarParametro(
                    command,
                    "@identificadorAnalisis",
                    dto.solicitud
                        .datosAnalisis?
                        .identificadorAnalisisSuelo);

                AgregarParametro(
                    command,
                    "@versionMotor",
                    dto.versionMotor);

                AgregarParametro(
                    command,
                    "@hashPaquete",
                    dto.hashPaquete);

                AgregarParametro(
                    command,
                    "@fechaCalculoLocalUtc",
                    dto.fechaCalculoLocalUtc ==
                        default
                            ? null
                            : dto.fechaCalculoLocalUtc);

                try
                {
                    return await command.ExecuteNonQueryAsync(
                        cancellationToken) > 0;
                }
                catch (DbException)
                {
                    return false;
                }
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private async Task<OperacionServidor?>
            ObtenerOperacionAsync(
                Guid operacionLocalId,
                CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    SELECT
                        estado,
                        respuestaJson,
                        fechaRecepcionUtc
                    FROM dbo.analisisOfflineOperacion
                    WHERE operacionLocalId = @operacionLocalId;
                    """;

                AgregarParametro(
                    command,
                    "@operacionLocalId",
                    operacionLocalId);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                return new OperacionServidor
                {
                    Estado =
                        reader.IsDBNull(0)
                            ? string.Empty
                            : reader.GetString(0),

                    RespuestaJson =
                        reader.IsDBNull(1)
                            ? string.Empty
                            : reader.GetString(1),

                    FechaRecepcionUtc =
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetDateTime(2)
                };
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private async Task CompletarOperacionAsync(
            Guid operacionLocalId,
            string respuestaJson,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    UPDATE dbo.analisisOfflineOperacion
                    SET
                        estado = N'COMPLETADO',
                        respuestaJson = @respuestaJson,
                        fechaCompletadoUtc = SYSUTCDATETIME()
                    WHERE operacionLocalId = @operacionLocalId;
                    """;

                AgregarParametro(
                    command,
                    "@respuestaJson",
                    respuestaJson);

                AgregarParametro(
                    command,
                    "@operacionLocalId",
                    operacionLocalId);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private async Task EliminarReservaAsync(
            Guid operacionLocalId,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = """
                    DELETE FROM dbo.analisisOfflineOperacion
                    WHERE operacionLocalId = @operacionLocalId
                      AND estado = N'PROCESANDO';
                    """;

                AgregarParametro(
                    command,
                    "@operacionLocalId",
                    operacionLocalId);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            finally
            {
                if (cerrar)
                    await connection.CloseAsync();
            }
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor)
        {
            DbParameter parameter =
                command.CreateParameter();

            parameter.ParameterName = nombre;
            parameter.Value = valor ?? DBNull.Value;

            command.Parameters.Add(parameter);
        }

        private static int ObtenerStatusCode(
            IActionResult result) =>
            result switch
            {
                ObjectResult objectResult =>
                    objectResult.StatusCode ??
                    StatusCodes.Status200OK,

                StatusCodeResult statusResult =>
                    statusResult.StatusCode,

                _ =>
                    StatusCodes.Status200OK
            };

        private static string NormalizarTipo(
            string? tipo) =>
            string.Equals(
                tipo,
                "EDITAR",
                StringComparison.OrdinalIgnoreCase)
                ? "EDITAR"
                : "CREAR";

        private sealed class OperacionServidor
        {
            public string Estado { get; set; } =
                string.Empty;

            public string RespuestaJson { get; set; } =
                string.Empty;

            public DateTime? FechaRecepcionUtc { get; set; }
        }
    }
}
