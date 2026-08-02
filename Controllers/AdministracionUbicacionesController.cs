using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/administracion/ubicaciones")]
    public sealed class AdministracionUbicacionesController : ControllerBase
    {
        private const string PermisoPaises = "paisPage";
        private const string PermisoDepartamentos = "departamentoPage";
        private const string PermisoMunicipios = "municipioPage";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public AdministracionUbicacionesController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet("paises")]
        public async Task<IActionResult> ListarPaises(
            string? buscar = null,
            int pagina = 1,
            int tamanoPagina = 20,
            bool incluirInactivos = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises, TipoPermisoApi.Leer, cancellationToken);
            if (acceso != null) return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Pais> query = db.Pais.AsNoTracking();

            if (!incluirInactivos)
                query = query.Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.NombrePais.Contains(texto) ||
                    x.CodigoISOPais.Contains(texto));
            }

            int total = await query.CountAsync(cancellationToken);

            List<PaisAdminDto> items = await query
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.NombrePais)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(x => new PaisAdminDto
                {
                    PaisId = x.PaisId,
                    Nombre = x.NombrePais,
                    CodigoIso = x.CodigoISOPais,
                    Activo = x.Activo,
                    CantidadDependencias =
                        x.Departamentos.Count(y => y.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(PaginaRespuesta<PaisAdminDto>.Crear(
                items, pagina, tamanoPagina, total));
        }

        [HttpPost("paises")]
        public async Task<IActionResult> CrearPais(
            PaisGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises, TipoPermisoApi.Agregar, cancellationToken);
            if (acceso != null) return acceso;

            string nombre = NormalizarNombre(dto.Nombre);
            string codigo = NormalizarCodigo(dto.CodigoIso);

            if (string.IsNullOrWhiteSpace(nombre) || codigo.Length != 3)
            {
                return BadRequest(Error(
                    "El nombre y un código ISO de tres letras son obligatorios."));
            }

            bool duplicado = await db.Pais.AnyAsync(
                x => x.Activo &&
                     (x.NombrePais == nombre ||
                      x.CodigoISOPais == codigo),
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Ya existe un país activo con el mismo nombre o código ISO."));

            db.Pais.Add(new Pais
            {
                NombrePais = nombre,
                CodigoISOPais = codigo,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("País creado correctamente."));
        }

        [HttpPut("paises/{id:int}")]
        public async Task<IActionResult> ActualizarPais(
            int id,
            PaisGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El país no existe."));
            if (!entidad.Activo)
                return Conflict(Error("Reactive el país antes de editarlo."));

            string nombre = NormalizarNombre(dto.Nombre);
            string codigo = NormalizarCodigo(dto.CodigoIso);

            if (string.IsNullOrWhiteSpace(nombre) || codigo.Length != 3)
            {
                return BadRequest(Error(
                    "El nombre y un código ISO de tres letras son obligatorios."));
            }

            bool duplicado = await db.Pais.AnyAsync(
                x => x.PaisId != id &&
                     x.Activo &&
                     (x.NombrePais == nombre ||
                      x.CodigoISOPais == codigo),
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Otro país utiliza el mismo nombre o código ISO."));

            entidad.NombrePais = nombre;
            entidad.CodigoISOPais = codigo;

            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("País actualizado correctamente."));
        }

        [HttpDelete("paises/{id:int}")]
        public async Task<IActionResult> DesactivarPais(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises, TipoPermisoApi.Eliminar, cancellationToken);
            if (acceso != null) return acceso;

            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id && x.Activo, cancellationToken);

            if (entidad == null)
                return NotFound(Error(
                    "El país no existe o ya está inactivo."));

            bool tieneDependencias = await db.Departamento.AnyAsync(
                x => x.PaisId == id && x.Activo, cancellationToken);

            if (tieneDependencias)
            {
                return Conflict(Error(
                    "No puede desactivar el país mientras tenga departamentos activos."));
            }

            entidad.Activo = false;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("País desactivado correctamente."));
        }

        [HttpPost("paises/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarPais(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El país no existe."));

            entidad.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("País reactivado correctamente."));
        }

        [HttpGet("departamentos")]
        public async Task<IActionResult> ListarDepartamentos(
            int paisId,
            string? buscar = null,
            int pagina = 1,
            int tamanoPagina = 20,
            bool incluirInactivos = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos, TipoPermisoApi.Leer, cancellationToken);
            if (acceso != null) return acceso;

            if (paisId <= 0)
                return BadRequest(Error("Debe seleccionar un país."));

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Departamento> query = db.Departamento
                .AsNoTracking()
                .Where(x => x.PaisId == paisId);

            if (!incluirInactivos)
                query = query.Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(texto))
                query = query.Where(x => x.NombreDepartamento.Contains(texto));

            int total = await query.CountAsync(cancellationToken);

            List<DepartamentoAdminDto> items = await query
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.NombreDepartamento)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(x => new DepartamentoAdminDto
                {
                    DepartamentoId = x.DepartamentoId,
                    PaisId = x.PaisId,
                    Nombre = x.NombreDepartamento,
                    Activo = x.Activo,
                    CantidadDependencias =
                        x.Municipios.Count(y => y.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(PaginaRespuesta<DepartamentoAdminDto>.Crear(
                items, pagina, tamanoPagina, total));
        }

        [HttpPost("departamentos")]
        public async Task<IActionResult> CrearDepartamento(
            DepartamentoGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos, TipoPermisoApi.Agregar, cancellationToken);
            if (acceso != null) return acceso;

            string nombre = NormalizarNombre(dto.Nombre);

            bool padreActivo = await db.Pais.AnyAsync(
                x => x.PaisId == dto.PaisId && x.Activo,
                cancellationToken);

            if (!padreActivo)
                return BadRequest(Error("Seleccione un país activo."));
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error(
                    "El nombre del departamento es obligatorio."));

            bool duplicado = await db.Departamento.AnyAsync(
                x => x.PaisId == dto.PaisId &&
                     x.Activo &&
                     x.NombreDepartamento == nombre,
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Ya existe ese departamento en el país seleccionado."));

            db.Departamento.Add(new Departamento
            {
                PaisId = dto.PaisId,
                NombreDepartamento = nombre,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("Departamento creado correctamente."));
        }

        [HttpPut("departamentos/{id:int}")]
        public async Task<IActionResult> ActualizarDepartamento(
            int id,
            DepartamentoGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Departamento? entidad = await db.Departamento.FirstOrDefaultAsync(
                x => x.DepartamentoId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El departamento no existe."));
            if (!entidad.Activo)
                return Conflict(Error(
                    "Reactive el departamento antes de editarlo."));

            string nombre = NormalizarNombre(dto.Nombre);
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            bool duplicado = await db.Departamento.AnyAsync(
                x => x.DepartamentoId != id &&
                     x.PaisId == entidad.PaisId &&
                     x.Activo &&
                     x.NombreDepartamento == nombre,
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Otro departamento utiliza ese nombre."));

            entidad.NombreDepartamento = nombre;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Departamento actualizado correctamente."));
        }

        [HttpDelete("departamentos/{id:int}")]
        public async Task<IActionResult> DesactivarDepartamento(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos, TipoPermisoApi.Eliminar, cancellationToken);
            if (acceso != null) return acceso;

            Departamento? entidad = await db.Departamento.FirstOrDefaultAsync(
                x => x.DepartamentoId == id && x.Activo,
                cancellationToken);

            if (entidad == null)
                return NotFound(Error(
                    "El departamento no existe o ya está inactivo."));

            bool tieneDependencias = await db.Municipios.AnyAsync(
                x => x.DepartamentoId == id && x.Activo,
                cancellationToken);

            if (tieneDependencias)
            {
                return Conflict(Error(
                    "No puede desactivar el departamento mientras tenga municipios activos."));
            }

            entidad.Activo = false;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito(
                "Departamento desactivado correctamente."));
        }

        [HttpPost("departamentos/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarDepartamento(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Departamento? entidad = await db.Departamento.FirstOrDefaultAsync(
                x => x.DepartamentoId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El departamento no existe."));

            bool padreActivo = await db.Pais.AnyAsync(
                x => x.PaisId == entidad.PaisId && x.Activo,
                cancellationToken);

            if (!padreActivo)
                return Conflict(Error(
                    "Debe reactivar primero el país relacionado."));

            entidad.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito(
                "Departamento reactivado correctamente."));
        }

        [HttpGet("municipios")]
        public async Task<IActionResult> ListarMunicipios(
            int departamentoId,
            string? buscar = null,
            int pagina = 1,
            int tamanoPagina = 20,
            bool incluirInactivos = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios, TipoPermisoApi.Leer, cancellationToken);
            if (acceso != null) return acceso;

            if (departamentoId <= 0)
                return BadRequest(Error(
                    "Debe seleccionar un departamento."));

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Municipio> query = db.Municipios
                .AsNoTracking()
                .Where(x => x.DepartamentoId == departamentoId);

            if (!incluirInactivos)
                query = query.Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(texto))
                query = query.Where(x => x.NombreMunicipio.Contains(texto));

            int total = await query.CountAsync(cancellationToken);

            List<MunicipioAdminDto> items = await query
                .OrderByDescending(x => x.Activo)
                .ThenBy(x => x.NombreMunicipio)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(x => new MunicipioAdminDto
                {
                    MunicipioId = x.MunicipioId,
                    DepartamentoId = x.DepartamentoId,
                    Nombre = x.NombreMunicipio,
                    Activo = x.Activo,
                    CantidadTerrenos = db.Terreno.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo),
                    CantidadUsuarios = db.Usuarios.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(PaginaRespuesta<MunicipioAdminDto>.Crear(
                items, pagina, tamanoPagina, total));
        }

        [HttpPost("municipios")]
        public async Task<IActionResult> CrearMunicipio(
            MunicipioGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios, TipoPermisoApi.Agregar, cancellationToken);
            if (acceso != null) return acceso;

            string nombre = NormalizarNombre(dto.Nombre);

            bool padreActivo = await (
                from departamento in db.Departamento
                join pais in db.Pais
                    on departamento.PaisId equals pais.PaisId
                where departamento.DepartamentoId == dto.DepartamentoId
                      && departamento.Activo
                      && pais.Activo
                select departamento.DepartamentoId)
                .AnyAsync(cancellationToken);

            if (!padreActivo)
                return BadRequest(Error(
                    "Seleccione un departamento activo."));
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error(
                    "El nombre del municipio es obligatorio."));

            bool duplicado = await db.Municipios.AnyAsync(
                x => x.DepartamentoId == dto.DepartamentoId &&
                     x.Activo &&
                     x.NombreMunicipio == nombre,
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Ya existe ese municipio en el departamento seleccionado."));

            db.Municipios.Add(new Municipio
            {
                DepartamentoId = dto.DepartamentoId,
                NombreMunicipio = nombre,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito("Municipio creado correctamente."));
        }

        [HttpPut("municipios/{id:int}")]
        public async Task<IActionResult> ActualizarMunicipio(
            int id,
            MunicipioGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Municipio? entidad = await db.Municipios.FirstOrDefaultAsync(
                x => x.MunicipioId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El municipio no existe."));
            if (!entidad.Activo)
                return Conflict(Error(
                    "Reactive el municipio antes de editarlo."));

            string nombre = NormalizarNombre(dto.Nombre);
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            bool duplicado = await db.Municipios.AnyAsync(
                x => x.MunicipioId != id &&
                     x.DepartamentoId == entidad.DepartamentoId &&
                     x.Activo &&
                     x.NombreMunicipio == nombre,
                cancellationToken);

            if (duplicado)
                return Conflict(Error(
                    "Otro municipio utiliza ese nombre."));

            entidad.NombreMunicipio = nombre;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Municipio actualizado correctamente."));
        }

        [HttpDelete("municipios/{id:int}")]
        public async Task<IActionResult> DesactivarMunicipio(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios, TipoPermisoApi.Eliminar, cancellationToken);
            if (acceso != null) return acceso;

            Municipio? entidad = await db.Municipios.FirstOrDefaultAsync(
                x => x.MunicipioId == id && x.Activo,
                cancellationToken);

            if (entidad == null)
                return NotFound(Error(
                    "El municipio no existe o ya está inactivo."));

            bool tieneTerrenos = await db.Terreno.AnyAsync(
                x => x.municipioId == id && x.activo,
                cancellationToken);

            bool tieneUsuarios = await db.Usuarios.AnyAsync(
                x => x.municipioId == id && x.activo,
                cancellationToken);

            if (tieneTerrenos || tieneUsuarios)
            {
                return Conflict(Error(
                    "No puede desactivar el municipio mientras tenga terrenos o usuarios activos."));
            }

            entidad.Activo = false;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Municipio desactivado correctamente."));
        }

        [HttpPost("municipios/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarMunicipio(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios, TipoPermisoApi.Actualizar, cancellationToken);
            if (acceso != null) return acceso;

            Municipio? entidad = await db.Municipios.FirstOrDefaultAsync(
                x => x.MunicipioId == id, cancellationToken);

            if (entidad == null)
                return NotFound(Error("El municipio no existe."));

            bool padreActivo = await (
                from departamento in db.Departamento
                join pais in db.Pais
                    on departamento.PaisId equals pais.PaisId
                where departamento.DepartamentoId == entidad.DepartamentoId
                      && departamento.Activo
                      && pais.Activo
                select departamento.DepartamentoId)
                .AnyAsync(cancellationToken);

            if (!padreActivo)
                return Conflict(Error(
                    "Debe reactivar primero el país y el departamento relacionados."));

            entidad.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Ok(Exito(
                "Municipio reactivado correctamente."));
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                ObtenerUsuarioId(), interfaz, tipo, cancellationToken);

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
            new { success = false, message = mensaje };

        private static object Exito(string mensaje) =>
            new { success = true, message = mensaje };

        private static string NormalizarBusqueda(string? valor)
        {
            string texto = (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

            return texto.Length > 100 ? texto[..100] : texto;
        }

        private static string NormalizarNombre(string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarCodigo(string? valor) =>
            new string((valor ?? string.Empty)
                .Where(char.IsLetter)
                .Take(3)
                .ToArray())
                .ToUpperInvariant();

        public sealed class PaisGuardarDto
        {
            public string Nombre { get; set; } = string.Empty;
            public string CodigoIso { get; set; } = string.Empty;
        }

        public sealed class DepartamentoGuardarDto
        {
            public int PaisId { get; set; }
            public string Nombre { get; set; } = string.Empty;
        }

        public sealed class MunicipioGuardarDto
        {
            public int DepartamentoId { get; set; }
            public string Nombre { get; set; } = string.Empty;
        }

        public sealed class PaisAdminDto
        {
            public int PaisId { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public string CodigoIso { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadDependencias { get; set; }
        }

        public sealed class DepartamentoAdminDto
        {
            public int DepartamentoId { get; set; }
            public int PaisId { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadDependencias { get; set; }
        }

        public sealed class MunicipioAdminDto
        {
            public int MunicipioId { get; set; }
            public int DepartamentoId { get; set; }
            public string Nombre { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadTerrenos { get; set; }
            public int CantidadUsuarios { get; set; }
        }

        public sealed class PaginaRespuesta<T>
        {
            public List<T> Items { get; set; } = [];
            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }

            public static PaginaRespuesta<T> Crear(
                List<T> items,
                int pagina,
                int tamanoPagina,
                int total) =>
                new()
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = total,
                    TotalPaginas = total == 0
                        ? 1
                        : (int)Math.Ceiling(
                            total / (double)tamanoPagina)
                };
        }
    }
}
