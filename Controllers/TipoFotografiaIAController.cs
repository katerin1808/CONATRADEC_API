using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Catálogo que indica a la IA qué parte o evidencia aparece en la foto y
    /// qué detalles debe priorizar. La tabla se instala de forma idempotente,
    /// sin scripts SQL manuales.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion/tipos-fotografia-ia")]
    public sealed partial class TipoFotografiaIAController : ControllerBase
    {
        private const string InterfazConfiguracion =
            "diagnosticoIAConfiguracionPage";

        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializado;

        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<TipoFotografiaIAController> logger;

        public TipoFotografiaIAController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            ILogger<TipoFotografiaIAController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        /// <summary>
        /// Selector utilizado por técnicos. Solo devuelve registros activos y
        /// no exige permiso administrativo del catálogo.
        /// </summary>
        [HttpGet("activos")]
        public async Task<ActionResult<List<TipoFotografiaIARespuesta>>>
            ListarActivos(CancellationToken cancellationToken)
        {
            await InicializarAsync(cancellationToken);
            return Ok(await ListarAsync(false, null, cancellationToken));
        }

        [HttpGet]
        public async Task<ActionResult<List<TipoFotografiaIARespuesta>>>
            Listar(
                [FromQuery] bool incluirInactivos = false,
                [FromQuery] string? buscar = null,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);
            return Ok(await ListarAsync(
                incluirInactivos,
                buscar,
                cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TipoFotografiaIARespuesta>>
            Obtener(int id, CancellationToken cancellationToken)
        {
            ActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);
            TipoFotografiaIARespuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            return item == null
                ? NotFound(new
                {
                    success = false,
                    message = "El tipo de fotografía indicado no existe."
                })
                : Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] TipoFotografiaIACrearRequest? request,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del tipo de fotografía."));

            await InicializarAsync(cancellationToken);

            DatosNormalizados datos = Normalizar(request);
            IActionResult? validacion = Validar(datos);
            if (validacion != null)
                return validacion;

            List<TipoFotografiaIARespuesta> existentes =
                await ListarAsync(true, null, cancellationToken);

            TipoFotografiaIARespuesta? mismoCodigo = existentes
                .FirstOrDefault(item => string.Equals(
                    item.Codigo,
                    datos.Codigo,
                    StringComparison.OrdinalIgnoreCase));

            if (mismoCodigo?.Activo == true)
                return Conflict(Error("Ya existe un tipo de fotografía activo con ese código."));

            if (ExisteNombreActivo(existentes, datos.Nombre, mismoCodigo?.TipoFotografiaIAId))
                return Conflict(Error("Ya existe un tipo de fotografía activo con ese nombre."));

            int usuarioId = ObtenerUsuarioIdRequerido();

            try
            {
                int id;

                if (mismoCodigo != null)
                {
                    await ActualizarRegistroAsync(
                        mismoCodigo.TipoFotografiaIAId,
                        datos,
                        usuarioId,
                        activar: true,
                        cancellationToken);
                    id = mismoCodigo.TipoFotografiaIAId;
                }
                else
                {
                    id = await InsertarAsync(datos, usuarioId, cancellationToken);
                }

                TipoFotografiaIARespuesta item =
                    (await ObtenerPorIdAsync(id, cancellationToken))!;

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = mismoCodigo == null
                            ? "Tipo de fotografía creado correctamente."
                            : "Tipo de fotografía reactivado correctamente.",
                        data = item
                    });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al crear el tipo de fotografía IA.");
                return StatusCode(500, Error(
                    "Ocurrió un error inesperado al crear el tipo de fotografía."));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] TipoFotografiaIAActualizarRequest? request,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
                return BadRequest(Error("No se recibieron los datos del tipo de fotografía."));

            await InicializarAsync(cancellationToken);
            TipoFotografiaIARespuesta? actual =
                await ObtenerPorIdAsync(id, cancellationToken);

            if (actual == null)
                return NotFound(Error("El tipo de fotografía indicado no existe."));

            if (!actual.Activo)
                return Conflict(Error("Recupere el tipo de fotografía antes de editarlo."));

            DatosNormalizados datos = Normalizar(request);
            IActionResult? validacion = Validar(datos);
            if (validacion != null)
                return validacion;

            if (!string.Equals(actual.Codigo, datos.Codigo, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(Error(
                    "El código no puede cambiarse porque ya puede estar asociado a fotografías históricas."));
            }

            List<TipoFotografiaIARespuesta> existentes =
                await ListarAsync(true, null, cancellationToken);

            if (ExisteNombreActivo(existentes, datos.Nombre, id))
                return Conflict(Error("Ya existe otro tipo de fotografía activo con ese nombre."));

            try
            {
                await ActualizarRegistroAsync(
                    id,
                    datos,
                    ObtenerUsuarioIdRequerido(),
                    activar: null,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Tipo de fotografía actualizado correctamente.",
                    data = await ObtenerPorIdAsync(id, cancellationToken)
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error al actualizar el tipo de fotografía IA {Id}.", id);
                return StatusCode(500, Error(
                    "Ocurrió un error inesperado al actualizar el tipo de fotografía."));
            }
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);
            TipoFotografiaIARespuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            if (item == null || !item.Activo)
                return NotFound(Error("El tipo de fotografía no existe o ya está inactivo."));

            if (string.Equals(item.Codigo, "EVIDENCIA", StringComparison.OrdinalIgnoreCase))
                return Conflict(Error("EVIDENCIA es el tipo predeterminado y no puede desactivarse."));

            await CambiarEstadoAsync(
                id,
                false,
                ObtenerUsuarioIdRequerido(),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Tipo de fotografía desactivado correctamente."
            });
        }

        [HttpPut("{id:int}/recuperar")]
        public async Task<IActionResult> Recuperar(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await InicializarAsync(cancellationToken);
            TipoFotografiaIARespuesta? item =
                await ObtenerPorIdAsync(id, cancellationToken);

            if (item == null)
                return NotFound(Error("El tipo de fotografía indicado no existe."));

            if (item.Activo)
                return Ok(new { success = true, message = "El tipo ya se encuentra activo." });

            await CambiarEstadoAsync(
                id,
                true,
                ObtenerUsuarioIdRequerido(),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Tipo de fotografía recuperado correctamente."
            });
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

                const string sqlTabla = """
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
        CONSTRAINT [PK_tipoFotografiaIA] PRIMARY KEY ([TipoFotografiaIAId]),
        CONSTRAINT [UX_tipoFotografiaIA_codigo] UNIQUE ([Codigo]),
        CONSTRAINT [CK_tipoFotografiaIA_orden] CHECK ([Orden] BETWEEN 1 AND 999)
    );
END;
""";

                await db.Database.ExecuteSqlRawAsync(sqlTabla, cancellationToken);

                const string sqlSemillas = """
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

                await db.Database.ExecuteSqlRawAsync(sqlSemillas, cancellationToken);
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

        private async Task<List<TipoFotografiaIARespuesta>> ListarAsync(
            bool incluirInactivos,
            string? buscar,
            CancellationToken cancellationToken)
        {
            const string sql = """
SELECT
    [TipoFotografiaIAId], [Codigo], [Nombre], [Descripcion], [InstruccionIA],
    [Orden], [Activo], [FechaCreacionUtc], [FechaModificacionUtc]
FROM [dbo].[tipoFotografiaIA]
WHERE (@incluirInactivos = 1 OR [Activo] = 1)
  AND
  (
      @buscar = N'' OR
      [Codigo] LIKE N'%' + @buscar + N'%' OR
      [Nombre] LIKE N'%' + @buscar + N'%' OR
      [Descripcion] LIKE N'%' + @buscar + N'%' OR
      [InstruccionIA] LIKE N'%' + @buscar + N'%'
  )
ORDER BY [Orden], [Nombre];
""";

            await using DbCommand command = CrearComando(sql);
            AgregarParametro(command, "@incluirInactivos", incluirInactivos);
            AgregarParametro(command, "@buscar", Limitar(buscar, 150));
            await AbrirAsync(command.Connection!, cancellationToken);

            var items = new List<TipoFotografiaIARespuesta>();
            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                items.Add(Leer(reader));

            return items;
        }

        private async Task<TipoFotografiaIARespuesta?> ObtenerPorIdAsync(
            int id,
            CancellationToken cancellationToken)
        {
            const string sql = """
SELECT TOP (1)
    [TipoFotografiaIAId], [Codigo], [Nombre], [Descripcion], [InstruccionIA],
    [Orden], [Activo], [FechaCreacionUtc], [FechaModificacionUtc]
FROM [dbo].[tipoFotografiaIA]
WHERE [TipoFotografiaIAId] = @id;
""";

            await using DbCommand command = CrearComando(sql);
            AgregarParametro(command, "@id", id);
            await AbrirAsync(command.Connection!, cancellationToken);
            await using DbDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken)
                ? Leer(reader)
                : null;
        }

        private async Task<int> InsertarAsync(
            DatosNormalizados datos,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            const string sql = """
INSERT INTO [dbo].[tipoFotografiaIA]
(
    [Codigo], [Nombre], [Descripcion], [InstruccionIA], [Orden], [Activo],
    [FechaCreacionUtc], [UsuarioCreacionId],
    [FechaModificacionUtc], [UsuarioModificacionId]
)
VALUES
(
    @codigo, @nombre, @descripcion, @instruccion, @orden, 1,
    SYSUTCDATETIME(), @usuarioId, SYSUTCDATETIME(), @usuarioId
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            await using DbCommand command = CrearComando(sql);
            AgregarParametro(command, "@codigo", datos.Codigo);
            AgregarParametro(command, "@nombre", datos.Nombre);
            AgregarParametro(command, "@descripcion", datos.Descripcion);
            AgregarParametro(command, "@instruccion", datos.InstruccionIA);
            AgregarParametro(command, "@orden", datos.Orden);
            AgregarParametro(command, "@usuarioId", usuarioId);
            await AbrirAsync(command.Connection!, cancellationToken);

            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken));
        }

        private async Task ActualizarRegistroAsync(
            int id,
            DatosNormalizados datos,
            int usuarioId,
            bool? activar,
            CancellationToken cancellationToken)
        {
            const string sql = """
UPDATE [dbo].[tipoFotografiaIA]
SET [Nombre] = @nombre,
    [Descripcion] = @descripcion,
    [InstruccionIA] = @instruccion,
    [Orden] = @orden,
    [Activo] = CASE WHEN @cambiarActivo = 1 THEN @activo ELSE [Activo] END,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id;
""";

            await using DbCommand command = CrearComando(sql);
            AgregarParametro(command, "@id", id);
            AgregarParametro(command, "@nombre", datos.Nombre);
            AgregarParametro(command, "@descripcion", datos.Descripcion);
            AgregarParametro(command, "@instruccion", datos.InstruccionIA);
            AgregarParametro(command, "@orden", datos.Orden);
            AgregarParametro(command, "@cambiarActivo", activar.HasValue);
            AgregarParametro(command, "@activo", activar ?? false);
            AgregarParametro(command, "@usuarioId", usuarioId);
            await AbrirAsync(command.Connection!, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task CambiarEstadoAsync(
            int id,
            bool activo,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            const string sql = """
UPDATE [dbo].[tipoFotografiaIA]
SET [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [TipoFotografiaIAId] = @id;
""";

            await using DbCommand command = CrearComando(sql);
            AgregarParametro(command, "@id", id);
            AgregarParametro(command, "@activo", activo);
            AgregarParametro(command, "@usuarioId", usuarioId);
            await AbrirAsync(command.Connection!, cancellationToken);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<ActionResult?> ValidarPermisoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                InterfazConfiguracion,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    new { success = false, message = resultado.Mensaje });
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

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private IActionResult? Validar(DatosNormalizados datos)
        {
            if (datos.Codigo.Length is < 2 or > 40 ||
                !CodigoRegex().IsMatch(datos.Codigo))
            {
                return BadRequest(Error(
                    "El código debe tener entre 2 y 40 caracteres y utilizar únicamente letras, números y guion bajo."));
            }

            if (datos.Nombre.Length is < 2 or > 100)
                return BadRequest(Error("El nombre debe tener entre 2 y 100 caracteres."));

            if (datos.Descripcion.Length > 500)
                return BadRequest(Error("La descripción no puede superar 500 caracteres."));

            if (datos.InstruccionIA.Length is < 20 or > 2000)
            {
                return BadRequest(Error(
                    "La instrucción para la IA debe tener entre 20 y 2000 caracteres."));
            }

            if (datos.Orden is < 1 or > 999)
                return BadRequest(Error("El orden debe estar entre 1 y 999."));

            return null;
        }

        private static bool ExisteNombreActivo(
            IEnumerable<TipoFotografiaIARespuesta> items,
            string nombre,
            int? excluirId) =>
            items.Any(item =>
                item.Activo &&
                item.TipoFotografiaIAId != excluirId &&
                string.Equals(item.Nombre, nombre, StringComparison.OrdinalIgnoreCase));

        private static DatosNormalizados Normalizar(
            TipoFotografiaIACrearRequest request) =>
            new(
                NormalizarCodigo(request.Codigo),
                NormalizarTexto(request.Nombre, 100),
                NormalizarTexto(request.Descripcion, 500),
                NormalizarTexto(request.InstruccionIA, 2000),
                request.Orden);

        private static DatosNormalizados Normalizar(
            TipoFotografiaIAActualizarRequest request) =>
            new(
                NormalizarCodigo(request.Codigo),
                NormalizarTexto(request.Nombre, 100),
                NormalizarTexto(request.Descripcion, 500),
                NormalizarTexto(request.InstruccionIA, 2000),
                request.Orden);

        private static string NormalizarCodigo(string? valor) =>
            Regex.Replace(
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim()
                    .ToUpperInvariant()
                    .Replace(' ', '_'),
                "_+",
                "_");

        private static string NormalizarTexto(string? valor, int maximo)
        {
            string texto = Regex.Replace(
                (valor ?? string.Empty).ReplaceLineEndings(" ").Trim(),
                @"\s+",
                " ");

            return texto.Length <= maximo ? texto : texto[..maximo];
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }

        private DbCommand CrearComando(string sql)
        {
            DbCommand command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = 180;
            return command;
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            object? valor)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = nombre;
            parameter.Value = valor ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static async Task AbrirAsync(
            DbConnection connection,
            CancellationToken cancellationToken)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);
        }

        private static TipoFotografiaIARespuesta Leer(DbDataReader reader) =>
            new()
            {
                TipoFotografiaIAId = reader.GetInt32(0),
                Codigo = reader.GetString(1),
                Nombre = reader.GetString(2),
                Descripcion = reader.GetString(3),
                InstruccionIA = reader.GetString(4),
                Orden = reader.GetInt32(5),
                Activo = reader.GetBoolean(6),
                FechaCreacionUtc = reader.GetDateTime(7),
                FechaModificacionUtc = reader.GetDateTime(8)
            };

        private static object Error(string mensaje) =>
            new { success = false, message = mensaje };

        private sealed record DatosNormalizados(
            string Codigo,
            string Nombre,
            string Descripcion,
            string InstruccionIA,
            int Orden);

        [GeneratedRegex("^[A-Z0-9_]+$", RegexOptions.CultureInvariant)]
        private static partial Regex CodigoRegex();
    }
}
