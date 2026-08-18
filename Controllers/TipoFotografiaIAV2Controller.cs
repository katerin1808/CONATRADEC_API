using CONATRADEC_API.DTOs;
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
    /// Versión auditada del catálogo de tipos de fotografía.
    /// Las rutas históricas permanecen intactas. Esta versión separa activos
    /// de eliminados y añade RowVersion para edición/desactivación/recuperación.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion/tipos-fotografia-ia/v2")]
    public sealed class TipoFotografiaIAV2Controller : ControllerBase
    {
        private const string InterfazConfiguracion =
            "diagnosticoIAConfiguracionPage";

        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializado;

        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<TipoFotografiaIAV2Controller> logger;

        public TipoFotografiaIAV2Controller(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            ILogger<TipoFotografiaIAV2Controller> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> ListarActivos(
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Tipos de fotografía activos obtenidos correctamente.",
                data = await ListarAsync(
                    activo: true,
                    buscar,
                    cancellationToken)
            });
        }

        [HttpGet("eliminados")]
        public async Task<IActionResult> ListarEliminados(
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Tipos de fotografía eliminados obtenidos correctamente.",
                data = await ListarAsync(
                    activo: false,
                    buscar,
                    cancellationToken)
            });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);

            TipoFotografiaIAV2Respuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            return item == null
                ? NotFound(Error("El tipo de fotografía indicado no existe."))
                : Ok(new
                {
                    success = true,
                    message = "Tipo de fotografía obtenido correctamente.",
                    data = item
                });
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] TipoFotografiaIAV2CrearRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error(
                    "No se recibieron los datos del tipo de fotografía."));

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
                TipoFotografiaIAV2Respuesta? mismoCodigo =
                    await ObtenerPorCodigoAsync(
                        datos.Codigo,
                        transaccion,
                        cancellationToken);

                if (mismoCodigo?.Activo == true)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Ya existe un tipo de fotografía activo con ese código."));
                }

                if (await ExisteNombreActivoAsync(
                        datos.Nombre,
                        mismoCodigo?.TipoFotografiaIAId,
                        transaccion,
                        cancellationToken))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Ya existe un tipo de fotografía activo con ese nombre."));
                }

                int usuarioId = ObtenerUsuarioIdRequerido();
                int id;
                bool reactivado = mismoCodigo != null;

                if (mismoCodigo != null)
                {
                    id = mismoCodigo.TipoFotografiaIAId;
                    await ActualizarRegistroSinVersionAsync(
                        id,
                        datos,
                        usuarioId,
                        activar: true,
                        transaccion,
                        cancellationToken);
                }
                else
                {
                    id = await InsertarAsync(
                        datos,
                        usuarioId,
                        transaccion,
                        cancellationToken);
                }

                TipoFotografiaIAV2Respuesta? guardado =
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
                        message = reactivado
                            ? "Tipo de fotografía reactivado correctamente."
                            : "Tipo de fotografía creado correctamente.",
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
                logger.LogError(ex, "Error al crear el tipo de fotografía IA v2.");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al crear el tipo de fotografía."));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] TipoFotografiaIAV2ActualizarRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error(
                    "No se recibieron los datos del tipo de fotografía."));

            if (!IntentarDecodificarRowVersion(
                    request.RowVersion,
                    out byte[] rowVersion))
            {
                return BadRequest(Error(
                    "La versión del registro no es válida. Abra nuevamente el registro e intente guardar otra vez."));
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
                TipoFotografiaIAV2Respuesta? actual =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                if (actual == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error(
                        "El tipo de fotografía indicado no existe."));
                }

                if (!actual.Activo)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El tipo de fotografía fue desactivado. Recupérelo antes de editarlo."));
                }

                if (!string.Equals(
                        actual.Codigo,
                        datos.Codigo,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El código no puede cambiarse porque ya puede estar asociado a fotografías históricas."));
                }

                if (await ExisteNombreActivoAsync(
                        datos.Nombre,
                        id,
                        transaccion,
                        cancellationToken))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Ya existe otro tipo de fotografía activo con ese nombre."));
                }

                int afectados = await ActualizarRegistroConVersionAsync(
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
                        "El tipo de fotografía fue modificado por otro usuario. Se cargarán los datos más recientes."));
                }

                TipoFotografiaIAV2Respuesta? actualizado =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return Ok(new
                {
                    success = true,
                    message = "Tipo de fotografía actualizado correctamente.",
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
                    "Error al actualizar el tipo de fotografía IA v2 {Id}.",
                    id);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al actualizar el tipo de fotografía."));
            }
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(
            int id,
            [FromBody] TipoFotografiaIAV2EstadoRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
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

            TipoFotografiaIAV2Respuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            if (item == null || !item.Activo)
            {
                return NotFound(Error(
                    "El tipo de fotografía no existe o ya está inactivo."));
            }

            if (string.Equals(
                    item.Codigo,
                    "EVIDENCIA",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(Error(
                    "EVIDENCIA es el tipo predeterminado y no puede desactivarse."));
            }

            int afectados = await CambiarEstadoConVersionAsync(
                id,
                activo: false,
                ObtenerUsuarioIdRequerido(),
                rowVersion,
                cancellationToken);

            if (afectados == 0)
            {
                return Conflict(Error(
                    "El registro fue modificado por otro usuario. Actualice el listado e intente nuevamente."));
            }

            return Ok(new
            {
                success = true,
                message = "Tipo de fotografía desactivado correctamente."
            });
        }

        [HttpPut("{id:int}/recuperar")]
        public async Task<IActionResult> Recuperar(
            int id,
            [FromBody] TipoFotografiaIAV2EstadoRequest? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
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
                TipoFotografiaIAV2Respuesta? item =
                    await ObtenerPorIdAsync(
                        id,
                        transaccion,
                        cancellationToken);

                if (item == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error(
                        "El tipo de fotografía indicado no existe."));
                }

                if (item.Activo)
                {
                    await transaccion.CommitAsync(cancellationToken);
                    transaccionConfirmada = true;
                    return Ok(new
                    {
                        success = true,
                        message = "El tipo ya se encuentra activo."
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
                        "No se puede recuperar porque ya existe otro tipo activo con el mismo nombre."));
                }

                int afectados = await CambiarEstadoConVersionAsync(
                    id,
                    activo: true,
                    ObtenerUsuarioIdRequerido(),
                    rowVersion,
                    transaccion,
                    cancellationToken);

                if (afectados == 0)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "El registro fue modificado por otro usuario. Actualice Eliminados e intente nuevamente."));
                }

                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                return Ok(new
                {
                    success = true,
                    message = "Tipo de fotografía recuperado correctamente."
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
                    "Error al recuperar el tipo de fotografía IA v2 {Id}.",
                    id);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error("Ocurrió un error inesperado al recuperar el tipo de fotografía."));
            }
        }

        private async Task InicializarAsync(CancellationToken cancellationToken)
        {
            if (inicializado)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);

            try
            {
                if (inicializado)
                    return;

                const string sql = """
IF OBJECT_ID(N'[dbo].[tipoFotografiaIA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[tipoFotografiaIA]
    (
        [TipoFotografiaIAId] INT IDENTITY(1,1) NOT NULL,
        [Codigo] NVARCHAR(40) NOT NULL,
        [Nombre] NVARCHAR(100) NOT NULL,
        [Descripcion] NVARCHAR(500) NOT NULL CONSTRAINT [DF_tipoFotoIA_descripcion] DEFAULT(N''),
        [InstruccionIA] NVARCHAR(2000) NOT NULL,
        [Orden] INT NOT NULL CONSTRAINT [DF_tipoFotoIA_orden] DEFAULT(1),
        [Activo] BIT NOT NULL CONSTRAINT [DF_tipoFotoIA_activo] DEFAULT(1),
        [FechaCreacionUtc] DATETIME2(0) NOT NULL,
        [UsuarioCreacionId] INT NULL,
        [FechaModificacionUtc] DATETIME2(0) NOT NULL,
        [UsuarioModificacionId] INT NULL,
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [PK_tipoFotografiaIA] PRIMARY KEY ([TipoFotografiaIAId]),
        CONSTRAINT [UX_tipoFotografiaIA_codigo] UNIQUE ([Codigo]),
        CONSTRAINT [CK_tipoFotografiaIA_orden] CHECK ([Orden] BETWEEN 1 AND 999)
    );
END;

IF COL_LENGTH(N'dbo.tipoFotografiaIA', N'RowVersion') IS NULL
BEGIN
    ALTER TABLE [dbo].[tipoFotografiaIA]
        ADD [RowVersion] ROWVERSION NOT NULL;
END;

MERGE [dbo].[tipoFotografiaIA] AS destino
USING
(
    SELECT N'EVIDENCIA', N'Evidencia general', N'Fotografía general o evidencia que no corresponde claramente a una sola parte de la planta.', N'Describe primero el contenido visible y revisa de forma general síntomas, plagas, enfermedades, daños mecánicos y condiciones anormales en cualquier parte del cafeto.', 1
    UNION ALL SELECT N'HOJA', N'Hoja', N'Fotografía enfocada principalmente en hojas de café.', N'Prioriza el haz y el envés de la hoja. Observa manchas, clorosis, necrosis, perforaciones, galerías, pústulas, micelio, esporas, insectos, huevos, deformaciones, bordes y patrón de distribución de los síntomas.', 2
    UNION ALL SELECT N'FRUTO', N'Fruto', N'Fotografía enfocada en cerezas o frutos del café.', N'Prioriza coloración y madurez, lesiones, perforaciones, pudrición, momificación, caída, deformaciones, presencia de broca u otros insectos y distribución del daño entre frutos.', 3
    UNION ALL SELECT N'TALLO', N'Tallo', N'Fotografía enfocada en el tallo principal o tallos secundarios.', N'Prioriza lesiones, cancros, grietas, perforaciones, descortezamiento, exudados, pudrición, cambios de color, galerías y presencia de insectos en el tallo.', 4
    UNION ALL SELECT N'RAMA', N'Rama', N'Fotografía enfocada en una rama y sus estructuras asociadas.', N'Prioriza lesiones de la rama, defoliación, marchitez, muerte regresiva, nudos, distribución de hojas y frutos afectados, perforaciones y presencia de insectos.', 5
    UNION ALL SELECT N'PLANTA_COMPLETA', N'Planta completa', N'Fotografía donde se observa gran parte o la totalidad del cafeto.', N'Evalúa vigor general, arquitectura, distribución de síntomas, marchitez, defoliación, coloración, crecimiento desigual, daños generalizados y diferencias entre sectores de la planta.', 6
    UNION ALL SELECT N'RAIZ', N'Raíz', N'Fotografía enfocada en raíces o cuello de la planta.', N'Prioriza pudrición, necrosis, deformaciones, agallas, pérdida de raíces finas, lesiones del cuello, plagas del suelo y signos de estrés radicular.', 7
    UNION ALL SELECT N'OTRA', N'Otra evidencia', N'Evidencia útil que no corresponde a los tipos anteriores.', N'Describe primero qué estructura o evidencia aparece y adapta el análisis a lo visible, prestando atención a la observación de campo del técnico.', 8
) AS origen([Codigo], [Nombre], [Descripcion], [InstruccionIA], [Orden])
ON destino.[Codigo] = origen.[Codigo]
WHEN NOT MATCHED THEN
    INSERT
    (
        [Codigo], [Nombre], [Descripcion], [InstruccionIA], [Orden],
        [Activo], [FechaCreacionUtc], [FechaModificacionUtc]
    )
    VALUES
    (
        origen.[Codigo], origen.[Nombre], origen.[Descripcion],
        origen.[InstruccionIA], origen.[Orden], 1,
        SYSUTCDATETIME(), SYSUTCDATETIME()
    );
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

        private async Task<List<TipoFotografiaIAV2Respuesta>> ListarAsync(
            bool activo,
            string? buscar,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;

            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = """
SELECT
    [TipoFotografiaIAId], [Codigo], [Nombre], [Descripcion], [InstruccionIA],
    [Orden], [Activo], [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[tipoFotografiaIA]
WHERE [Activo] = @activo
  AND
  (
      @buscar = N'' OR
      [Codigo] LIKE N'%' + @buscar + N'%' OR
      [Nombre] LIKE N'%' + @buscar + N'%' OR
      [Descripcion] LIKE N'%' + @buscar + N'%' OR
      [InstruccionIA] LIKE N'%' + @buscar + N'%'
  )
ORDER BY [Orden], [Nombre], [TipoFotografiaIAId];
""";

                AgregarParametro(comando, "@activo", activo);
                AgregarParametro(comando, "@buscar", (buscar ?? string.Empty).Trim());

                return await LeerListaAsync(comando, cancellationToken);
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<TipoFotografiaIAV2Respuesta?> ObtenerPorIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;

            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = SqlSeleccion +
                    " WHERE [TipoFotografiaIAId] = @id;";
                AgregarParametro(comando, "@id", id);
                return await LeerUnoAsync(comando, cancellationToken);
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<TipoFotografiaIAV2Respuesta?> ObtenerPorIdAsync(
            int id,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = SqlSeleccion +
                " WHERE [TipoFotografiaIAId] = @id;";
            AgregarParametro(comando, "@id", id);
            return await LeerUnoAsync(comando, cancellationToken);
        }

        private async Task<TipoFotografiaIAV2Respuesta?> ObtenerPorCodigoAsync(
            string codigo,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = SqlSeleccion +
                " WHERE [Codigo] = @codigo;";
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
FROM [dbo].[tipoFotografiaIA]
WHERE [Activo] = 1
  AND UPPER(LTRIM(RTRIM([Nombre]))) = UPPER(LTRIM(RTRIM(@nombre)))
  AND (@excluirId IS NULL OR [TipoFotografiaIAId] <> @excluirId);
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
INSERT INTO [dbo].[tipoFotografiaIA]
(
    [Codigo], [Nombre], [Descripcion], [InstruccionIA], [Orden], [Activo],
    [FechaCreacionUtc], [UsuarioCreacionId],
    [FechaModificacionUtc], [UsuarioModificacionId]
)
VALUES
(
    @codigo, @nombre, @descripcion, @instruccion, @orden, 1,
    SYSUTCDATETIME(), @usuarioId,
    SYSUTCDATETIME(), @usuarioId
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";
            AgregarDatos(comando, datos, usuarioId);
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(resultado);
        }

        private async Task ActualizarRegistroSinVersionAsync(
            int id,
            DatosNormalizados datos,
            int usuarioId,
            bool activar,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
UPDATE [dbo].[tipoFotografiaIA]
SET [Nombre] = @nombre,
    [Descripcion] = @descripcion,
    [InstruccionIA] = @instruccion,
    [Orden] = @orden,
    [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id;
""";
            AgregarDatos(comando, datos, usuarioId);
            AgregarParametro(comando, "@activo", activar);
            AgregarParametro(comando, "@id", id);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<int> ActualizarRegistroConVersionAsync(
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
UPDATE [dbo].[tipoFotografiaIA]
SET [Nombre] = @nombre,
    [Descripcion] = @descripcion,
    [InstruccionIA] = @instruccion,
    [Orden] = @orden,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id
  AND [Activo] = 1
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
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;

            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = """
UPDATE [dbo].[tipoFotografiaIA]
SET [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id
  AND [RowVersion] = @rowVersion;
""";
                AgregarParametro(comando, "@activo", activo);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                AgregarParametro(comando, "@id", id);
                AgregarParametro(comando, "@rowVersion", rowVersion);
                return await comando.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<int> CambiarEstadoConVersionAsync(
            int id,
            bool activo,
            int usuarioId,
            byte[] rowVersion,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
UPDATE [dbo].[tipoFotografiaIA]
SET [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id
  AND [RowVersion] = @rowVersion;
""";
            AgregarParametro(comando, "@activo", activo);
            AgregarParametro(comando, "@usuarioId", usuarioId);
            AgregarParametro(comando, "@id", id);
            AgregarParametro(comando, "@rowVersion", rowVersion);
            return await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private static readonly string SqlSeleccion = """
SELECT
    [TipoFotografiaIAId], [Codigo], [Nombre], [Descripcion], [InstruccionIA],
    [Orden], [Activo], [FechaCreacionUtc], [FechaModificacionUtc], [RowVersion]
FROM [dbo].[tipoFotografiaIA]
""";

        private static async Task<List<TipoFotografiaIAV2Respuesta>> LeerListaAsync(
            DbCommand comando,
            CancellationToken cancellationToken)
        {
            var items = new List<TipoFotografiaIAV2Respuesta>();

            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                items.Add(LeerItem(reader));

            return items;
        }

        private static async Task<TipoFotografiaIAV2Respuesta?> LeerUnoAsync(
            DbCommand comando,
            CancellationToken cancellationToken)
        {
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken)
                ? LeerItem(reader)
                : null;
        }

        private static TipoFotografiaIAV2Respuesta LeerItem(DbDataReader reader)
        {
            byte[] rowVersion = (byte[])reader["RowVersion"];

            return new TipoFotografiaIAV2Respuesta
            {
                TipoFotografiaIAId = Convert.ToInt32(reader["TipoFotografiaIAId"]),
                Codigo = Convert.ToString(reader["Codigo"]) ?? string.Empty,
                Nombre = Convert.ToString(reader["Nombre"]) ?? string.Empty,
                Descripcion = Convert.ToString(reader["Descripcion"]) ?? string.Empty,
                InstruccionIA = Convert.ToString(reader["InstruccionIA"]) ?? string.Empty,
                Orden = Convert.ToInt32(reader["Orden"]),
                Activo = Convert.ToBoolean(reader["Activo"]),
                FechaCreacionUtc = Convert.ToDateTime(reader["FechaCreacionUtc"]),
                FechaModificacionUtc = Convert.ToDateTime(reader["FechaModificacionUtc"]),
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
            AgregarParametro(comando, "@instruccion", datos.InstruccionIA);
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
            TipoFotografiaIAV2CrearRequest request) =>
            new(
                NormalizarCodigo(request.Codigo),
                (request.Nombre ?? string.Empty).Trim(),
                (request.Descripcion ?? string.Empty).Trim(),
                (request.InstruccionIA ?? string.Empty).Trim(),
                request.Orden);

        private static DatosNormalizados Normalizar(
            TipoFotografiaIAV2ActualizarRequest request) =>
            new(
                NormalizarCodigo(request.Codigo),
                (request.Nombre ?? string.Empty).Trim(),
                (request.Descripcion ?? string.Empty).Trim(),
                (request.InstruccionIA ?? string.Empty).Trim(),
                request.Orden);

        private IActionResult? Validar(DatosNormalizados datos)
        {
            if (datos.Codigo.Length is < 2 or > 40 ||
                !Regex.IsMatch(datos.Codigo, "^[A-Z0-9_]+$"))
            {
                return BadRequest(Error(
                    "El código debe tener entre 2 y 40 caracteres y solo puede contener letras, números y guion bajo."));
            }

            if (datos.Nombre.Length is < 2 or > 100)
            {
                return BadRequest(Error(
                    "El nombre debe tener entre 2 y 100 caracteres."));
            }

            if (datos.Descripcion.Length > 500)
            {
                return BadRequest(Error(
                    "La descripción no puede superar 500 caracteres."));
            }

            if (datos.InstruccionIA.Length is < 20 or > 2000)
            {
                return BadRequest(Error(
                    "La instrucción para la IA debe tener entre 20 y 2000 caracteres."));
            }

            if (datos.Orden is < 1 or > 999)
            {
                return BadRequest(Error(
                    "El orden debe estar entre 1 y 999."));
            }

            return null;
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                ObtenerUsuarioId(),
                InterfazConfiguracion,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(resultado.CodigoEstado, Error(resultado.Mensaje));
        }

        private int ObtenerUsuarioIdRequerido() =>
            ObtenerUsuarioId() ??
            throw new InvalidOperationException(
                "No se encontró el usuario autenticado.");

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
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
                return false;
            }
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };

        private sealed record DatosNormalizados(
            string Codigo,
            string Nombre,
            string Descripcion,
            string InstruccionIA,
            int Orden);
    }
}
