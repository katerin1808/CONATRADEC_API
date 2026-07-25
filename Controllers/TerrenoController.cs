using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using static CONATRADEC_API.DTOs.TerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/terreno")]
    public class TerrenoController : ControllerBase
    {
        private readonly DBContext _db;

        private static readonly Regex CedulaRegex = new(
            @"^\d{3}-\d{6}-\d{4}[A-Z]$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        public TerrenoController(DBContext db)
        {
            _db = db;
        }

        // ============================================================
        // LISTAR TERRENOS
        // Se conserva por compatibilidad. Las pantallas nuevas deben usar
        // /buscar para no descargar toda la tabla en dispositivos móviles.
        // ============================================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<TerrenoListarDto>>> Listar(
            CancellationToken cancellationToken)
        {
            List<TerrenoListarDto> lista = await _db.Terreno
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.codigoTerreno)
                .Select(x => new TerrenoListarDto
                {
                    terrenoId = x.terrenoId,
                    codigoTerreno = x.codigoTerreno,
                    identificacionPropietarioTerreno =
                        x.identificacionPropietarioTerreno,
                    nombrePropietarioTerreno = x.nombrePropietarioTerreno,
                    telefonoPropietario = x.telefonoPropietario,
                    correoPropietario = x.correoPropietario,
                    direccionTerreno = x.direccionTerreno,
                    extensionManzanaTerreno = x.extensionManzanaTerreno,
                    fechaIngresoTerreno = x.fechaIngresoTerreno,
                    municipioId = x.municipioId,
                    cantidadQuintalesOro = x.cantidadQuintalesOro,
                    cantidadPlantasTerreno = x.cantidadPlantasTerreno,
                    latitud = x.latitud,
                    longitud = x.longitud,
                    ubicacion = new TerrenoUbicacionDto
                    {
                        paisId = x.Municipio.Departamento.Pais.PaisId,
                        nombrePais = x.Municipio.Departamento.Pais.NombrePais,
                        departamentoId =
                            x.Municipio.Departamento.DepartamentoId,
                        nombreDepartamento =
                            x.Municipio.Departamento.NombreDepartamento,
                        municipioId = x.Municipio.MunicipioId,
                        nombreMunicipio = x.Municipio.NombreMunicipio
                    }
                })
                .ToListAsync(cancellationToken);

            return Ok(lista);
        }

        // ============================================================
        // CREAR TERRENO
        // El código nunca se recibe del cliente. Primero se obtiene el
        // terrenoId de SQL Server y luego se genera:
        // TRR-{municipioId:0000}-{terrenoId:000000}
        // ============================================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear(
            [FromBody] TerrenoCrearDto dto,
            CancellationToken cancellationToken)
        {
            string? errorValidacion = ValidarDatosTerreno(
                dto.identificacionPropietarioTerreno,
                dto.nombrePropietarioTerreno,
                dto.direccionTerreno,
                dto.extensionManzanaTerreno,
                dto.cantidadQuintalesOro,
                dto.cantidadPlantasTerreno,
                dto.telefonoPropietario,
                dto.municipioId,
                dto.latitud,
                dto.longitud);

            if (errorValidacion != null)
                return BadRequest(new { mensaje = errorValidacion });

            if (!await MunicipioActivoExisteAsync(
                    dto.municipioId,
                    cancellationToken))
            {
                return BadRequest(new
                {
                    mensaje = "El municipio seleccionado no existe o está inactivo."
                });
            }

            await using var transaccion =
                await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var terreno = new Terreno
                {
                    // Valor temporal irrepetible. Se reemplaza dentro de la
                    // misma transacción después de obtener el terrenoId.
                    codigoTerreno = $"TMP-{Guid.NewGuid():N}",
                    identificacionPropietarioTerreno =
                        NormalizarCedula(dto.identificacionPropietarioTerreno),
                    nombrePropietarioTerreno =
                        dto.nombrePropietarioTerreno.Trim(),
                    telefonoPropietario = dto.telefonoPropietario,
                    correoPropietario = NormalizarOpcional(dto.correoPropietario),
                    direccionTerreno = dto.direccionTerreno.Trim(),
                    extensionManzanaTerreno =
                        decimal.Round(dto.extensionManzanaTerreno, 2),
                    // Fecha interna de control. No se toma del cliente.
                    fechaIngresoTerreno =
                        DateOnly.FromDateTime(DateTime.Now),
                    municipioId = dto.municipioId,
                    cantidadQuintalesOro =
                        decimal.Round(dto.cantidadQuintalesOro, 2),
                    cantidadPlantasTerreno = dto.cantidadPlantasTerreno,
                    latitud = dto.latitud,
                    longitud = dto.longitud,
                    activo = true
                };

                _db.Terreno.Add(terreno);
                await _db.SaveChangesAsync(cancellationToken);

                terreno.codigoTerreno = GenerarCodigoTerreno(
                    terreno.municipioId,
                    terreno.terrenoId);

                await _db.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    mensaje = "Terreno creado correctamente.",
                    data = new
                    {
                        terreno.terrenoId,
                        terreno.codigoTerreno
                    }
                });
            }
            catch (DbUpdateException ex) when (EsConflictoUnico(ex))
            {
                await transaccion.RollbackAsync(CancellationToken.None);

                return Conflict(new
                {
                    mensaje =
                        "No fue posible generar un código único para el terreno. Intente guardar nuevamente."
                });
            }
            catch (OperationCanceledException)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    mensaje =
                        "Ocurrió un error al crear el terreno. Intente nuevamente."
                });
            }
        }

        // ============================================================
        // EDITAR TERRENO
        // El código no se modifica aunque un cliente antiguo lo envíe.
        // ============================================================
        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(
            int id,
            [FromBody] TerrenoEditarDto dto,
            CancellationToken cancellationToken)
        {
            string? errorValidacion = ValidarDatosTerreno(
                dto.identificacionPropietarioTerreno,
                dto.nombrePropietarioTerreno,
                dto.direccionTerreno,
                dto.extensionManzanaTerreno,
                dto.cantidadQuintalesOro,
                dto.cantidadPlantasTerreno,
                dto.telefonoPropietario,
                dto.municipioId,
                dto.latitud,
                dto.longitud);

            if (errorValidacion != null)
                return BadRequest(new { mensaje = errorValidacion });

            Terreno? terreno = await _db.Terreno
                .FirstOrDefaultAsync(
                    x => x.terrenoId == id && x.activo,
                    cancellationToken);

            if (terreno == null)
            {
                return NotFound(new
                {
                    mensaje = "Terreno no encontrado o inactivo."
                });
            }

            if (!await MunicipioActivoExisteAsync(
                    dto.municipioId,
                    cancellationToken))
            {
                return BadRequest(new
                {
                    mensaje = "El municipio seleccionado no existe o está inactivo."
                });
            }

            // terreno.codigoTerreno permanece intacto deliberadamente.
            terreno.identificacionPropietarioTerreno =
                NormalizarCedula(dto.identificacionPropietarioTerreno);
            terreno.nombrePropietarioTerreno =
                dto.nombrePropietarioTerreno.Trim();
            terreno.telefonoPropietario = dto.telefonoPropietario;
            terreno.correoPropietario = NormalizarOpcional(dto.correoPropietario);
            terreno.direccionTerreno = dto.direccionTerreno.Trim();
            terreno.extensionManzanaTerreno =
                decimal.Round(dto.extensionManzanaTerreno, 2);
            // La fecha de ingreso es inmutable y se conserva tal como fue
            // registrada al crear el terreno.
            terreno.municipioId = dto.municipioId;
            terreno.cantidadQuintalesOro =
                decimal.Round(dto.cantidadQuintalesOro, 2);
            terreno.cantidadPlantasTerreno = dto.cantidadPlantasTerreno;
            terreno.latitud = dto.latitud;
            terreno.longitud = dto.longitud;

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje = "Terreno editado correctamente.",
                data = new
                {
                    terreno.terrenoId,
                    terreno.codigoTerreno
                }
            });
        }

        // ============================================================
        // ELIMINAR LÓGICAMENTE
        // ============================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            Terreno? terreno = await _db.Terreno
                .FirstOrDefaultAsync(
                    x => x.terrenoId == id && x.activo,
                    cancellationToken);

            if (terreno == null)
            {
                return NotFound(new
                {
                    mensaje = "Terreno no encontrado o ya está desactivado."
                });
            }

            terreno.activo = false;
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje = "Terreno eliminado correctamente.",
                data = new
                {
                    terreno.terrenoId,
                    terreno.codigoTerreno,
                    terreno.activo
                }
            });
        }

        // ============================================================
        // BÚSQUEDA AVANZADA PAGINADA
        // ============================================================
        [HttpGet("buscar")]
        public async Task<IActionResult> Buscar(
            string? texto,
            string? codigoTerreno,
            string? nombrePropietario,
            string? identificacionPropietario,
            string? direccion,
            int? paisId,
            int? departamentoId,
            int? municipioId,
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima,
            string? ordenarPor = "codigo",
            bool descendente = false,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            string? errorFiltros = ValidarFiltrosBusqueda(
                fechaDesde,
                fechaHasta,
                extensionMinima,
                extensionMaxima);

            if (errorFiltros != null)
                return BadRequest(new { mensaje = errorFiltros });

            IQueryable<Terreno> query = _db.Terreno
                .AsNoTracking()
                .Where(x => x.activo);

            texto = NormalizarFiltro(texto);
            codigoTerreno = NormalizarFiltro(codigoTerreno);
            nombrePropietario = NormalizarFiltro(nombrePropietario);
            identificacionPropietario =
                NormalizarFiltro(identificacionPropietario);
            direccion = NormalizarFiltro(direccion);

            if (texto != null)
            {
                query = query.Where(x =>
                    x.codigoTerreno.Contains(texto) ||
                    x.nombrePropietarioTerreno.Contains(texto) ||
                    x.identificacionPropietarioTerreno.Contains(texto) ||
                    x.direccionTerreno.Contains(texto));
            }

            if (codigoTerreno != null)
            {
                query = query.Where(x =>
                    x.codigoTerreno.Contains(codigoTerreno));
            }

            if (nombrePropietario != null)
            {
                query = query.Where(x =>
                    x.nombrePropietarioTerreno.Contains(nombrePropietario));
            }

            if (identificacionPropietario != null)
            {
                query = query.Where(x =>
                    x.identificacionPropietarioTerreno
                        .Contains(identificacionPropietario));
            }

            if (direccion != null)
            {
                query = query.Where(x =>
                    x.direccionTerreno.Contains(direccion));
            }

            if (paisId is > 0)
            {
                query = query.Where(x =>
                    x.Municipio.Departamento.PaisId == paisId.Value);
            }

            if (departamentoId is > 0)
            {
                query = query.Where(x =>
                    x.Municipio.DepartamentoId == departamentoId.Value);
            }

            if (municipioId is > 0)
            {
                query = query.Where(x =>
                    x.municipioId == municipioId.Value);
            }

            if (fechaDesde.HasValue)
            {
                query = query.Where(x =>
                    x.fechaIngresoTerreno >= fechaDesde.Value);
            }

            if (fechaHasta.HasValue)
            {
                query = query.Where(x =>
                    x.fechaIngresoTerreno <= fechaHasta.Value);
            }

            if (extensionMinima.HasValue)
            {
                query = query.Where(x =>
                    x.extensionManzanaTerreno >= extensionMinima.Value);
            }

            if (extensionMaxima.HasValue)
            {
                query = query.Where(x =>
                    x.extensionManzanaTerreno <= extensionMaxima.Value);
            }

            query = AplicarOrdenamiento(
                query,
                ordenarPor,
                descendente);

            int total = await query.CountAsync(cancellationToken);

            var data = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.terrenoId,
                    x.codigoTerreno,
                    x.identificacionPropietarioTerreno,
                    x.nombrePropietarioTerreno,
                    x.telefonoPropietario,
                    x.correoPropietario,
                    x.direccionTerreno,
                    x.extensionManzanaTerreno,
                    x.fechaIngresoTerreno,
                    x.cantidadPlantasTerreno,
                    x.cantidadQuintalesOro,
                    x.latitud,
                    x.longitud,
                    x.municipioId,
                    ubicacion = new
                    {
                        paisId = x.Municipio.Departamento.Pais.PaisId,
                        nombrePais = x.Municipio.Departamento.Pais.NombrePais,
                        departamentoId =
                            x.Municipio.Departamento.DepartamentoId,
                        nombreDepartamento =
                            x.Municipio.Departamento.NombreDepartamento,
                        municipioId = x.Municipio.MunicipioId,
                        nombreMunicipio = x.Municipio.NombreMunicipio
                    }
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                total,
                page,
                pageSize,
                totalPages = total == 0
                    ? 0
                    : (int)Math.Ceiling(total / (decimal)pageSize),
                data
            });
        }

        private static IQueryable<Terreno> AplicarOrdenamiento(
            IQueryable<Terreno> query,
            string? ordenarPor,
            bool descendente)
        {
            string campo = ordenarPor?.Trim().ToLowerInvariant() ?? "codigo";

            return campo switch
            {
                "propietario" when descendente => query
                    .OrderByDescending(x => x.nombrePropietarioTerreno)
                    .ThenByDescending(x => x.terrenoId),

                "propietario" => query
                    .OrderBy(x => x.nombrePropietarioTerreno)
                    .ThenBy(x => x.terrenoId),

                "fecha" when descendente => query
                    .OrderByDescending(x => x.fechaIngresoTerreno)
                    .ThenByDescending(x => x.terrenoId),

                "fecha" => query
                    .OrderBy(x => x.fechaIngresoTerreno)
                    .ThenBy(x => x.terrenoId),

                "extension" when descendente => query
                    .OrderByDescending(x => x.extensionManzanaTerreno)
                    .ThenByDescending(x => x.terrenoId),

                "extension" => query
                    .OrderBy(x => x.extensionManzanaTerreno)
                    .ThenBy(x => x.terrenoId),

                _ when descendente => query
                    .OrderByDescending(x => x.codigoTerreno)
                    .ThenByDescending(x => x.terrenoId),

                _ => query
                    .OrderBy(x => x.codigoTerreno)
                    .ThenBy(x => x.terrenoId)
            };
        }

        private static string? ValidarDatosTerreno(
            string? identificacionPropietario,
            string? nombrePropietario,
            string? direccion,
            decimal extensionManzanas,
            decimal cantidadQuintales,
            int cantidadPlantas,
            int telefono,
            int municipioId,
            decimal latitud,
            decimal longitud)
        {
            if (string.IsNullOrWhiteSpace(identificacionPropietario) ||
                !CedulaRegex.IsMatch(identificacionPropietario.Trim()))
            {
                return "La identificación del propietario debe tener el formato 001-080701-1050R.";
            }

            if (string.IsNullOrWhiteSpace(nombrePropietario))
                return "El nombre del propietario es obligatorio.";

            if (string.IsNullOrWhiteSpace(direccion))
                return "La dirección del terreno es obligatoria.";

            if (municipioId <= 0)
                return "Debe seleccionar un municipio válido.";

            if (extensionManzanas <= 0)
                return "La extensión del terreno debe ser mayor que cero.";

            if (!TieneMaximoDosDecimales(extensionManzanas))
                return "La extensión del terreno solo permite dos decimales.";

            if (cantidadQuintales < 0)
                return "La cantidad de quintales no puede ser negativa.";

            if (!TieneMaximoDosDecimales(cantidadQuintales))
                return "La cantidad de quintales solo permite dos decimales.";

            if (cantidadPlantas < 0)
                return "La cantidad de plantas debe ser un número entero positivo o cero.";

            if (telefono < 0)
                return "El teléfono solo debe contener números enteros positivos.";

            if (latitud < -90 || latitud > 90)
                return "La latitud debe estar entre -90 y 90.";

            if (longitud < -180 || longitud > 180)
                return "La longitud debe estar entre -180 y 180.";

            return null;
        }

        private static string? ValidarFiltrosBusqueda(
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            decimal? extensionMinima,
            decimal? extensionMaxima)
        {
            if (fechaDesde.HasValue &&
                fechaHasta.HasValue &&
                fechaDesde.Value > fechaHasta.Value)
            {
                return "La fecha inicial no puede ser mayor que la fecha final.";
            }

            if (extensionMinima is < 0 || extensionMaxima is < 0)
            {
                return "Las extensiones utilizadas como filtro no pueden ser negativas.";
            }

            if (extensionMinima.HasValue &&
                extensionMaxima.HasValue &&
                extensionMinima.Value > extensionMaxima.Value)
            {
                return "La extensión mínima no puede ser mayor que la extensión máxima.";
            }

            return null;
        }

        private Task<bool> MunicipioActivoExisteAsync(
            int municipioId,
            CancellationToken cancellationToken)
        {
            return _db.Municipios
                .AsNoTracking()
                .AnyAsync(
                    x => x.MunicipioId == municipioId && x.Activo,
                    cancellationToken);
        }

        private static string GenerarCodigoTerreno(
            int municipioId,
            int terrenoId)
        {
            return $"TRR-{municipioId:D4}-{terrenoId:D6}";
        }

        private static string NormalizarCedula(string valor)
        {
            return valor.Trim().ToUpperInvariant();
        }

        private static string? NormalizarOpcional(string? valor)
        {
            return string.IsNullOrWhiteSpace(valor)
                ? null
                : valor.Trim();
        }

        private static string? NormalizarFiltro(string? valor)
        {
            return string.IsNullOrWhiteSpace(valor)
                ? null
                : valor.Trim();
        }

        private static bool TieneMaximoDosDecimales(decimal valor)
        {
            return decimal.Round(valor, 2) == valor;
        }

        private static bool EsConflictoUnico(DbUpdateException exception)
        {
            return exception.InnerException is SqlException sqlException &&
                   (sqlException.Number == 2601 || sqlException.Number == 2627);
        }
    }
}
