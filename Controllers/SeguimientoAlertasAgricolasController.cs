using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/seguimiento-alertas-agricolas")]
    public sealed class SeguimientoAlertasAgricolasController : ControllerBase
    {
        private static readonly string[] EstadosValidos =
            ["PENDIENTE", "EN_PROCESO", "ATENDIDA", "DESCARTADA"];

        private readonly AlertasAgricolasDbContext alertasDb;
        private readonly DBContext db;

        public SeguimientoAlertasAgricolasController(
            AlertasAgricolasDbContext alertasDb,
            DBContext db)
        {
            this.alertasDb = alertasDb;
            this.db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<SeguimientoAlertaResponse>>> Listar(
            int? terrenoId = null,
            string? estado = null,
            CancellationToken cancellationToken = default)
        {
            IQueryable<SeguimientoAlertaAgricola> query =
                alertasDb.Seguimientos.AsNoTracking().Where(x => x.Activo);

            if (terrenoId is > 0)
                query = query.Where(x => x.TerrenoId == terrenoId);

            if (!string.IsNullOrWhiteSpace(estado))
            {
                string valor = estado.Trim().ToUpperInvariant();
                query = query.Where(x => x.Estado == valor);
            }

            List<SeguimientoAlertaAgricola> datos = await query
                .OrderBy(x => x.Estado == "ATENDIDA" || x.Estado == "DESCARTADA")
                .ThenByDescending(x => x.FechaUltimaModificacionUtc)
                .ToListAsync(cancellationToken);

            Dictionary<int, string> usuarios = await ObtenerUsuariosAsync(
                datos.Where(x => x.UsuarioAsignadoId.HasValue)
                    .Select(x => x.UsuarioAsignadoId!.Value),
                cancellationToken);

            return Ok(datos.Select(x => Mapear(x, usuarios)).ToList());
        }

        [HttpGet("abiertos")]
        public async Task<ActionResult<List<SeguimientoAlertaResponse>>> Abiertos(
            CancellationToken cancellationToken = default)
        {
            List<SeguimientoAlertaAgricola> datos = await alertasDb.Seguimientos
                .AsNoTracking()
                .Where(x => x.Activo &&
                            x.Estado != "ATENDIDA" &&
                            x.Estado != "DESCARTADA")
                .ToListAsync(cancellationToken);

            Dictionary<int, string> usuarios = await ObtenerUsuariosAsync(
                datos.Where(x => x.UsuarioAsignadoId.HasValue)
                    .Select(x => x.UsuarioAsignadoId!.Value),
                cancellationToken);

            return Ok(datos.Select(x => Mapear(x, usuarios)).ToList());
        }

        [HttpGet("tecnicos")]
        public async Task<ActionResult<List<TecnicoAlertaResponse>>> Tecnicos(
            CancellationToken cancellationToken = default)
        {
            var datos = await db.Usuarios
                .AsNoTracking()
                .Include(x => x.Rol)
                .Include(x => x.Procedencia)
                .Where(x => x.activo)
                .OrderBy(x => x.nombreCompletoUsuario)
                .Select(x => new TecnicoAlertaResponse
                {
                    usuarioId = x.UsuarioId,
                    nombreCompleto = x.nombreCompletoUsuario,
                    nombreUsuario = x.nombreUsuario,
                    rol = x.Rol.nombreRol,
                    procedencia = x.Procedencia.nombreProcedencia
                })
                .ToListAsync(cancellationToken);

            return Ok(datos);
        }

        [HttpPost]
        public async Task<ActionResult<SeguimientoAlertaResponse>> Crear(
            [FromBody] CrearSeguimientoAlertaRequest request,
            CancellationToken cancellationToken = default)
        {
            string tipo = request.tipoAlerta.Trim();

            SeguimientoAlertaAgricola? existente =
                await alertasDb.Seguimientos.FirstOrDefaultAsync(x =>
                    x.Activo &&
                    x.TerrenoId == request.terrenoId &&
                    x.TipoAlerta == tipo &&
                    x.Estado != "ATENDIDA" &&
                    x.Estado != "DESCARTADA",
                    cancellationToken);

            if (existente is not null)
                return Conflict(new
                {
                    message = "Ya existe un seguimiento abierto para esta alerta.",
                    seguimientoAlertaAgricolaId =
                        existente.SeguimientoAlertaAgricolaId
                });

            DateTime ahora = DateTime.UtcNow;

            var entidad = new SeguimientoAlertaAgricola
            {
                TerrenoId = request.terrenoId,
                TipoAlerta = tipo,
                Nivel = request.nivel.Trim().ToUpperInvariant(),
                Estado = "PENDIENTE",
                UsuarioAsignadoId = request.usuarioAsignadoId,
                Observacion = request.observacion.Trim(),
                FechaCreacionUtc = ahora,
                FechaUltimaModificacionUtc = ahora,
                UsuarioCreacionId = request.usuarioAccionId,
                UsuarioUltimaModificacionId = request.usuarioAccionId,
                Activo = true
            };

            alertasDb.Seguimientos.Add(entidad);
            await alertasDb.SaveChangesAsync(cancellationToken);

            await AgregarHistorialAsync(
                entidad.SeguimientoAlertaAgricolaId,
                "CREADA",
                "Se creó el seguimiento de la alerta.",
                request.usuarioAccionId,
                cancellationToken);

            Dictionary<int, string> usuarios =
                await ObtenerUsuariosAsync(
                    entidad.UsuarioAsignadoId.HasValue
                        ? [entidad.UsuarioAsignadoId.Value]
                        : [],
                    cancellationToken);

            return Ok(Mapear(entidad, usuarios));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<SeguimientoAlertaResponse>> Actualizar(
            int id,
            [FromBody] ActualizarSeguimientoAlertaRequest request,
            CancellationToken cancellationToken = default)
        {
            string estado = request.estado.Trim().ToUpperInvariant();

            if (!EstadosValidos.Contains(estado))
                return BadRequest(new { message = "El estado indicado no es válido." });

            SeguimientoAlertaAgricola? entidad =
                await alertasDb.Seguimientos.FirstOrDefaultAsync(x =>
                    x.SeguimientoAlertaAgricolaId == id && x.Activo,
                    cancellationToken);

            if (entidad is null)
                return NotFound(new { message = "No se encontró el seguimiento." });

            string estadoAnterior = entidad.Estado;
            int? asignadoAnterior = entidad.UsuarioAsignadoId;

            entidad.Estado = estado;
            entidad.UsuarioAsignadoId = request.usuarioAsignadoId;
            entidad.Observacion = request.observacion.Trim();
            entidad.FechaUltimaModificacionUtc = DateTime.UtcNow;
            entidad.UsuarioUltimaModificacionId = request.usuarioAccionId;
            entidad.FechaCierreUtc =
                estado is "ATENDIDA" or "DESCARTADA"
                    ? DateTime.UtcNow
                    : null;

            await alertasDb.SaveChangesAsync(cancellationToken);

            await AgregarHistorialAsync(
                id,
                "ACTUALIZADA",
                $"Estado: {estadoAnterior} → {estado}. Responsable: " +
                $"{asignadoAnterior?.ToString() ?? "sin asignar"} → " +
                $"{entidad.UsuarioAsignadoId?.ToString() ?? "sin asignar"}. " +
                $"Observación: {entidad.Observacion}",
                request.usuarioAccionId,
                cancellationToken);

            Dictionary<int, string> usuarios =
                await ObtenerUsuariosAsync(
                    entidad.UsuarioAsignadoId.HasValue
                        ? [entidad.UsuarioAsignadoId.Value]
                        : [],
                    cancellationToken);

            return Ok(Mapear(entidad, usuarios));
        }

        [HttpGet("{id:int}/historial")]
        public async Task<ActionResult<List<HistorialAlertaResponse>>> Historial(
            int id,
            CancellationToken cancellationToken = default)
        {
            var baseDatos = await alertasDb.Historial.AsNoTracking()
                .Where(x => x.SeguimientoAlertaAgricolaId == id)
                .OrderByDescending(x => x.FechaUtc)
                .ToListAsync(cancellationToken);

            Dictionary<int, string> usuarios = await ObtenerUsuariosAsync(
                baseDatos.Select(x => x.UsuarioId), cancellationToken);

            return Ok(baseDatos.Select(x => new HistorialAlertaResponse
            {
                historialAlertaAgricolaId = x.HistorialAlertaAgricolaId,
                accion = x.Accion,
                detalle = x.Detalle,
                usuarioId = x.UsuarioId,
                usuario = usuarios.TryGetValue(x.UsuarioId, out string? nombre)
                    ? nombre : $"Usuario #{x.UsuarioId}",
                fechaUtc = x.FechaUtc
            }).ToList());
        }

        [HttpGet("~/api/configuracion-alertas-agricolas")]
        public async Task<ActionResult<List<ConfiguracionAlertaResponse>>> Configuraciones(
            CancellationToken cancellationToken = default)
        {
            return Ok(await alertasDb.Configuraciones.AsNoTracking()
                .Where(x => x.Activo)
                .OrderBy(x => x.Nombre)
                .Select(x => new ConfiguracionAlertaResponse
                {
                    configuracionAlertaAgricolaId = x.ConfiguracionAlertaAgricolaId,
                    clave = x.Clave,
                    nombre = x.Nombre,
                    valor = x.Valor,
                    operador = x.Operador,
                    unidad = x.Unidad,
                    descripcion = x.Descripcion
                })
                .ToListAsync(cancellationToken));
        }

        [HttpPut("~/api/configuracion-alertas-agricolas/{id:int}")]
        public async Task<ActionResult> ActualizarUmbral(
            int id,
            [FromBody] ActualizarUmbralAlertaRequest request,
            CancellationToken cancellationToken = default)
        {
            ConfiguracionAlertaAgricola? entidad =
                await alertasDb.Configuraciones.FirstOrDefaultAsync(x =>
                    x.ConfiguracionAlertaAgricolaId == id && x.Activo,
                    cancellationToken);

            if (entidad is null)
                return NotFound(new { message = "No se encontró la configuración." });

            if (request.valor < 0)
                return BadRequest(new { message = "El valor no puede ser negativo." });

            entidad.Valor = request.valor;
            entidad.FechaModificacionUtc = DateTime.UtcNow;
            entidad.UsuarioModificacionId = request.usuarioAccionId;
            await alertasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Umbral actualizado correctamente."
            });
        }

        private async Task AgregarHistorialAsync(
            int seguimientoId,
            string accion,
            string detalle,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            alertasDb.Historial.Add(new HistorialAlertaAgricola
            {
                SeguimientoAlertaAgricolaId = seguimientoId,
                Accion = accion,
                Detalle = detalle,
                UsuarioId = usuarioId,
                FechaUtc = DateTime.UtcNow
            });

            await alertasDb.SaveChangesAsync(cancellationToken);
        }

        private async Task<Dictionary<int, string>> ObtenerUsuariosAsync(
            IEnumerable<int> ids,
            CancellationToken cancellationToken)
        {
            int[] valores = ids.Distinct().ToArray();

            if (valores.Length == 0)
                return new();

            return await db.Usuarios.AsNoTracking()
                .Where(x => valores.Contains(x.UsuarioId))
                .ToDictionaryAsync(
                    x => x.UsuarioId,
                    x => x.nombreCompletoUsuario,
                    cancellationToken);
        }

        private static SeguimientoAlertaResponse Mapear(
            SeguimientoAlertaAgricola x,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                seguimientoAlertaAgricolaId = x.SeguimientoAlertaAgricolaId,
                terrenoId = x.TerrenoId,
                tipoAlerta = x.TipoAlerta,
                nivel = x.Nivel,
                estado = x.Estado,
                usuarioAsignadoId = x.UsuarioAsignadoId,
                usuarioAsignado =
                    x.UsuarioAsignadoId.HasValue &&
                    usuarios.TryGetValue(x.UsuarioAsignadoId.Value, out string? nombre)
                        ? nombre
                        : null,
                observacion = x.Observacion,
                fechaCreacionUtc = x.FechaCreacionUtc,
                fechaUltimaModificacionUtc = x.FechaUltimaModificacionUtc,
                fechaCierreUtc = x.FechaCierreUtc
            };
    }
}
