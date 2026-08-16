using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consultas de eliminados acotadas al contexto jerárquico de la
    /// administración actual de ubicaciones.
    ///
    /// El controlador global de CatalogosEliminados se conserva intacto para
    /// versiones anteriores y otros consumidores. Estas rutas evitan mezclar
    /// departamentos de distintos países o municipios de distintos
    /// departamentos en la interfaz administrativa actual.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/ubicaciones/eliminados")]
    public sealed class AdministracionUbicacionesEliminadosController
        : ControllerBase
    {
        private const string PermisoDepartamentos = "departamentoPage";
        private const string PermisoMunicipios = "municipioPage";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public AdministracionUbicacionesEliminadosController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        /// <summary>
        /// Devuelve únicamente departamentos inactivos pertenecientes al país
        /// desde el que se abrió la pantalla de Departamentos.
        /// </summary>
        [HttpGet("departamentos")]
        public async Task<IActionResult> ListarDepartamentos(
            [FromQuery] int paisId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    PermisoDepartamentos,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (paisId <= 0)
            {
                return BadRequest(Error(
                    "Debe indicar un país válido para consultar los departamentos eliminados."));
            }

            bool paisActivo =
                await db.Pais
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.PaisId == paisId &&
                            item.Activo,
                        cancellationToken);

            if (!paisActivo)
            {
                return NotFound(Error(
                    "El país indicado no existe o está inactivo."));
            }

            var data =
                await db.Departamento
                    .AsNoTracking()
                    .Where(item =>
                        item.PaisId == paisId &&
                        !item.Activo)
                    .OrderBy(item => item.NombreDepartamento)
                    .Select(item => new
                    {
                        id = item.DepartamentoId,
                        catalogo = "departamento",
                        titulo = item.NombreDepartamento,
                        subtitulo = "País: " + item.Pais.NombrePais,
                        detalle =
                            item.Municipios.Count == 1
                                ? "1 municipio relacionado"
                                : item.Municipios.Count +
                                  " municipios relacionados",
                        codigo = item.Pais.CodigoISOPais,
                        activo = item.Activo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Departamentos eliminados del país obtenidos correctamente.",
                data
            });
        }

        /// <summary>
        /// Devuelve únicamente municipios inactivos pertenecientes al
        /// departamento desde el que se abrió la pantalla de Municipios.
        /// </summary>
        [HttpGet("municipios")]
        public async Task<IActionResult> ListarMunicipios(
            [FromQuery] int departamentoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    PermisoMunicipios,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (departamentoId <= 0)
            {
                return BadRequest(Error(
                    "Debe indicar un departamento válido para consultar los municipios eliminados."));
            }

            bool jerarquiaActiva =
                await db.Departamento
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.DepartamentoId == departamentoId &&
                            item.Activo &&
                            item.Pais.Activo,
                        cancellationToken);

            if (!jerarquiaActiva)
            {
                return NotFound(Error(
                    "El departamento indicado no existe, está inactivo o su país está inactivo."));
            }

            var data =
                await db.Municipios
                    .AsNoTracking()
                    .Where(item =>
                        item.DepartamentoId == departamentoId &&
                        !item.Activo)
                    .OrderBy(item => item.NombreMunicipio)
                    .Select(item => new
                    {
                        id = item.MunicipioId,
                        catalogo = "municipio",
                        titulo = item.NombreMunicipio,
                        subtitulo =
                            item.Departamento.NombreDepartamento +
                            " · " +
                            item.Departamento.Pais.NombrePais,
                        detalle =
                            item.Usuarios.Count == 1
                                ? "1 usuario relacionado"
                                : item.Usuarios.Count +
                                  " usuarios relacionados",
                        codigo = item.Departamento.Pais.CodigoISOPais,
                        activo = item.Activo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Municipios eliminados del departamento obtenidos correctamente.",
                data
            });
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    interfaz,
                    tipo,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };
    }
}
