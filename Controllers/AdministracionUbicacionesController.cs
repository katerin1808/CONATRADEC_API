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

        private const string CodigoPaisInactivo =
            "PAIS_INACTIVO_EXISTENTE";

        private const string CodigoDepartamentoInactivo =
            "DEPARTAMENTO_INACTIVO_EXISTENTE";

        private const string CodigoMunicipioInactivo =
            "MUNICIPIO_INACTIVO_EXISTENTE";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public AdministracionUbicacionesController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        // ==========================================================
        // PAÍSES
        // ==========================================================

        [HttpGet("paises")]
        public async Task<IActionResult> ListarPaises(
            string? buscar = null,
            int pagina = 1,
            int tamanoPagina = 20,
            bool incluirInactivos = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

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
                items,
                pagina,
                tamanoPagina,
                total));
        }

        [HttpPost("paises")]
        public async Task<IActionResult> CrearPais(
            [FromBody] PaisGuardarDto dto,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string nombre = NormalizarNombre(dto.Nombre);
            string codigo = NormalizarCodigo(dto.CodigoIso);

            IActionResult? validacion =
                ValidarPais(nombre, codigo);

            if (validacion != null)
                return validacion;

            bool duplicadoActivo = await db.Pais
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Activo &&
                        (x.NombrePais == nombre ||
                         x.CodigoISOPais == codigo),
                    cancellationToken);

            if (duplicadoActivo)
            {
                return Conflict(Error(
                    "Ya existe un país activo con el mismo nombre o código ISO."));
            }

            if (!crearNuevoSiExisteInactivo)
            {
                List<PaisAdminDto> coincidencias = await db.Pais
                    .AsNoTracking()
                    .Where(x =>
                        !x.Activo &&
                        (x.NombrePais == nombre ||
                         x.CodigoISOPais == codigo))
                    .OrderBy(x => x.PaisId)
                    .Take(2)
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

                if (coincidencias.Count > 1)
                {
                    return Conflict(Error(
                        "Los datos coinciden con más de un país inactivo. Reactívelo manualmente desde Países eliminados."));
                }

                if (coincidencias.Count == 1)
                {
                    return Conflict(Conflicto(
                        CodigoPaisInactivo,
                        "Ya existe un país inactivo con el mismo nombre o código ISO.",
                        coincidencias[0]));
                }
            }

            var entidad = new Pais
            {
                NombrePais = nombre,
                CodigoISOPais = codigo,
                Activo = true
            };

            db.Pais.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            PaisAdminDto data =
                await ObtenerPaisAsync(
                    entidad.PaisId,
                    cancellationToken);

            return Ok(Exito(
                "País creado correctamente.",
                data));
        }

        [HttpPut("paises/{id:int}")]
        public async Task<IActionResult> ActualizarPais(
            int id,
            [FromBody] PaisGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id,
                cancellationToken);

            if (entidad == null)
                return NotFound(Error("El país no existe."));

            if (!entidad.Activo)
            {
                return Conflict(Error(
                    "Reactive el país antes de editarlo."));
            }

            string nombre = NormalizarNombre(dto.Nombre);
            string codigo = NormalizarCodigo(dto.CodigoIso);

            IActionResult? validacion =
                ValidarPais(nombre, codigo);

            if (validacion != null)
                return validacion;

            bool duplicado = await db.Pais
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.PaisId != id &&
                        x.Activo &&
                        (x.NombrePais == nombre ||
                         x.CodigoISOPais == codigo),
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "Otro país utiliza el mismo nombre o código ISO."));
            }

            entidad.NombrePais = nombre;
            entidad.CodigoISOPais = codigo;

            await db.SaveChangesAsync(cancellationToken);

            PaisAdminDto data =
                await ObtenerPaisAsync(id, cancellationToken);

            return Ok(Exito(
                "País actualizado correctamente.",
                data));
        }

        [HttpDelete("paises/{id:int}")]
        public async Task<IActionResult> DesactivarPais(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id && x.Activo,
                cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "El país no existe o ya está inactivo."));
            }

            bool tieneDependencias = await db.Departamento
                .AsNoTracking()
                .AnyAsync(
                    x => x.PaisId == id && x.Activo,
                    cancellationToken);

            if (tieneDependencias)
            {
                return Conflict(Error(
                    "No puede desactivar el país mientras tenga departamentos activos."));
            }

            entidad.Activo = false;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "País desactivado correctamente."));
        }

        /// <summary>
        /// Ruta conservada para consumidores que ya utilizan la reactivación
        /// administrativa sin enviar datos del formulario.
        /// </summary>
        [HttpPost("paises/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarPais(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarPaisInternoAsync(
                id,
                null,
                cancellationToken);
        }

        /// <summary>
        /// Reactiva el país conservando su identificador e historial y permite
        /// aplicar los datos escritos en el formulario actual.
        /// </summary>
        [HttpPut("paises/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarPaisConDatos(
            int id,
            [FromBody] PaisGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoPaises,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarPaisInternoAsync(
                id,
                dto,
                cancellationToken);
        }

        // ==========================================================
        // DEPARTAMENTOS
        // ==========================================================

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
                PermisoDepartamentos,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (paisId <= 0)
                return BadRequest(Error("Debe seleccionar un país."));

            string? nombrePais = await db.Pais
                .AsNoTracking()
                .Where(x => x.PaisId == paisId)
                .Select(x => x.NombrePais)
                .SingleOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(nombrePais))
                return NotFound(Error("El país indicado no existe."));

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Departamento> query = db.Departamento
                .AsNoTracking()
                .Where(x => x.PaisId == paisId);

            if (!incluirInactivos)
                query = query.Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.NombreDepartamento.Contains(texto));
            }

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
                    NombrePais = nombrePais,
                    Nombre = x.NombreDepartamento,
                    Activo = x.Activo,
                    CantidadDependencias =
                        x.Municipios.Count(y => y.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(PaginaRespuesta<DepartamentoAdminDto>.Crear(
                items,
                pagina,
                tamanoPagina,
                total));
        }

        [HttpPost("departamentos")]
        public async Task<IActionResult> CrearDepartamento(
            [FromBody] DepartamentoGuardarDto dto,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto.PaisId <= 0)
                return BadRequest(Error("Seleccione un país válido."));

            string nombre = NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(Error(
                    "El nombre del departamento es obligatorio."));
            }

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del departamento no puede superar 80 caracteres."));
            }

            bool padreActivo = await db.Pais
                .AsNoTracking()
                .AnyAsync(
                    x => x.PaisId == dto.PaisId && x.Activo,
                    cancellationToken);

            if (!padreActivo)
                return BadRequest(Error("Seleccione un país activo."));

            bool duplicadoActivo = await db.Departamento
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.PaisId == dto.PaisId &&
                        x.Activo &&
                        x.NombreDepartamento == nombre,
                    cancellationToken);

            if (duplicadoActivo)
            {
                return Conflict(Error(
                    "Ya existe ese departamento en el país seleccionado."));
            }

            if (!crearNuevoSiExisteInactivo)
            {
                List<DepartamentoAdminDto> coincidencias =
                    await db.Departamento
                        .AsNoTracking()
                        .Where(x =>
                            x.PaisId == dto.PaisId &&
                            !x.Activo &&
                            x.NombreDepartamento == nombre)
                        .OrderBy(x => x.DepartamentoId)
                        .Take(2)
                        .Select(x => new DepartamentoAdminDto
                        {
                            DepartamentoId = x.DepartamentoId,
                            PaisId = x.PaisId,
                            NombrePais = x.Pais!.NombrePais,
                            Nombre = x.NombreDepartamento,
                            Activo = x.Activo,
                            CantidadDependencias =
                                x.Municipios.Count(y => y.Activo)
                        })
                        .ToListAsync(cancellationToken);

                if (coincidencias.Count > 1)
                {
                    return Conflict(Error(
                        "Los datos coinciden con más de un departamento inactivo. Reactívelo manualmente desde Departamentos eliminados."));
                }

                if (coincidencias.Count == 1)
                {
                    return Conflict(Conflicto(
                        CodigoDepartamentoInactivo,
                        "Ya existe un departamento inactivo con ese nombre en el país seleccionado.",
                        coincidencias[0]));
                }
            }

            var entidad = new Departamento
            {
                PaisId = dto.PaisId,
                NombreDepartamento = nombre,
                Activo = true
            };

            db.Departamento.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            DepartamentoAdminDto data =
                await ObtenerDepartamentoAsync(
                    entidad.DepartamentoId,
                    cancellationToken);

            return Ok(Exito(
                "Departamento creado correctamente.",
                data));
        }

        [HttpPut("departamentos/{id:int}")]
        public async Task<IActionResult> ActualizarDepartamento(
            int id,
            [FromBody] DepartamentoGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Departamento? entidad = await db.Departamento
                .FirstOrDefaultAsync(
                    x => x.DepartamentoId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error("El departamento no existe."));

            if (!entidad.Activo)
            {
                return Conflict(Error(
                    "Reactive el departamento antes de editarlo."));
            }

            if (dto.PaisId > 0 && dto.PaisId != entidad.PaisId)
            {
                return BadRequest(Error(
                    "El departamento no puede cambiar de país desde este formulario."));
            }

            string nombre = NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del departamento no puede superar 80 caracteres."));
            }

            bool duplicado = await db.Departamento
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.DepartamentoId != id &&
                        x.PaisId == entidad.PaisId &&
                        x.Activo &&
                        x.NombreDepartamento == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "Otro departamento utiliza ese nombre."));
            }

            entidad.NombreDepartamento = nombre;
            await db.SaveChangesAsync(cancellationToken);

            DepartamentoAdminDto data =
                await ObtenerDepartamentoAsync(id, cancellationToken);

            return Ok(Exito(
                "Departamento actualizado correctamente.",
                data));
        }

        [HttpDelete("departamentos/{id:int}")]
        public async Task<IActionResult> DesactivarDepartamento(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Departamento? entidad = await db.Departamento
                .FirstOrDefaultAsync(
                    x => x.DepartamentoId == id && x.Activo,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "El departamento no existe o ya está inactivo."));
            }

            bool tieneDependencias = await db.Municipios
                .AsNoTracking()
                .AnyAsync(
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
                PermisoDepartamentos,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarDepartamentoInternoAsync(
                id,
                null,
                cancellationToken);
        }

        [HttpPut("departamentos/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarDepartamentoConDatos(
            int id,
            [FromBody] DepartamentoGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoDepartamentos,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarDepartamentoInternoAsync(
                id,
                dto,
                cancellationToken);
        }

        // ==========================================================
        // MUNICIPIOS
        // ==========================================================

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
                PermisoMunicipios,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (departamentoId <= 0)
            {
                return BadRequest(Error(
                    "Debe seleccionar un departamento."));
            }

            var ubicacion = await (
                from departamento in db.Departamento.AsNoTracking()
                join pais in db.Pais.AsNoTracking()
                    on departamento.PaisId equals pais.PaisId
                where departamento.DepartamentoId == departamentoId
                select new
                {
                    departamento.DepartamentoId,
                    departamento.NombreDepartamento,
                    departamento.PaisId,
                    pais.NombrePais
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (ubicacion == null)
            {
                return NotFound(Error(
                    "El departamento indicado no existe."));
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = NormalizarBusqueda(buscar);

            IQueryable<Municipio> query = db.Municipios
                .AsNoTracking()
                .Where(x => x.DepartamentoId == departamentoId);

            if (!incluirInactivos)
                query = query.Where(x => x.Activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.NombreMunicipio.Contains(texto));
            }

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
                    NombreDepartamento = ubicacion.NombreDepartamento,
                    PaisId = ubicacion.PaisId,
                    NombrePais = ubicacion.NombrePais,
                    Nombre = x.NombreMunicipio,
                    Activo = x.Activo,
                    CantidadTerrenos = db.Terreno.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo),
                    CantidadUsuarios = db.Usuarios.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(PaginaRespuesta<MunicipioAdminDto>.Crear(
                items,
                pagina,
                tamanoPagina,
                total));
        }

        [HttpPost("municipios")]
        public async Task<IActionResult> CrearMunicipio(
            [FromBody] MunicipioGuardarDto dto,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto.DepartamentoId <= 0)
            {
                return BadRequest(Error(
                    "Seleccione un departamento válido."));
            }

            string nombre = NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(Error(
                    "El nombre del municipio es obligatorio."));
            }

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del municipio no puede superar 80 caracteres."));
            }

            bool padreActivo = await (
                from departamento in db.Departamento.AsNoTracking()
                join pais in db.Pais.AsNoTracking()
                    on departamento.PaisId equals pais.PaisId
                where departamento.DepartamentoId == dto.DepartamentoId
                      && departamento.Activo
                      && pais.Activo
                select departamento.DepartamentoId)
                .AnyAsync(cancellationToken);

            if (!padreActivo)
            {
                return BadRequest(Error(
                    "Seleccione un departamento activo."));
            }

            bool duplicadoActivo = await db.Municipios
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.DepartamentoId == dto.DepartamentoId &&
                        x.Activo &&
                        x.NombreMunicipio == nombre,
                    cancellationToken);

            if (duplicadoActivo)
            {
                return Conflict(Error(
                    "Ya existe ese municipio en el departamento seleccionado."));
            }

            if (!crearNuevoSiExisteInactivo)
            {
                List<MunicipioAdminDto> coincidencias =
                    await db.Municipios
                        .AsNoTracking()
                        .Where(x =>
                            x.DepartamentoId == dto.DepartamentoId &&
                            !x.Activo &&
                            x.NombreMunicipio == nombre)
                        .OrderBy(x => x.MunicipioId)
                        .Take(2)
                        .Select(x => new MunicipioAdminDto
                        {
                            MunicipioId = x.MunicipioId,
                            DepartamentoId = x.DepartamentoId,
                            NombreDepartamento =
                                x.Departamento!.NombreDepartamento,
                            PaisId = x.Departamento.PaisId,
                            NombrePais = x.Departamento.Pais!.NombrePais,
                            Nombre = x.NombreMunicipio,
                            Activo = x.Activo,
                            CantidadTerrenos = db.Terreno.Count(y =>
                                y.municipioId == x.MunicipioId && y.activo),
                            CantidadUsuarios = db.Usuarios.Count(y =>
                                y.municipioId == x.MunicipioId && y.activo)
                        })
                        .ToListAsync(cancellationToken);

                if (coincidencias.Count > 1)
                {
                    return Conflict(Error(
                        "Los datos coinciden con más de un municipio inactivo. Reactívelo manualmente desde Municipios eliminados."));
                }

                if (coincidencias.Count == 1)
                {
                    return Conflict(Conflicto(
                        CodigoMunicipioInactivo,
                        "Ya existe un municipio inactivo con ese nombre en el departamento seleccionado.",
                        coincidencias[0]));
                }
            }

            var entidad = new Municipio
            {
                DepartamentoId = dto.DepartamentoId,
                NombreMunicipio = nombre,
                Activo = true
            };

            db.Municipios.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            MunicipioAdminDto data =
                await ObtenerMunicipioAsync(
                    entidad.MunicipioId,
                    cancellationToken);

            return Ok(Exito(
                "Municipio creado correctamente.",
                data));
        }

        [HttpPut("municipios/{id:int}")]
        public async Task<IActionResult> ActualizarMunicipio(
            int id,
            [FromBody] MunicipioGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Municipio? entidad = await db.Municipios
                .FirstOrDefaultAsync(
                    x => x.MunicipioId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error("El municipio no existe."));

            if (!entidad.Activo)
            {
                return Conflict(Error(
                    "Reactive el municipio antes de editarlo."));
            }

            if (dto.DepartamentoId > 0 &&
                dto.DepartamentoId != entidad.DepartamentoId)
            {
                return BadRequest(Error(
                    "El municipio no puede cambiar de departamento desde este formulario."));
            }

            string nombre = NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del municipio no puede superar 80 caracteres."));
            }

            bool duplicado = await db.Municipios
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.MunicipioId != id &&
                        x.DepartamentoId == entidad.DepartamentoId &&
                        x.Activo &&
                        x.NombreMunicipio == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "Otro municipio utiliza ese nombre."));
            }

            entidad.NombreMunicipio = nombre;
            await db.SaveChangesAsync(cancellationToken);

            MunicipioAdminDto data =
                await ObtenerMunicipioAsync(id, cancellationToken);

            return Ok(Exito(
                "Municipio actualizado correctamente.",
                data));
        }

        [HttpDelete("municipios/{id:int}")]
        public async Task<IActionResult> DesactivarMunicipio(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Municipio? entidad = await db.Municipios
                .FirstOrDefaultAsync(
                    x => x.MunicipioId == id && x.Activo,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "El municipio no existe o ya está inactivo."));
            }

            bool tieneTerrenos = await db.Terreno
                .AsNoTracking()
                .AnyAsync(
                    x => x.municipioId == id && x.activo,
                    cancellationToken);

            bool tieneUsuarios = await db.Usuarios
                .AsNoTracking()
                .AnyAsync(
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
                PermisoMunicipios,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarMunicipioInternoAsync(
                id,
                null,
                cancellationToken);
        }

        [HttpPut("municipios/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarMunicipioConDatos(
            int id,
            [FromBody] MunicipioGuardarDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarAccesoAsync(
                PermisoMunicipios,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return await ReactivarMunicipioInternoAsync(
                id,
                dto,
                cancellationToken);
        }

        // ==========================================================
        // REACTIVACIÓN INTERNA
        // ==========================================================

        private async Task<IActionResult> ReactivarPaisInternoAsync(
            int id,
            PaisGuardarDto? dto,
            CancellationToken cancellationToken)
        {
            Pais? entidad = await db.Pais.FirstOrDefaultAsync(
                x => x.PaisId == id,
                cancellationToken);

            if (entidad == null)
                return NotFound(Error("El país no existe."));

            if (entidad.Activo)
                return Conflict(Error("El país ya se encuentra activo."));

            string nombre = dto == null
                ? NormalizarNombre(entidad.NombrePais)
                : NormalizarNombre(dto.Nombre);

            string codigo = dto == null
                ? NormalizarCodigo(entidad.CodigoISOPais)
                : NormalizarCodigo(dto.CodigoIso);

            IActionResult? validacion =
                ValidarPais(nombre, codigo);

            if (validacion != null)
                return validacion;

            bool duplicado = await db.Pais
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.PaisId != id &&
                        x.Activo &&
                        (x.NombrePais == nombre ||
                         x.CodigoISOPais == codigo),
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "No se puede reactivar porque otro país activo utiliza el mismo nombre o código ISO."));
            }

            entidad.NombrePais = nombre;
            entidad.CodigoISOPais = codigo;
            entidad.Activo = true;

            await db.SaveChangesAsync(cancellationToken);

            PaisAdminDto data =
                await ObtenerPaisAsync(id, cancellationToken);

            return Ok(Exito(
                "País reactivado correctamente.",
                data));
        }

        private async Task<IActionResult> ReactivarDepartamentoInternoAsync(
            int id,
            DepartamentoGuardarDto? dto,
            CancellationToken cancellationToken)
        {
            Departamento? entidad = await db.Departamento
                .FirstOrDefaultAsync(
                    x => x.DepartamentoId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error("El departamento no existe."));

            if (entidad.Activo)
            {
                return Conflict(Error(
                    "El departamento ya se encuentra activo."));
            }

            if (dto?.PaisId is > 0 && dto.PaisId != entidad.PaisId)
            {
                return BadRequest(Error(
                    "El departamento no puede cambiar de país durante la reactivación."));
            }

            bool padreActivo = await db.Pais
                .AsNoTracking()
                .AnyAsync(
                    x => x.PaisId == entidad.PaisId && x.Activo,
                    cancellationToken);

            if (!padreActivo)
            {
                return Conflict(Error(
                    "Debe reactivar primero el país relacionado."));
            }

            string nombre = dto == null
                ? NormalizarNombre(entidad.NombreDepartamento)
                : NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del departamento no puede superar 80 caracteres."));
            }

            bool duplicado = await db.Departamento
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.DepartamentoId != id &&
                        x.PaisId == entidad.PaisId &&
                        x.Activo &&
                        x.NombreDepartamento == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "No se puede reactivar porque otro departamento activo utiliza ese nombre."));
            }

            entidad.NombreDepartamento = nombre;
            entidad.Activo = true;

            await db.SaveChangesAsync(cancellationToken);

            DepartamentoAdminDto data =
                await ObtenerDepartamentoAsync(id, cancellationToken);

            return Ok(Exito(
                "Departamento reactivado correctamente.",
                data));
        }

        private async Task<IActionResult> ReactivarMunicipioInternoAsync(
            int id,
            MunicipioGuardarDto? dto,
            CancellationToken cancellationToken)
        {
            Municipio? entidad = await db.Municipios
                .FirstOrDefaultAsync(
                    x => x.MunicipioId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(Error("El municipio no existe."));

            if (entidad.Activo)
                return Conflict(Error("El municipio ya se encuentra activo."));

            if (dto?.DepartamentoId is > 0 &&
                dto.DepartamentoId != entidad.DepartamentoId)
            {
                return BadRequest(Error(
                    "El municipio no puede cambiar de departamento durante la reactivación."));
            }

            bool padreActivo = await (
                from departamento in db.Departamento.AsNoTracking()
                join pais in db.Pais.AsNoTracking()
                    on departamento.PaisId equals pais.PaisId
                where departamento.DepartamentoId == entidad.DepartamentoId
                      && departamento.Activo
                      && pais.Activo
                select departamento.DepartamentoId)
                .AnyAsync(cancellationToken);

            if (!padreActivo)
            {
                return Conflict(Error(
                    "Debe reactivar primero el país y el departamento relacionados."));
            }

            string nombre = dto == null
                ? NormalizarNombre(entidad.NombreMunicipio)
                : NormalizarNombre(dto.Nombre);

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre es obligatorio."));

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del municipio no puede superar 80 caracteres."));
            }

            bool duplicado = await db.Municipios
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.MunicipioId != id &&
                        x.DepartamentoId == entidad.DepartamentoId &&
                        x.Activo &&
                        x.NombreMunicipio == nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(Error(
                    "No se puede reactivar porque otro municipio activo utiliza ese nombre."));
            }

            entidad.NombreMunicipio = nombre;
            entidad.Activo = true;

            await db.SaveChangesAsync(cancellationToken);

            MunicipioAdminDto data =
                await ObtenerMunicipioAsync(id, cancellationToken);

            return Ok(Exito(
                "Municipio reactivado correctamente.",
                data));
        }

        // ==========================================================
        // PROYECCIONES DE RESPUESTA
        // ==========================================================

        private async Task<PaisAdminDto> ObtenerPaisAsync(
            int id,
            CancellationToken cancellationToken) =>
            await db.Pais
                .AsNoTracking()
                .Where(x => x.PaisId == id)
                .Select(x => new PaisAdminDto
                {
                    PaisId = x.PaisId,
                    Nombre = x.NombrePais,
                    CodigoIso = x.CodigoISOPais,
                    Activo = x.Activo,
                    CantidadDependencias =
                        x.Departamentos.Count(y => y.Activo)
                })
                .SingleAsync(cancellationToken);

        private async Task<DepartamentoAdminDto> ObtenerDepartamentoAsync(
            int id,
            CancellationToken cancellationToken) =>
            await db.Departamento
                .AsNoTracking()
                .Where(x => x.DepartamentoId == id)
                .Select(x => new DepartamentoAdminDto
                {
                    DepartamentoId = x.DepartamentoId,
                    PaisId = x.PaisId,
                    NombrePais = x.Pais!.NombrePais,
                    Nombre = x.NombreDepartamento,
                    Activo = x.Activo,
                    CantidadDependencias =
                        x.Municipios.Count(y => y.Activo)
                })
                .SingleAsync(cancellationToken);

        private async Task<MunicipioAdminDto> ObtenerMunicipioAsync(
            int id,
            CancellationToken cancellationToken) =>
            await db.Municipios
                .AsNoTracking()
                .Where(x => x.MunicipioId == id)
                .Select(x => new MunicipioAdminDto
                {
                    MunicipioId = x.MunicipioId,
                    DepartamentoId = x.DepartamentoId,
                    NombreDepartamento =
                        x.Departamento!.NombreDepartamento,
                    PaisId = x.Departamento.PaisId,
                    NombrePais = x.Departamento.Pais!.NombrePais,
                    Nombre = x.NombreMunicipio,
                    Activo = x.Activo,
                    CantidadTerrenos = db.Terreno.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo),
                    CantidadUsuarios = db.Usuarios.Count(y =>
                        y.municipioId == x.MunicipioId && y.activo)
                })
                .SingleAsync(cancellationToken);

        // ==========================================================
        // SEGURIDAD Y UTILIDADES
        // ==========================================================

        private async Task<IActionResult?> ValidarAccesoAsync(
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
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

        private IActionResult? ValidarPais(
            string nombre,
            string codigo)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest(Error("El nombre del país es obligatorio."));

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del país no puede superar 80 caracteres."));
            }

            if (codigo.Length != 3)
            {
                return BadRequest(Error(
                    "El código ISO debe contener exactamente tres letras."));
            }

            return null;
        }

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };

        private static object Conflicto<T>(
            string codigo,
            string mensaje,
            T data) =>
            new
            {
                success = false,
                code = codigo,
                message = mensaje,
                data
            };

        private static object Exito(string mensaje) =>
            new
            {
                success = true,
                message = mensaje
            };

        private static object Exito<T>(
            string mensaje,
            T data) =>
            new
            {
                success = true,
                message = mensaje,
                data
            };

        private static string NormalizarBusqueda(string? valor)
        {
            string texto = (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

            return texto.Length > 100
                ? texto[..100]
                : texto;
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
            public string NombrePais { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadDependencias { get; set; }
        }

        public sealed class MunicipioAdminDto
        {
            public int MunicipioId { get; set; }
            public int DepartamentoId { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
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
