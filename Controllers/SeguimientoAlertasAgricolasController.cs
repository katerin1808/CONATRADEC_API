using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/seguimiento-alertas-agricolas")]
    public sealed class SeguimientoAlertasAgricolasController
        : ControllerBase
    {
        private static readonly string[] EstadosValidos =
        [
            "PENDIENTE",
            "EN_PROCESO",
            "ATENDIDA",
            "DESCARTADA"
        ];

        private readonly AlertasAgricolasDbContext alertasDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public SeguimientoAlertasAgricolasController(
            AlertasAgricolasDbContext alertasDb,
            DBContext db,
            PermisoApiService permisos)
        {
            this.alertasDb = alertasDb;
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet]
        public async Task<
            ActionResult<List<SeguimientoAlertaResponse>>>
            Listar(
                int? terrenoId = null,
                string? estado = null,
                CancellationToken cancellationToken = default)
        {
            IQueryable<SeguimientoAlertaAgricola> query =
                alertasDb.Seguimientos
                    .AsNoTracking()
                    .Where(item => item.Activo);

            if (terrenoId is > 0)
            {
                query = query.Where(item =>
                    item.TerrenoId ==
                    terrenoId.Value);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                string valor =
                    estado.Trim().ToUpperInvariant();

                if (!EstadosValidos.Contains(valor))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "El estado indicado no es válido."
                    });
                }

                query = query.Where(item =>
                    item.Estado == valor);
            }

            List<SeguimientoAlertaAgricola> datos =
                await query
                    .OrderBy(item =>
                        item.Estado == "ATENDIDA" ||
                        item.Estado == "DESCARTADA")
                    .ThenByDescending(item =>
                        item.FechaUltimaModificacionUtc)
                    .ToListAsync(cancellationToken);

            return Ok(
                await MapearListaAsync(
                    datos,
                    cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<
            ActionResult<SeguimientoAlertaResponse>>
            Obtener(
                int id,
                CancellationToken cancellationToken = default)
        {
            SeguimientoAlertaAgricola? entidad =
                await alertasDb.Seguimientos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.SeguimientoAlertaAgricolaId ==
                                id &&
                            item.Activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el seguimiento solicitado."
                });
            }

            List<SeguimientoAlertaResponse> resultado =
                await MapearListaAsync(
                    [entidad],
                    cancellationToken);

            return Ok(resultado[0]);
        }

        [HttpGet("abiertos")]
        public async Task<
            ActionResult<List<SeguimientoAlertaResponse>>>
            Abiertos(
                CancellationToken cancellationToken = default)
        {
            List<SeguimientoAlertaAgricola> datos =
                await alertasDb.Seguimientos
                    .AsNoTracking()
                    .Where(item =>
                        item.Activo &&
                        item.Estado != "ATENDIDA" &&
                        item.Estado != "DESCARTADA")
                    .OrderByDescending(item =>
                        item.FechaUltimaModificacionUtc)
                    .ToListAsync(cancellationToken);

            return Ok(
                await MapearListaAsync(
                    datos,
                    cancellationToken));
        }

        [HttpGet("tecnicos")]
        public async Task<
            ActionResult<List<TecnicoAlertaResponse>>>
            Tecnicos(
                CancellationToken cancellationToken = default)
        {
            List<TecnicoAlertaResponse> datos =
                await db.Usuarios
                    .AsNoTracking()
                    .Include(item => item.Rol)
                    .Include(item => item.Procedencia)
                    .Where(item => item.activo)
                    .OrderBy(item =>
                        item.nombreCompletoUsuario)
                    .Select(item =>
                        new TecnicoAlertaResponse
                        {
                            usuarioId =
                                item.UsuarioId,

                            nombreCompleto =
                                item.nombreCompletoUsuario,

                            nombreUsuario =
                                item.nombreUsuario,

                            rol =
                                item.Rol.nombreRol,

                            procedencia =
                                item.Procedencia
                                    .nombreProcedencia
                        })
                    .ToListAsync(cancellationToken);

            return Ok(datos);
        }

        [HttpPost]
        public async Task<
            ActionResult<SeguimientoAlertaResponse>>
            Crear(
                [FromBody]
                CrearSeguimientoAlertaRequest request,
                CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    request.usuarioAccionId,
                    "SeguimientoAlertasWeb",
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            string tipo =
                request.tipoAlerta.Trim();

            bool terrenoExiste =
                await db.Terreno
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.terrenoId ==
                                request.terrenoId &&
                            item.activo,
                        cancellationToken);

            if (!terrenoExiste)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el terreno activo asociado a la alerta."
                });
            }

            if (request.usuarioAsignadoId.HasValue)
            {
                bool usuarioExiste =
                    await db.Usuarios
                        .AsNoTracking()
                        .AnyAsync(
                            item =>
                                item.UsuarioId ==
                                    request.usuarioAsignadoId.Value &&
                                item.activo,
                            cancellationToken);

                if (!usuarioExiste)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "El técnico seleccionado no existe o está inactivo."
                    });
                }
            }

            SeguimientoAlertaAgricola? existente =
                await alertasDb.Seguimientos
                    .FirstOrDefaultAsync(
                        item =>
                            item.Activo &&
                            item.TerrenoId ==
                                request.terrenoId &&
                            item.TipoAlerta == tipo &&
                            item.Estado != "ATENDIDA" &&
                            item.Estado != "DESCARTADA",
                        cancellationToken);

            if (existente is not null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un seguimiento abierto para esta alerta.",
                    seguimientoAlertaAgricolaId =
                        existente
                            .SeguimientoAlertaAgricolaId
                });
            }

            DateTime ahora =
                DateTime.UtcNow;

            var entidad =
                new SeguimientoAlertaAgricola
                {
                    TerrenoId =
                        request.terrenoId,

                    TipoAlerta =
                        tipo,

                    Nivel =
                        request.nivel
                            .Trim()
                            .ToUpperInvariant(),

                    Estado =
                        "PENDIENTE",

                    UsuarioAsignadoId =
                        request.usuarioAsignadoId,

                    Observacion =
                        request.observacion.Trim(),

                    FechaCreacionUtc =
                        ahora,

                    FechaUltimaModificacionUtc =
                        ahora,

                    UsuarioCreacionId =
                        request.usuarioAccionId,

                    UsuarioUltimaModificacionId =
                        request.usuarioAccionId,

                    Activo = true
                };

            alertasDb.Seguimientos.Add(entidad);

            await alertasDb.SaveChangesAsync(
                cancellationToken);

            await AgregarHistorialAsync(
                entidad.SeguimientoAlertaAgricolaId,
                "CREADA",
                ConstruirDetalleCreacion(entidad),
                request.usuarioAccionId,
                cancellationToken);

            List<SeguimientoAlertaResponse> resultado =
                await MapearListaAsync(
                    [entidad],
                    cancellationToken);

            return Ok(resultado[0]);
        }

        [HttpPut("{id:int}")]
        public async Task<
            ActionResult<SeguimientoAlertaResponse>>
            Actualizar(
                int id,
                [FromBody]
                ActualizarSeguimientoAlertaRequest request,
                CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    request.usuarioAccionId,
                    "SeguimientoAlertasWeb",
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            string estado =
                request.estado
                    .Trim()
                    .ToUpperInvariant();

            if (!EstadosValidos.Contains(estado))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El estado indicado no es válido."
                });
            }

            if (request.usuarioAsignadoId.HasValue)
            {
                bool usuarioExiste =
                    await db.Usuarios
                        .AsNoTracking()
                        .AnyAsync(
                            item =>
                                item.UsuarioId ==
                                    request.usuarioAsignadoId.Value &&
                                item.activo,
                            cancellationToken);

                if (!usuarioExiste)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "El técnico seleccionado no existe o está inactivo."
                    });
                }
            }

            SeguimientoAlertaAgricola? entidad =
                await alertasDb.Seguimientos
                    .FirstOrDefaultAsync(
                        item =>
                            item.SeguimientoAlertaAgricolaId ==
                                id &&
                            item.Activo,
                        cancellationToken);

            if (entidad is null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el seguimiento."
                });
            }

            string estadoAnterior =
                entidad.Estado;

            int? asignadoAnterior =
                entidad.UsuarioAsignadoId;

            string observacionAnterior =
                entidad.Observacion;

            entidad.Estado =
                estado;

            entidad.UsuarioAsignadoId =
                request.usuarioAsignadoId;

            entidad.Observacion =
                request.observacion.Trim();

            entidad.FechaUltimaModificacionUtc =
                DateTime.UtcNow;

            entidad.UsuarioUltimaModificacionId =
                request.usuarioAccionId;

            entidad.FechaCierreUtc =
                estado is "ATENDIDA" or "DESCARTADA"
                    ? DateTime.UtcNow
                    : null;

            await alertasDb.SaveChangesAsync(
                cancellationToken);

            string detalle =
                ConstruirDetalleActualizacion(
                    estadoAnterior,
                    estado,
                    asignadoAnterior,
                    entidad.UsuarioAsignadoId,
                    observacionAnterior,
                    entidad.Observacion);

            await AgregarHistorialAsync(
                id,
                "ACTUALIZADA",
                detalle,
                request.usuarioAccionId,
                cancellationToken);

            List<SeguimientoAlertaResponse> resultado =
                await MapearListaAsync(
                    [entidad],
                    cancellationToken);

            return Ok(resultado[0]);
        }

        [HttpGet("{id:int}/historial")]
        public async Task<
            ActionResult<List<HistorialAlertaResponse>>>
            Historial(
                int id,
                CancellationToken cancellationToken = default)
        {
            bool seguimientoExiste =
                await alertasDb.Seguimientos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.SeguimientoAlertaAgricolaId ==
                                id &&
                            item.Activo,
                        cancellationToken);

            if (!seguimientoExiste)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el seguimiento."
                });
            }

            List<HistorialAlertaAgricola> datos =
                await alertasDb.Historial
                    .AsNoTracking()
                    .Where(item =>
                        item.SeguimientoAlertaAgricolaId ==
                            id)
                    .OrderByDescending(item =>
                        item.FechaUtc)
                    .ToListAsync(cancellationToken);

            Dictionary<int, string> usuarios =
                await ObtenerUsuariosAsync(
                    datos.Select(item =>
                        item.UsuarioId),
                    cancellationToken);

            return Ok(
                datos.Select(item =>
                    new HistorialAlertaResponse
                    {
                        historialAlertaAgricolaId =
                            item.HistorialAlertaAgricolaId,

                        accion =
                            item.Accion,

                        detalle =
                            item.Detalle,

                        usuarioId =
                            item.UsuarioId,

                        usuario =
                            usuarios.TryGetValue(
                                item.UsuarioId,
                                out string? nombre)
                                ? nombre
                                : $"Usuario #{item.UsuarioId}",

                        fechaUtc =
                            item.FechaUtc
                    })
                    .ToList());
        }

        [HttpGet(
            "~/api/configuracion-alertas-agricolas")]
        public async Task<
            ActionResult<List<ConfiguracionAlertaResponse>>>
            Configuraciones(
                CancellationToken cancellationToken = default)
        {
            return Ok(
                await alertasDb.Configuraciones
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderBy(item => item.Nombre)
                    .Select(item =>
                        new ConfiguracionAlertaResponse
                        {
                            configuracionAlertaAgricolaId =
                                item.ConfiguracionAlertaAgricolaId,

                            clave =
                                item.Clave,

                            nombre =
                                item.Nombre,

                            valor =
                                item.Valor,

                            operador =
                                item.Operador,

                            unidad =
                                item.Unidad,

                            descripcion =
                                item.Descripcion
                        })
                    .ToListAsync(cancellationToken));
        }

        [HttpPut(
            "~/api/configuracion-alertas-agricolas/{id:int}")]
        public async Task<ActionResult>
            ActualizarUmbral(
                int id,
                [FromBody]
                ActualizarUmbralAlertaRequest request,
                CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    request.usuarioAccionId,
                    "ConfiguracionAlertasWeb",
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            ConfiguracionAlertaAgricola? entidad =
                await alertasDb.Configuraciones
                    .FirstOrDefaultAsync(
                        item =>
                            item.ConfiguracionAlertaAgricolaId ==
                                id &&
                            item.Activo,
                        cancellationToken);

            if (entidad is null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró la configuración."
                });
            }

            if (request.valor < 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El valor no puede ser negativo."
                });
            }

            entidad.Valor =
                request.valor;

            entidad.FechaModificacionUtc =
                DateTime.UtcNow;

            entidad.UsuarioModificacionId =
                request.usuarioAccionId;

            await alertasDb.SaveChangesAsync(
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Umbral actualizado correctamente."
            });
        }

        private async Task<
            List<SeguimientoAlertaResponse>>
            MapearListaAsync(
                IReadOnlyCollection<
                    SeguimientoAlertaAgricola> datos,
                CancellationToken cancellationToken)
        {
            Dictionary<int, string> usuarios =
                await ObtenerUsuariosAsync(
                    datos
                        .Where(item =>
                            item.UsuarioAsignadoId
                                .HasValue)
                        .Select(item =>
                            item.UsuarioAsignadoId!
                                .Value),
                    cancellationToken);

            Dictionary<int, TerrenoSeguimientoInfo>
                terrenos =
                    await ObtenerTerrenosAsync(
                        datos.Select(item =>
                            item.TerrenoId),
                        cancellationToken);

            return datos
                .Select(item =>
                    Mapear(
                        item,
                        usuarios,
                        terrenos))
                .ToList();
        }

        private async Task AgregarHistorialAsync(
            int seguimientoId,
            string accion,
            string detalle,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            alertasDb.Historial.Add(
                new HistorialAlertaAgricola
                {
                    SeguimientoAlertaAgricolaId =
                        seguimientoId,

                    Accion =
                        accion,

                    Detalle =
                        detalle,

                    UsuarioId =
                        usuarioId,

                    FechaUtc =
                        DateTime.UtcNow
                });

            await alertasDb.SaveChangesAsync(
                cancellationToken);
        }

        private async Task<Dictionary<int, string>>
            ObtenerUsuariosAsync(
                IEnumerable<int> ids,
                CancellationToken cancellationToken)
        {
            int[] valores =
                ids.Distinct().ToArray();

            if (valores.Length == 0)
                return new();

            return await db.Usuarios
                .AsNoTracking()
                .Where(item =>
                    valores.Contains(
                        item.UsuarioId))
                .ToDictionaryAsync(
                    item => item.UsuarioId,
                    item =>
                        item.nombreCompletoUsuario,
                    cancellationToken);
        }

        private async Task<
            Dictionary<int, TerrenoSeguimientoInfo>>
            ObtenerTerrenosAsync(
                IEnumerable<int> ids,
                CancellationToken cancellationToken)
        {
            int[] valores =
                ids.Distinct().ToArray();

            if (valores.Length == 0)
                return new();

            return await db.Terreno
                .AsNoTracking()
                .Where(item =>
                    valores.Contains(
                        item.terrenoId))
                .Select(item =>
                    new TerrenoSeguimientoInfo
                    {
                        TerrenoId =
                            item.terrenoId,

                        Codigo =
                            item.codigoTerreno,

                        Propietario =
                            item.RelacionesPropietario
                                .Where(relacion =>
                                    relacion.activo &&
                                    relacion.Propietario.activo)
                                .Select(relacion =>
                                    relacion.Propietario.nombreCompleto)
                                .FirstOrDefault() ??
                            string.Empty,

                        Direccion =
                            item.direccionTerreno,

                        Municipio =
                            item.Municipio
                                .NombreMunicipio,

                        Departamento =
                            item.Municipio
                                .Departamento
                                .NombreDepartamento
                    })
                .ToDictionaryAsync(
                    item => item.TerrenoId,
                    cancellationToken);
        }

        private static SeguimientoAlertaResponse
            Mapear(
                SeguimientoAlertaAgricola entidad,
                IReadOnlyDictionary<int, string> usuarios,
                IReadOnlyDictionary<
                    int,
                    TerrenoSeguimientoInfo> terrenos)
        {
            terrenos.TryGetValue(
                entidad.TerrenoId,
                out TerrenoSeguimientoInfo? terreno);

            return new SeguimientoAlertaResponse
            {
                seguimientoAlertaAgricolaId =
                    entidad.SeguimientoAlertaAgricolaId,

                terrenoId =
                    entidad.TerrenoId,

                codigoTerreno =
                    terreno?.Codigo ??
                    $"Terreno #{entidad.TerrenoId}",

                propietario =
                    terreno?.Propietario ??
                    string.Empty,

                direccion =
                    terreno?.Direccion ??
                    string.Empty,

                municipio =
                    terreno?.Municipio ??
                    string.Empty,

                departamento =
                    terreno?.Departamento ??
                    string.Empty,

                tipoAlerta =
                    entidad.TipoAlerta,

                nivel =
                    entidad.Nivel,

                estado =
                    entidad.Estado,

                usuarioAsignadoId =
                    entidad.UsuarioAsignadoId,

                usuarioAsignado =
                    entidad.UsuarioAsignadoId
                        .HasValue &&
                    usuarios.TryGetValue(
                        entidad.UsuarioAsignadoId.Value,
                        out string? nombre)
                            ? nombre
                            : null,

                observacion =
                    entidad.Observacion,

                fechaCreacionUtc =
                    entidad.FechaCreacionUtc,

                fechaUltimaModificacionUtc =
                    entidad.FechaUltimaModificacionUtc,

                fechaCierreUtc =
                    entidad.FechaCierreUtc
            };
        }

        private static string
            ConstruirDetalleCreacion(
                SeguimientoAlertaAgricola entidad)
        {
            string responsable =
                entidad.UsuarioAsignadoId
                    .HasValue
                        ? $"Usuario #{entidad.UsuarioAsignadoId.Value}"
                        : "Sin asignar";

            string observacion =
                string.IsNullOrWhiteSpace(
                    entidad.Observacion)
                        ? "Sin observación inicial."
                        : entidad.Observacion;

            return
                $"Se creó el seguimiento en estado PENDIENTE. " +
                $"Responsable: {responsable}. " +
                $"Observación: {observacion}";
        }

        private static string
            ConstruirDetalleActualizacion(
                string estadoAnterior,
                string estadoNuevo,
                int? asignadoAnterior,
                int? asignadoNuevo,
                string observacionAnterior,
                string observacionNueva)
        {
            var cambios =
                new List<string>();

            if (!string.Equals(
                    estadoAnterior,
                    estadoNuevo,
                    StringComparison.Ordinal))
            {
                cambios.Add(
                    $"Estado: {estadoAnterior} → {estadoNuevo}");
            }

            if (asignadoAnterior != asignadoNuevo)
            {
                cambios.Add(
                    "Responsable: " +
                    $"{asignadoAnterior?.ToString() ?? "sin asignar"} → " +
                    $"{asignadoNuevo?.ToString() ?? "sin asignar"}");
            }

            if (!string.Equals(
                    observacionAnterior,
                    observacionNueva,
                    StringComparison.Ordinal))
            {
                cambios.Add(
                    $"Observación: {observacionNueva}");
            }

            return cambios.Count == 0
                ? "Se guardó el seguimiento sin cambios visibles."
                : string.Join(". ", cambios) + ".";
        }

        private sealed class TerrenoSeguimientoInfo
        {
            public int TerrenoId { get; set; }

            public string Codigo { get; set; } =
                string.Empty;

            public string Propietario { get; set; } =
                string.Empty;

            public string Direccion { get; set; } =
                string.Empty;

            public string Municipio { get; set; } =
                string.Empty;

            public string Departamento { get; set; } =
                string.Empty;
        }
    }
}
