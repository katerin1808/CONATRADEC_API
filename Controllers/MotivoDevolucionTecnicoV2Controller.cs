using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Versión auditada del catálogo de motivos de devolución al técnico.
    /// Las rutas históricas permanecen intactas. Esta versión separa activos de
    /// eliminados, consulta por ID y usa RowVersion en las mutaciones.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion/motivos-devolucion-tecnico/v2")]
    public sealed partial class MotivoDevolucionTecnicoV2Controller : ControllerBase
    {
        private const string InterfazConfiguracion =
            "diagnosticoIAConfiguracionPage";

        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializado;

        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDevolucionDatabase database;
        private readonly ILogger<MotivoDevolucionTecnicoV2Controller> logger;

        public MotivoDevolucionTecnicoV2Controller(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            ILogger<MotivoDevolucionTecnicoV2Controller> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
            database = new InspeccionFitosanitariaDevolucionDatabase(db);
        }

        /// <summary>
        /// Administración: devuelve únicamente motivos activos.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ListarActivos(
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivos activos obtenidos correctamente.",
                data = await ListarAsync(
                    activo: true,
                    buscar: buscar,
                    cancellationToken: cancellationToken)
            });
        }

        /// <summary>
        /// Administración: devuelve únicamente registros inactivos.
        /// </summary>
        [HttpGet("eliminados")]
        public async Task<IActionResult> ListarEliminados(
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivos eliminados obtenidos correctamente.",
                data = await ListarAsync(
                    activo: false,
                    buscar: buscar,
                    cancellationToken: cancellationToken)
            });
        }

        /// <summary>
        /// Selector operativo del analizador. Se mantiene separado del permiso
        /// administrativo y siempre consulta motivos activos del servidor.
        /// </summary>
        [HttpGet("selector-activos")]
        public async Task<IActionResult> ListarSelectorActivos(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Motivos disponibles obtenidos correctamente.",
                data = await ListarAsync(
                    activo: true,
                    buscar: null,
                    cancellationToken: cancellationToken)
            });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            MotivoDevolucionTecnicoV2Respuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            return item == null
                ? NotFound(Error("El motivo indicado no existe."))
                : Ok(new
                {
                    success = true,
                    message = "Motivo obtenido correctamente.",
                    data = item
                });
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] MotivoDevolucionTecnicoV2CrearRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del motivo."));

            await InicializarAsync(cancellationToken);

            DatosNormalizados datos = Normalizar(request);
            IActionResult? validacion = Validar(datos);
            if (validacion != null)
                return validacion;

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            bool transaccionConfirmada = false;

            try
            {
                MotivoDevolucionTecnicoV2Respuesta? mismoCodigo =
                    await ObtenerPorCodigoAsync(
                        datos.Codigo,
                        transaccion,
                        cancellationToken);

                if (mismoCodigo != null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        mismoCodigo.Activo
                            ? "Ya existe un motivo activo con ese código."
                            : "Ese código pertenece a un motivo eliminado. Recupérelo desde Eliminados para conservar su identificador histórico."));
                }

                if (await ExisteNombreActivoAsync(
                        nombre: datos.Nombre,
                        excluirId: null,
                        transaccion: transaccion,
                        cancellationToken: cancellationToken))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Ya existe un motivo activo con ese nombre."));
                }

                int id = await InsertarAsync(
                    datos,
                    ObtenerUsuarioIdRequerido(),
                    transaccion,
                    cancellationToken);

                MotivoDevolucionTecnicoV2Respuesta? guardado =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Motivo creado correctamente.",
                        data = guardado
                    });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error al crear el motivo de devolución v2.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al crear el motivo."));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] MotivoDevolucionTecnicoV2ActualizarRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del motivo."));

            if (!IntentarDecodificarRowVersion(
                    request.RowVersion,
                    out byte[] rowVersion))
            {
                return BadRequest(Error(
                    "La versión del registro no es válida. Abra nuevamente el motivo e intente guardar otra vez."));
            }

            await InicializarAsync(cancellationToken);

            DatosNormalizados datos = Normalizar(request);
            IActionResult? validacion = Validar(datos);
            if (validacion != null)
                return validacion;

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            bool transaccionConfirmada = false;

            try
            {
                MotivoDevolucionTecnicoV2Respuesta? actual =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                if (actual == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error("El motivo indicado no existe."));
                }

                if (!actual.Activo)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El motivo fue desactivado. Recupérelo antes de editarlo."));
                }

                if (!string.Equals(
                        actual.Codigo,
                        datos.Codigo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El código no puede modificarse porque puede estar asociado a devoluciones históricas."));
                }

                if (await ExisteNombreActivoAsync(
                        datos.Nombre,
                        id,
                        transaccion,
                        cancellationToken))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Ya existe otro motivo activo con ese nombre."));
                }

                int afectados = await ActualizarConVersionAsync(
                    id,
                    datos,
                    ObtenerUsuarioIdRequerido(),
                    rowVersion,
                    transaccion,
                    cancellationToken);

                if (afectados == 0)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El motivo fue modificado por otro usuario. Se cargarán los datos más recientes."));
                }

                MotivoDevolucionTecnicoV2Respuesta? actualizado =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return Ok(new
                {
                    success = true,
                    message = "Motivo actualizado correctamente.",
                    data = actualizado
                });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error al actualizar el motivo de devolución v2 {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al actualizar el motivo."));
            }
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(
            int id,
            [FromBody] MotivoDevolucionTecnicoV2EstadoRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null ||
                !IntentarDecodificarRowVersion(
                    request.RowVersion,
                    out byte[] rowVersion))
            {
                return BadRequest(Error(
                    "La versión del registro no es válida. Actualice el listado e intente nuevamente."));
            }

            await InicializarAsync(cancellationToken);

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            bool transaccionConfirmada = false;

            try
            {
                MotivoDevolucionTecnicoV2Respuesta? item =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                if (item == null || !item.Activo)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error(
                        "El motivo no existe o ya está inactivo."));
                }

                int afectados = await CambiarEstadoConVersionAsync(
                    id: id,
                    activo: false,
                    usuarioId: ObtenerUsuarioIdRequerido(),
                    rowVersion: rowVersion,
                    estadoActual: true,
                    transaccion: transaccion,
                    cancellationToken: cancellationToken);

                if (afectados == 0)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El motivo fue modificado por otro usuario. Actualice el listado e intente nuevamente."));
                }

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return Ok(new
                {
                    success = true,
                    message = "Motivo desactivado correctamente."
                });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error al desactivar el motivo de devolución v2 {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al desactivar el motivo."));
            }
        }

        [HttpPut("{id:int}/recuperar")]
        public async Task<IActionResult> Recuperar(
            int id,
            [FromBody] MotivoDevolucionTecnicoV2EstadoRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazConfiguracion,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null ||
                !IntentarDecodificarRowVersion(
                    request.RowVersion,
                    out byte[] rowVersion))
            {
                return BadRequest(Error(
                    "La versión del registro no es válida. Actualice Eliminados e intente nuevamente."));
            }

            await InicializarAsync(cancellationToken);

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            bool transaccionConfirmada = false;

            try
            {
                MotivoDevolucionTecnicoV2Respuesta? item =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                if (item == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error("El motivo indicado no existe."));
                }

                if (item.Activo)
                {
                    await transaccion.CommitAsync(cancellationToken);
                    transaccionConfirmada = true;
                    return Ok(new
                    {
                        success = true,
                        message = "El motivo ya se encuentra activo."
                    });
                }

                if (await ExisteNombreActivoAsync(
                        item.Nombre,
                        id,
                        transaccion,
                        cancellationToken))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "No se puede recuperar porque ya existe otro motivo activo con el mismo nombre."));
                }

                int afectados = await CambiarEstadoConVersionAsync(
                    id: id,
                    activo: true,
                    usuarioId: ObtenerUsuarioIdRequerido(),
                    rowVersion: rowVersion,
                    estadoActual: false,
                    transaccion: transaccion,
                    cancellationToken: cancellationToken);

                if (afectados == 0)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El motivo fue modificado por otro usuario. Actualice Eliminados e intente nuevamente."));
                }

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return Ok(new
                {
                    success = true,
                    message = "Motivo recuperado correctamente."
                });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error al recuperar el motivo de devolución v2 {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al recuperar el motivo."));
            }
        }

        private async Task InicializarAsync(CancellationToken cancellationToken)
        {
            // Primero se conserva el inicializador histórico, que instala tabla,
            // índices y semillas. La versión v2 solo agrega RowVersion.
            await database.InicializarAsync(cancellationToken);

            if (inicializado)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);

            try
            {
                if (inicializado)
                    return;

                const string sql = """
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'RowVersion') IS NULL
BEGIN
    ALTER TABLE [dbo].[motivoDevolucionTecnico]
        ADD [RowVersion] ROWVERSION NOT NULL;
END;
""";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                inicializado = true;
            }
            catch
            {
                inicializado = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        private async Task<List<MotivoDevolucionTecnicoV2Respuesta>> ListarAsync(
            bool activo,
            string? buscar,
            CancellationToken cancellationToken)
        {
            string texto = NormalizarBusqueda(buscar);

            const string sql = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[motivoDevolucionTecnico]
WHERE [Activo] = @activo
  AND
  (
      @buscar = N'' OR
      [Codigo] LIKE N'%' + @buscar + N'%' OR
      [Nombre] LIKE N'%' + @buscar + N'%' OR
      [Descripcion] LIKE N'%' + @buscar + N'%' OR
      [InstruccionSugerida] LIKE N'%' + @buscar + N'%'
  )
ORDER BY [Orden], [Nombre], [MotivoDevolucionTecnicoId];
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@activo", activo);
                AgregarParametro(comando, "@buscar", texto);
                return await LeerListaAsync(comando, cancellationToken);
            }, cancellationToken);
        }

        private async Task<MotivoDevolucionTecnicoV2Respuesta?> ObtenerPorIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            const string sql = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[motivoDevolucionTecnico]
WHERE [MotivoDevolucionTecnicoId] = @id;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@id", id);
                return await LeerUnoAsync(comando, cancellationToken);
            }, cancellationToken);
        }

        private async Task<MotivoDevolucionTecnicoV2Respuesta?> ObtenerPorIdAsync(
            int id,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[motivoDevolucionTecnico] WITH (UPDLOCK, HOLDLOCK)
WHERE [MotivoDevolucionTecnicoId] = @id;
""";
            AgregarParametro(comando, "@id", id);
            return await LeerUnoAsync(comando, cancellationToken);
        }

        private async Task<MotivoDevolucionTecnicoV2Respuesta?> ObtenerPorCodigoAsync(
            string codigo,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[motivoDevolucionTecnico] WITH (UPDLOCK, HOLDLOCK)
WHERE [Codigo] = @codigo;
""";
            AgregarParametro(comando, "@codigo", codigo);
            return await LeerUnoAsync(comando, cancellationToken);
        }

        private async Task<bool> ExisteNombreActivoAsync(
            string nombre,
            int? excluirId,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
SELECT COUNT_BIG(1)
FROM [dbo].[motivoDevolucionTecnico] WITH (UPDLOCK, HOLDLOCK)
WHERE [Activo] = 1
  AND UPPER(LTRIM(RTRIM([Nombre]))) = UPPER(LTRIM(RTRIM(@nombre)))
  AND (@excluirId IS NULL OR [MotivoDevolucionTecnicoId] <> @excluirId);
""";
            AgregarParametro(comando, "@nombre", nombre);
            AgregarParametro(comando, "@excluirId", excluirId);
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(resultado ?? 0) > 0;
        }

        private async Task<int> InsertarAsync(
            DatosNormalizados datos,
            int usuarioId,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
INSERT INTO [dbo].[motivoDevolucionTecnico]
(
    [Codigo], [Nombre], [Descripcion], [InstruccionSugerida],
    [RequiereNuevaFotografia], [PermiteCorregirMetadatos],
    [Orden], [Activo], [FechaCreacionUtc], [UsuarioCreacionId],
    [FechaModificacionUtc], [UsuarioModificacionId]
)
VALUES
(
    @codigo, @nombre, @descripcion, @instruccion,
    @requiereNueva, @permiteMetadatos,
    @orden, 1, SYSUTCDATETIME(), @usuarioId,
    SYSUTCDATETIME(), @usuarioId
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";
            AgregarDatos(comando, datos, usuarioId);
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(resultado);
        }

        private async Task<int> ActualizarConVersionAsync(
            int id,
            DatosNormalizados datos,
            int usuarioId,
            byte[] rowVersion,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
UPDATE [dbo].[motivoDevolucionTecnico]
SET [Nombre] = @nombre,
    [Descripcion] = @descripcion,
    [InstruccionSugerida] = @instruccion,
    [RequiereNuevaFotografia] = @requiereNueva,
    [PermiteCorregirMetadatos] = @permiteMetadatos,
    [Orden] = @orden,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [MotivoDevolucionTecnicoId] = @id
  AND [Activo] = 1
  AND [Codigo] = @codigo
  AND [RowVersion] = @rowVersion;
""";
            AgregarDatos(comando, datos, usuarioId);
            AgregarParametro(comando, "@id", id);
            AgregarParametro(comando, "@rowVersion", rowVersion);
            return await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<int> CambiarEstadoConVersionAsync(
            int id,
            bool activo,
            int usuarioId,
            byte[] rowVersion,
            bool estadoActual,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
UPDATE [dbo].[motivoDevolucionTecnico]
SET [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [MotivoDevolucionTecnicoId] = @id
  AND [Activo] = @estadoActual
  AND [RowVersion] = @rowVersion;
""";
            AgregarParametro(comando, "@activo", activo);
            AgregarParametro(comando, "@usuarioId", usuarioId);
            AgregarParametro(comando, "@id", id);
            AgregarParametro(comando, "@estadoActual", estadoActual);
            AgregarParametro(comando, "@rowVersion", rowVersion);
            return await comando.ExecuteNonQueryAsync(cancellationToken);
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
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private static async Task<List<MotivoDevolucionTecnicoV2Respuesta>>
            LeerListaAsync(
                DbCommand comando,
                CancellationToken cancellationToken)
        {
            var items = new List<MotivoDevolucionTecnicoV2Respuesta>();

            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                items.Add(LeerItem(reader));

            return items;
        }

        private static async Task<MotivoDevolucionTecnicoV2Respuesta?> LeerUnoAsync(
            DbCommand comando,
            CancellationToken cancellationToken)
        {
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken)
                ? LeerItem(reader)
                : null;
        }

        private static MotivoDevolucionTecnicoV2Respuesta LeerItem(
            DbDataReader reader)
        {
            byte[] rowVersion = (byte[])reader["RowVersion"];

            return new MotivoDevolucionTecnicoV2Respuesta
            {
                MotivoDevolucionTecnicoId =
                    Convert.ToInt32(reader["MotivoDevolucionTecnicoId"]),
                Codigo = Convert.ToString(reader["Codigo"]) ?? string.Empty,
                Nombre = Convert.ToString(reader["Nombre"]) ?? string.Empty,
                Descripcion =
                    Convert.ToString(reader["Descripcion"]) ?? string.Empty,
                InstruccionSugerida =
                    Convert.ToString(reader["InstruccionSugerida"]) ?? string.Empty,
                RequiereNuevaFotografia =
                    Convert.ToBoolean(reader["RequiereNuevaFotografia"]),
                PermiteCorregirMetadatos =
                    Convert.ToBoolean(reader["PermiteCorregirMetadatos"]),
                Orden = Convert.ToInt32(reader["Orden"]),
                Activo = Convert.ToBoolean(reader["Activo"]),
                FechaCreacionUtc =
                    DateTime.SpecifyKind(
                        Convert.ToDateTime(reader["FechaCreacionUtc"]),
                        DateTimeKind.Utc),
                FechaModificacionUtc =
                    DateTime.SpecifyKind(
                        Convert.ToDateTime(reader["FechaModificacionUtc"]),
                        DateTimeKind.Utc),
                RowVersion = Convert.ToBase64String(rowVersion)
            };
        }

        private static void AgregarDatos(
            DbCommand comando,
            DatosNormalizados datos,
            int usuarioId)
        {
            AgregarParametro(comando, "@codigo", datos.Codigo);
            AgregarParametro(comando, "@nombre", datos.Nombre);
            AgregarParametro(comando, "@descripcion", datos.Descripcion);
            AgregarParametro(comando, "@instruccion", datos.InstruccionSugerida);
            AgregarParametro(comando, "@requiereNueva", datos.RequiereNuevaFotografia);
            AgregarParametro(comando, "@permiteMetadatos", datos.PermiteCorregirMetadatos);
            AgregarParametro(comando, "@orden", datos.Orden);
            AgregarParametro(comando, "@usuarioId", usuarioId);
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

        private static DatosNormalizados Normalizar(
            MotivoDevolucionTecnicoV2GuardarRequest request) =>
            new(
                NormalizarCodigo(request.Codigo),
                (request.Nombre ?? string.Empty).Trim(),
                (request.Descripcion ?? string.Empty).Trim(),
                (request.InstruccionSugerida ?? string.Empty).Trim(),
                request.RequiereNuevaFotografia,
                request.PermiteCorregirMetadatos,
                request.Orden);

        private IActionResult? Validar(DatosNormalizados datos)
        {
            if (!CodigoRegex().IsMatch(datos.Codigo))
            {
                return BadRequest(Error(
                    "El código debe contener entre 3 y 60 caracteres: letras mayúsculas, números o guion bajo."));
            }

            if (datos.Nombre.Length is < 3 or > 140)
                return BadRequest(Error(
                    "El nombre debe contener entre 3 y 140 caracteres."));

            if (datos.Descripcion.Length > 700)
                return BadRequest(Error(
                    "La descripción no puede superar 700 caracteres."));

            if (datos.InstruccionSugerida.Length is < 8 or > 2000)
            {
                return BadRequest(Error(
                    "La instrucción sugerida debe contener entre 8 y 2000 caracteres."));
            }

            if (datos.Orden is < 1 or > 999)
                return BadRequest(Error(
                    "El orden debe estar entre 1 y 999."));

            if (datos.RequiereNuevaFotografia ==
                datos.PermiteCorregirMetadatos)
            {
                return BadRequest(Error(
                    "Seleccione exactamente una forma de resolución: solicitar una nueva fotografía o permitir que el técnico corrija los metadatos de la evidencia actual."));
            }

            return null;
        }

        private static string NormalizarCodigo(string? valor)
        {
            string texto = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_');

            while (texto.Contains("__", StringComparison.Ordinal))
                texto = texto.Replace("__", "_", StringComparison.Ordinal);

            return texto;
        }

        private static string NormalizarBusqueda(string? valor) =>
            (valor ?? string.Empty).Trim();

        private static bool IntentarDecodificarRowVersion(
            string? valor,
            out byte[] rowVersion)
        {
            rowVersion = [];

            if (string.IsNullOrWhiteSpace(valor))
                return false;

            try
            {
                rowVersion = Convert.FromBase64String(valor.Trim());
                return rowVersion.Length == 8;
            }
            catch (FormatException)
            {
                rowVersion = [];
                return false;
            }
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                ObtenerUsuarioId(),
                interfaz,
                tipo,
                cancellationToken);

            return permiso.Permitido
                ? null
                : StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private int ObtenerUsuarioIdRequerido() =>
            ObtenerUsuarioId() ?? throw new UnauthorizedAccessException(
                "No se pudo identificar al usuario autenticado.");

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };

        [GeneratedRegex("^[A-Z0-9_]{3,60}$")]
        private static partial Regex CodigoRegex();

        private sealed record DatosNormalizados(
            string Codigo,
            string Nombre,
            string Descripcion,
            string InstruccionSugerida,
            bool RequiereNuevaFotografia,
            bool PermiteCorregirMetadatos,
            int Orden);
    }
}
