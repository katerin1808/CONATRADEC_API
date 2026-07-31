using CONATRADEC_API.Constants;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Centraliza la consulta y reactivación de registros eliminados
    /// lógicamente utilizados por las pantallas de Configuración.
    ///
    /// No elimina relaciones históricas. Al reactivar un terreno también
    /// habilita su relación de propiedad más reciente, manteniendo intacto
    /// el historial anterior.
    /// </summary>
    [ApiController]
    [Route("api/catalogos-eliminados")]
    public sealed class CatalogosEliminadosController : ControllerBase
    {
        private readonly DBContext db;
        private readonly NoticiasDbContext noticiasDb;
        private readonly PermisoApiService permisoApiService;
        private readonly ILogger<CatalogosEliminadosController> logger;

        public CatalogosEliminadosController(
            DBContext db,
            NoticiasDbContext noticiasDb,
            PermisoApiService permisoApiService,
            ILogger<CatalogosEliminadosController> logger)
        {
            this.db = db;
            this.noticiasDb = noticiasDb;
            this.permisoApiService = permisoApiService;
            this.logger = logger;
        }

        [HttpGet("{catalogo}")]
        public async Task<ActionResult> Listar(
            string catalogo,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            string codigo = NormalizarCatalogo(catalogo);

            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    codigo,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<CatalogoEliminadoItemDto>? data =
                codigo switch
                {
                    Catalogos.Pais =>
                        await ListarPaisesAsync(cancellationToken),

                    Catalogos.Departamento =>
                        await ListarDepartamentosAsync(cancellationToken),

                    Catalogos.Municipio =>
                        await ListarMunicipiosAsync(cancellationToken),

                    Catalogos.Rol =>
                        await ListarRolesAsync(cancellationToken),

                    Catalogos.ElementoQuimico =>
                        await ListarElementosAsync(cancellationToken),

                    Catalogos.TipoCultivo =>
                        await ListarTiposCultivoAsync(cancellationToken),

                    Catalogos.TipoAnalisis =>
                        await ListarTiposAnalisisAsync(cancellationToken),

                    Catalogos.Usuario =>
                        await ListarUsuariosAsync(cancellationToken),

                    Catalogos.Terreno =>
                        await ListarTerrenosAsync(cancellationToken),

                    Catalogos.ExtraccionNutriente =>
                        await ListarExtraccionesAsync(cancellationToken),

                    Catalogos.RangoNutriente =>
                        await ListarRangosAsync(cancellationToken),

                    Catalogos.CategoriaPublicacion =>
                        await ListarCategoriasPublicacionAsync(cancellationToken),

                    Catalogos.CategoriaAlbum =>
                        await ListarCategoriasAlbumAsync(cancellationToken),

                    _ => null
                };

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El catálogo solicitado no admite reactivación."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Registros eliminados obtenidos correctamente.",
                data
            });
        }

        [HttpPut("{catalogo}/{id:int}/reactivar")]
        public async Task<ActionResult> Reactivar(
            string catalogo,
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del registro no es válido."
                });
            }

            string codigo = NormalizarCatalogo(catalogo);

            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    codigo,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            try
            {
                return codigo switch
                {
                    Catalogos.Pais =>
                        await ReactivarPaisAsync(id, cancellationToken),

                    Catalogos.Departamento =>
                        await ReactivarDepartamentoAsync(id, cancellationToken),

                    Catalogos.Municipio =>
                        await ReactivarMunicipioAsync(id, cancellationToken),

                    Catalogos.Rol =>
                        await ReactivarRolAsync(id, cancellationToken),

                    Catalogos.ElementoQuimico =>
                        await ReactivarElementoAsync(id, cancellationToken),

                    Catalogos.TipoCultivo =>
                        await ReactivarTipoCultivoAsync(id, cancellationToken),

                    Catalogos.TipoAnalisis =>
                        await ReactivarTipoAnalisisAsync(id, cancellationToken),

                    Catalogos.Usuario =>
                        await ReactivarUsuarioAsync(id, cancellationToken),

                    Catalogos.Terreno =>
                        await ReactivarTerrenoAsync(id, cancellationToken),

                    Catalogos.ExtraccionNutriente =>
                        await ReactivarExtraccionAsync(id, cancellationToken),

                    Catalogos.RangoNutriente =>
                        await ReactivarRangoAsync(id, cancellationToken),

                    Catalogos.CategoriaPublicacion =>
                        await ReactivarCategoriaPublicacionAsync(
                            id,
                            cancellationToken),

                    Catalogos.CategoriaAlbum =>
                        await ReactivarCategoriaAlbumAsync(
                            id,
                            cancellationToken),

                    _ => NotFound(new
                    {
                        success = false,
                        message =
                            "El catálogo solicitado no admite reactivación."
                    })
                };
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al reactivar {Catalogo} con ID {Id}.",
                    codigo,
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible reactivar el registro porque existe otro registro activo con la misma identidad."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error al reactivar {Catalogo} con ID {Id}.",
                    codigo,
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al reactivar el registro."
                });
            }
        }


[HttpPost("usuario/coincidencia")]
public async Task<ActionResult> BuscarCoincidenciaUsuario(
    [FromBody] JsonElement datos,
    [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
    CancellationToken cancellationToken)
{
    ActionResult? acceso =
        await ValidarAccesoAsync(
            usuarioSesionId,
            Catalogos.Usuario,
            TipoPermisoApi.Agregar,
            cancellationToken);

    if (acceso != null)
        return acceso;

    if (datos.ValueKind != JsonValueKind.Object)
    {
        return BadRequest(new
        {
            success = false,
            message =
                "No se recibieron datos válidos del usuario."
        });
    }

    string nombreUsuario =
        NormalizarNombre(
            ObtenerString(
                datos,
                "NombreUsuario",
                "nombreUsuario"));

    string correo =
        NormalizarNombre(
            ObtenerString(
                datos,
                "CorreoUsuario",
                "correoUsuario"));

    string identificacion =
        NormalizarNombre(
            ObtenerString(
                datos,
                "IdentificacionUsuario",
                "identificacionUsuario"));

    if (string.IsNullOrWhiteSpace(nombreUsuario) ||
        string.IsNullOrWhiteSpace(correo) ||
        string.IsNullOrWhiteSpace(identificacion))
    {
        return NoContent();
    }

    List<Usuario> coincidencias =
        await db.Usuarios
            .AsNoTracking()
            .Where(x =>
                !x.activo &&
                (x.nombreUsuario == nombreUsuario ||
                 x.correoUsuario == correo ||
                 x.identificacionUsuario == identificacion))
            .Take(2)
            .ToListAsync(cancellationToken);

    if (coincidencias.Count == 0)
        return NoContent();

    if (coincidencias.Count > 1)
    {
        return Conflict(new
        {
            success = false,
            message =
                "Los datos coinciden con más de un usuario inactivo. Reactívelo manualmente desde Usuarios inactivos."
        });
    }

    Usuario usuario = coincidencias[0];

    return Ok(new
    {
        success = true,
        message =
            "Se encontró un usuario inactivo con la misma identidad.",
        data = new
        {
            registro = new CatalogoEliminadoItemDto
            {
                Id = usuario.UsuarioId,
                Catalogo = Catalogos.Usuario,
                Titulo = usuario.nombreCompletoUsuario,
                Subtitulo =
                    usuario.nombreUsuario +
                    " · " +
                    usuario.correoUsuario,
                Codigo =
                    usuario.identificacionUsuario ??
                    string.Empty,
                Activo = false
            },
            puedeCrearNuevo = false
        }
    });
}

        [HttpPost("{catalogo}/crear")]
        public async Task<ActionResult> Crear(
            string catalogo,
            [FromBody] JsonElement datos,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            string codigo = NormalizarCatalogo(catalogo);

            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    codigo,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return await EjecutarCreacionSeguraAsync(
                codigo,
                datos,
                null,
                false,
                cancellationToken);
        }

        [HttpPost("{catalogo}/crear-confirmado")]
        public async Task<ActionResult> CrearConfirmado(
            string catalogo,
            [FromBody] JsonElement datos,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            string codigo = NormalizarCatalogo(catalogo);

            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    codigo,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return await EjecutarCreacionSeguraAsync(
                codigo,
                datos,
                null,
                true,
                cancellationToken);
        }

        [HttpPut("{catalogo}/{id:int}/reactivar-con-datos")]
        public async Task<ActionResult> ReactivarConDatos(
            string catalogo,
            int id,
            [FromBody] JsonElement datos,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del registro no es válido."
                });
            }

            string codigo = NormalizarCatalogo(catalogo);

            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    codigo,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return await EjecutarCreacionSeguraAsync(
                codigo,
                datos,
                id,
                false,
                cancellationToken);
        }

        private async Task<ActionResult> EjecutarCreacionSeguraAsync(
            string catalogo,
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            try
            {
                return await ProcesarCreacionAsync(
                    catalogo,
                    datos,
                    reactivarId,
                    creacionConfirmada,
                    cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al guardar el catálogo {Catalogo}.",
                    catalogo);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible guardar el registro porque existe un conflicto con otro registro."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al guardar el catálogo {Catalogo}.",
                    catalogo);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al guardar el registro."
                });
            }
        }

        private Task<ActionResult> ProcesarCreacionAsync(
            string catalogo,
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            if (datos.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult<ActionResult>(
                    BadRequest(new
                    {
                        success = false,
                        message =
                            "No se recibieron datos válidos para el registro."
                    }));
            }

            return catalogo switch
            {
                Catalogos.Pais =>
                    GuardarPaisAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.Departamento =>
                    GuardarDepartamentoAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.Municipio =>
                    GuardarMunicipioAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.Rol =>
                    GuardarRolAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.ElementoQuimico =>
                    GuardarElementoAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.TipoCultivo =>
                    GuardarTipoCultivoAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.TipoAnalisis =>
                    GuardarTipoAnalisisAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.Usuario =>
                    GuardarUsuarioAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.ExtraccionNutriente =>
                    GuardarExtraccionAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.RangoNutriente =>
                    GuardarRangoAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.CategoriaPublicacion =>
                    GuardarCategoriaPublicacionAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                Catalogos.CategoriaAlbum =>
                    GuardarCategoriaAlbumAsync(
                        datos,
                        reactivarId,
                        creacionConfirmada,
                        cancellationToken),

                _ => Task.FromResult<ActionResult>(
                    NotFound(new
                    {
                        success = false,
                        message =
                            "Este formulario no utiliza el flujo de creación con reactivación."
                    }))
            };
        }

        // ==========================================================
        // LISTADOS
        // ==========================================================

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarPaisesAsync(CancellationToken cancellationToken) =>
            await db.Pais
                .AsNoTracking()
                .Where(x => !x.Activo)
                .OrderBy(x => x.NombrePais)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.PaisId,
                    Catalogo = Catalogos.Pais,
                    Titulo = x.NombrePais,
                    Subtitulo = "Código ISO: " + x.CodigoISOPais,
                    Detalle =
                        x.Departamentos.Count == 1
                            ? "1 departamento relacionado"
                            : x.Departamentos.Count +
                              " departamentos relacionados",
                    Codigo = x.CodigoISOPais,
                    Activo = x.Activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarDepartamentosAsync(CancellationToken cancellationToken) =>
            await db.Departamento
                .AsNoTracking()
                .Where(x => !x.Activo)
                .OrderBy(x => x.NombreDepartamento)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.DepartamentoId,
                    Catalogo = Catalogos.Departamento,
                    Titulo = x.NombreDepartamento,
                    Subtitulo = "País: " + x.Pais.NombrePais,
                    Detalle =
                        x.Municipios.Count == 1
                            ? "1 municipio relacionado"
                            : x.Municipios.Count +
                              " municipios relacionados",
                    Codigo = x.Pais.CodigoISOPais,
                    Activo = x.Activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarMunicipiosAsync(CancellationToken cancellationToken) =>
            await db.Municipios
                .AsNoTracking()
                .Where(x => !x.Activo)
                .OrderBy(x => x.NombreMunicipio)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.MunicipioId,
                    Catalogo = Catalogos.Municipio,
                    Titulo = x.NombreMunicipio,
                    Subtitulo =
                        x.Departamento.NombreDepartamento +
                        " · " +
                        x.Departamento.Pais.NombrePais,
                    Detalle =
                        x.Usuarios.Count == 1
                            ? "1 usuario relacionado"
                            : x.Usuarios.Count +
                              " usuarios relacionados",
                    Codigo = x.Departamento.Pais.CodigoISOPais,
                    Activo = x.Activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarRolesAsync(CancellationToken cancellationToken) =>
            await db.Roles
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreRol)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.rolId,
                    Catalogo = Catalogos.Rol,
                    Titulo = x.nombreRol,
                    Subtitulo = x.descripcionRol,
                    Detalle =
                        x.Usuarios.Count == 1
                            ? "1 usuario relacionado"
                            : x.Usuarios.Count +
                              " usuarios relacionados",
                    Codigo = "ROL",
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarElementosAsync(CancellationToken cancellationToken) =>
            await db.elementoQuimico
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreElementoQuimico)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.elementoQuimicosId,
                    Catalogo = Catalogos.ElementoQuimico,
                    Titulo = x.nombreElementoQuimico,
                    Subtitulo =
                        "Símbolo: " +
                        x.simboloElementoQuimico,
                    Detalle =
                        "Peso equivalente: " +
                        x.pesoEquivalenteElementoQuimico,
                    Codigo = x.simboloElementoQuimico,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarTiposCultivoAsync(CancellationToken cancellationToken) =>
            await db.TipoCultivos
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreTipoCultivo)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.tipoCultivoId,
                    Catalogo = Catalogos.TipoCultivo,
                    Titulo = x.nombreTipoCultivo,
                    Subtitulo = x.descripcionTipoCultivo,
                    Detalle =
                        db.ParametroRangoNutrienteCultivo.Count(r =>
                            r.tipoCultivoId == x.tipoCultivoId) +
                        " rangos históricos relacionados",
                    Codigo = "CULTIVO",
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarTiposAnalisisAsync(CancellationToken cancellationToken) =>
            await db.TipoAnalisisSuelos
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreTipoAnalisisSuelo)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.tipoAnalisisSueloId,
                    Catalogo = Catalogos.TipoAnalisis,
                    Titulo = x.nombreTipoAnalisisSuelo,
                    Subtitulo = x.descripcionTipoAnalisisSuelo,
                    Detalle =
                        x.codigoTipoAnalisisSuelo ==
                            TipoAnalisisSueloCodigos.RequerimientoAnual ||
                        x.codigoTipoAnalisisSuelo ==
                            TipoAnalisisSueloCodigos.BalanceFormula ||
                        x.codigoTipoAnalisisSuelo ==
                            TipoAnalisisSueloCodigos.EnmiendaCalcarea ||
                        x.codigoTipoAnalisisSuelo ==
                            TipoAnalisisSueloCodigos.FertilizacionMixta
                            ? "Tipo interno del sistema"
                            : "Tipo personalizado",
                    Codigo = x.codigoTipoAnalisisSuelo,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarUsuariosAsync(CancellationToken cancellationToken) =>
            await db.Usuarios
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreCompletoUsuario)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.UsuarioId,
                    Catalogo = Catalogos.Usuario,
                    Titulo = x.nombreCompletoUsuario,
                    Subtitulo =
                        x.nombreUsuario +
                        " · " +
                        x.correoUsuario,
                    Detalle =
                        "Rol: " +
                        db.Roles
                            .Where(r => r.rolId == x.rolId)
                            .Select(r => r.nombreRol)
                            .FirstOrDefault(),
                    Codigo = x.identificacionUsuario ?? string.Empty,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarTerrenosAsync(CancellationToken cancellationToken) =>
            await db.Terreno
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.codigoTerreno)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.terrenoId,
                    Catalogo = Catalogos.Terreno,
                    Titulo = x.codigoTerreno,
                    Subtitulo =
                        x.RelacionesPropietario
                            .OrderByDescending(relacion =>
                                relacion.fechaAsignacionUtc)
                            .Select(relacion =>
                                relacion.Propietario.nombreCompleto)
                            .FirstOrDefault() ??
                        "Sin propietario",
                    Detalle =
                        x.Municipio.NombreMunicipio +
                        " · " +
                        x.Municipio.Departamento.NombreDepartamento,
                    Codigo =
                        x.RelacionesPropietario
                            .OrderByDescending(relacion =>
                                relacion.fechaAsignacionUtc)
                            .Select(relacion =>
                                relacion.Propietario.identificacion)
                            .FirstOrDefault() ??
                        string.Empty,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarExtraccionesAsync(CancellationToken cancellationToken) =>
            await db.ParametroExtraccionNutrienteCafe
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.parametroExtraccionNutrienteCafeId,
                    Catalogo = Catalogos.ExtraccionNutriente,
                    Titulo =
                        x.ElementoQuimico.nombreElementoQuimico,
                    Subtitulo =
                        "Símbolo: " +
                        x.ElementoQuimico.simboloElementoQuimico,
                    Detalle =
                        x.cantidadExtraidaPorQQOro +
                        " por QQ oro · " +
                        x.descripcionParametro,
                    Codigo =
                        x.ElementoQuimico.simboloElementoQuimico,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarRangosAsync(CancellationToken cancellationToken) =>
            await db.ParametroRangoNutrienteCultivo
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.TipoCultivo.nombreTipoCultivo)
                .ThenBy(x => x.ElementoQuimico.nombreElementoQuimico)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.parametroRangoNutrienteCultivoId,
                    Catalogo = Catalogos.RangoNutriente,
                    Titulo =
                        x.TipoCultivo.nombreTipoCultivo +
                        " · " +
                        x.ElementoQuimico.nombreElementoQuimico,
                    Subtitulo =
                        x.valorMinimo +
                        " - " +
                        x.valorMaximo +
                        " " +
                        x.unidadBase,
                    Detalle = x.descripcionParametro,
                    Codigo =
                        x.ElementoQuimico.simboloElementoQuimico,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarCategoriasPublicacionAsync(
                CancellationToken cancellationToken) =>
            await noticiasDb.CategoriasPublicacion
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.orden)
                .ThenBy(x => x.nombreCategoriaPublicacion)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.categoriaPublicacionId,
                    Catalogo = Catalogos.CategoriaPublicacion,
                    Titulo = x.nombreCategoriaPublicacion,
                    Subtitulo =
                        x.descripcionCategoriaPublicacion,
                    Detalle =
                        x.Publicaciones.Count == 1
                            ? "1 publicación relacionada"
                            : x.Publicaciones.Count +
                              " publicaciones relacionadas",
                    Codigo = x.colorHex,
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        private async Task<List<CatalogoEliminadoItemDto>>
            ListarCategoriasAlbumAsync(
                CancellationToken cancellationToken) =>
            await db.CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(x => !x.activo)
                .OrderBy(x => x.nombreCategoria)
                .Select(x => new CatalogoEliminadoItemDto
                {
                    Id = x.categoriaAlbumBotanicoId,
                    Catalogo = Catalogos.CategoriaAlbum,
                    Titulo = x.nombreCategoria,
                    Subtitulo = x.descripcion ?? string.Empty,
                    Detalle =
                        x.Registros.Count == 1
                            ? "1 registro relacionado"
                            : x.Registros.Count +
                              " registros relacionados",
                    Codigo = "ÁLBUM",
                    Activo = x.activo
                })
                .ToListAsync(cancellationToken);

        // ==========================================================
        // REACTIVACIÓN DIRECTA
        // ==========================================================

        private async Task<ActionResult> ReactivarPaisAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Pais? entity =
                await db.Pais.FirstOrDefaultAsync(
                    x => x.PaisId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El país");

            if (entity.Activo)
                return YaActivo("El país");

            bool duplicado =
                await db.Pais.AsNoTracking().AnyAsync(
                    x =>
                        x.PaisId != id &&
                        x.Activo &&
                        (x.CodigoISOPais == entity.CodigoISOPais ||
                         x.NombrePais.ToUpper() ==
                         entity.NombrePais.ToUpper()),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("país");

            entity.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("País");
        }

        private async Task<ActionResult> ReactivarDepartamentoAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Departamento? entity =
                await db.Departamento.FirstOrDefaultAsync(
                    x => x.DepartamentoId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El departamento");

            if (entity.Activo)
                return YaActivo("El departamento");

            bool padreActivo =
                await db.Pais.AsNoTracking().AnyAsync(
                    x =>
                        x.PaisId == entity.PaisId &&
                        x.Activo,
                    cancellationToken);

            if (!padreActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Debe reactivar primero el país relacionado."
                });
            }

            bool duplicado =
                await db.Departamento.AsNoTracking().AnyAsync(
                    x =>
                        x.DepartamentoId != id &&
                        x.Activo &&
                        x.PaisId == entity.PaisId &&
                        x.NombreDepartamento.ToUpper() ==
                        entity.NombreDepartamento.ToUpper(),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("departamento");

            entity.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Departamento");
        }

        private async Task<ActionResult> ReactivarMunicipioAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Municipio? entity =
                await db.Municipios.FirstOrDefaultAsync(
                    x => x.MunicipioId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El municipio");

            if (entity.Activo)
                return YaActivo("El municipio");

            bool padreActivo =
                await db.Departamento
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.DepartamentoId ==
                                entity.DepartamentoId &&
                            x.Activo &&
                            x.Pais.Activo,
                        cancellationToken);

            if (!padreActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Debe reactivar primero el departamento y el país relacionados."
                });
            }

            bool duplicado =
                await db.Municipios.AsNoTracking().AnyAsync(
                    x =>
                        x.MunicipioId != id &&
                        x.Activo &&
                        x.DepartamentoId ==
                            entity.DepartamentoId &&
                        x.NombreMunicipio.ToUpper() ==
                        entity.NombreMunicipio.ToUpper(),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("municipio");

            entity.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Municipio");
        }

        private async Task<ActionResult> ReactivarRolAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Rol? entity =
                await db.Roles.FirstOrDefaultAsync(
                    x => x.rolId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El rol");

            if (entity.activo)
                return YaActivo("El rol");

            bool duplicado =
                await db.Roles.AsNoTracking().AnyAsync(
                    x =>
                        x.rolId != id &&
                        x.activo &&
                        x.nombreRol.ToUpper() ==
                        entity.nombreRol.ToUpper(),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("rol");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Rol");
        }

        private async Task<ActionResult> ReactivarElementoAsync(
            int id,
            CancellationToken cancellationToken)
        {
            ElementoQuimico? entity =
                await db.elementoQuimico.FirstOrDefaultAsync(
                    x => x.elementoQuimicosId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El elemento químico");

            if (entity.activo)
                return YaActivo("El elemento químico");

            bool duplicado =
                await db.elementoQuimico.AsNoTracking().AnyAsync(
                    x =>
                        x.elementoQuimicosId != id &&
                        x.activo &&
                        (x.simboloElementoQuimico.ToUpper() ==
                         entity.simboloElementoQuimico.ToUpper() ||
                         x.nombreElementoQuimico.ToUpper() ==
                         entity.nombreElementoQuimico.ToUpper()),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("elemento químico");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Elemento químico");
        }

        private async Task<ActionResult> ReactivarTipoCultivoAsync(
            int id,
            CancellationToken cancellationToken)
        {
            TipoCultivo? entity =
                await db.TipoCultivos.FirstOrDefaultAsync(
                    x => x.tipoCultivoId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El tipo de cultivo");

            if (entity.activo)
                return YaActivo("El tipo de cultivo");

            bool duplicado =
                await db.TipoCultivos.AsNoTracking().AnyAsync(
                    x =>
                        x.tipoCultivoId != id &&
                        x.activo &&
                        x.nombreTipoCultivo.ToUpper() ==
                        entity.nombreTipoCultivo.ToUpper(),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("tipo de cultivo");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Tipo de cultivo");
        }

        private async Task<ActionResult> ReactivarTipoAnalisisAsync(
            int id,
            CancellationToken cancellationToken)
        {
            TipoAnalisisSuelo? entity =
                await db.TipoAnalisisSuelos.FirstOrDefaultAsync(
                    x => x.tipoAnalisisSueloId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El tipo de análisis");

            if (entity.activo)
                return YaActivo("El tipo de análisis");

            bool duplicado =
                await db.TipoAnalisisSuelos.AsNoTracking().AnyAsync(
                    x =>
                        x.tipoAnalisisSueloId != id &&
                        x.activo &&
                        (x.codigoTipoAnalisisSuelo ==
                         entity.codigoTipoAnalisisSuelo ||
                         x.nombreTipoAnalisisSuelo.ToUpper() ==
                         entity.nombreTipoAnalisisSuelo.ToUpper()),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("tipo de análisis");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Tipo de análisis");
        }

        private async Task<ActionResult> ReactivarUsuarioAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Usuario? entity =
                await db.Usuarios.FirstOrDefaultAsync(
                    x => x.UsuarioId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El usuario");

            if (entity.activo)
                return YaActivo("El usuario");

            bool rolActivo =
                await db.Roles.AsNoTracking().AnyAsync(
                    x =>
                        x.rolId == entity.rolId &&
                        x.activo,
                    cancellationToken);

            bool procedenciaActiva =
                await db.Procedencia.AsNoTracking().AnyAsync(
                    x =>
                        x.procedenciaId == entity.procedenciaId &&
                        x.activo,
                    cancellationToken);

            bool municipioActivo =
                !entity.municipioId.HasValue ||
                await db.Municipios.AsNoTracking().AnyAsync(
                    x =>
                        x.MunicipioId == entity.municipioId.Value &&
                        x.Activo &&
                        x.Departamento.Activo &&
                        x.Departamento.Pais.Activo,
                    cancellationToken);

            bool relacionesActivas =
                rolActivo &&
                procedenciaActiva &&
                municipioActivo;

            if (!relacionesActivas)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede reactivar el usuario porque su rol, procedencia o ubicación está inactiva."
                });
            }

            bool duplicado =
                await db.Usuarios.AsNoTracking().AnyAsync(
                    x =>
                        x.UsuarioId != id &&
                        x.activo &&
                        (x.nombreUsuario ==
                         entity.nombreUsuario ||
                         x.correoUsuario ==
                         entity.correoUsuario ||
                         x.identificacionUsuario ==
                         entity.identificacionUsuario),
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("usuario");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Usuario");
        }

        private async Task<ActionResult> ReactivarTerrenoAsync(
            int id,
            CancellationToken cancellationToken)
        {
            Terreno? entity =
                await db.Terreno.FirstOrDefaultAsync(
                    x => x.terrenoId == id,
                    cancellationToken);

            if (entity == null)
                return NoEncontrado("El terreno");

            if (entity.activo)
                return YaActivo("El terreno");

            bool ubicacionActiva =
                await db.Municipios.AsNoTracking().AnyAsync(
                    x =>
                        x.MunicipioId == entity.municipioId &&
                        x.Activo &&
                        x.Departamento.Activo &&
                        x.Departamento.Pais.Activo,
                    cancellationToken);

            if (!ubicacionActiva)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Debe reactivar primero la ubicación relacionada con el terreno."
                });
            }

            bool duplicado =
                await db.Terreno.AsNoTracking().AnyAsync(
                    x =>
                        x.terrenoId != id &&
                        x.activo &&
                        x.codigoTerreno ==
                        entity.codigoTerreno,
                    cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("terreno");

            entity.activo = true;

            PropietarioTerreno? relacion =
                await db.PropietarioTerrenos
                    .Where(item =>
                        item.terrenoId == id)
                    .OrderByDescending(item =>
                        item.fechaAsignacionUtc)
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (relacion is not null)
            {
                relacion.activo = true;
                relacion.fechaDesasignacionUtc = null;
                relacion.desasignadoPorUsuarioId = null;
            }

            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Terreno");
        }

        private async Task<ActionResult> ReactivarExtraccionAsync(
            int id,
            CancellationToken cancellationToken)
        {
            ParametroExtraccionNutrienteCafe? entity =
                await db.ParametroExtraccionNutrienteCafe
                    .FirstOrDefaultAsync(
                        x =>
                            x.parametroExtraccionNutrienteCafeId ==
                            id,
                        cancellationToken);

            if (entity == null)
                return NoEncontrado("El parámetro de extracción");

            if (entity.activo)
                return YaActivo("El parámetro de extracción");

            bool elementoActivo =
                await db.elementoQuimico.AsNoTracking().AnyAsync(
                    x =>
                        x.elementoQuimicosId ==
                            entity.elementoQuimicosId &&
                        x.activo,
                    cancellationToken);

            if (!elementoActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Debe reactivar primero el elemento químico relacionado."
                });
            }

            bool duplicado =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.parametroExtraccionNutrienteCafeId !=
                                id &&
                            x.activo &&
                            x.elementoQuimicosId ==
                                entity.elementoQuimicosId,
                        cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("parámetro de extracción");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Parámetro de extracción");
        }

        private async Task<ActionResult> ReactivarRangoAsync(
            int id,
            CancellationToken cancellationToken)
        {
            ParametroRangoNutrienteCultivo? entity =
                await db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(
                        x =>
                            x.parametroRangoNutrienteCultivoId ==
                            id,
                        cancellationToken);

            if (entity == null)
                return NoEncontrado("El rango nutricional");

            if (entity.activo)
                return YaActivo("El rango nutricional");

            bool relacionesActivas =
                await db.TipoCultivos.AsNoTracking().AnyAsync(
                    x =>
                        x.tipoCultivoId ==
                            entity.tipoCultivoId &&
                        x.activo,
                    cancellationToken) &&
                await db.elementoQuimico.AsNoTracking().AnyAsync(
                    x =>
                        x.elementoQuimicosId ==
                            entity.elementoQuimicosId &&
                        x.activo,
                    cancellationToken);

            if (!relacionesActivas)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Debe reactivar primero el cultivo y el elemento químico relacionados."
                });
            }

            bool duplicado =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.parametroRangoNutrienteCultivoId !=
                                id &&
                            x.activo &&
                            x.tipoCultivoId ==
                                entity.tipoCultivoId &&
                            x.elementoQuimicosId ==
                                entity.elementoQuimicosId,
                        cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("rango nutricional");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Rango nutricional");
        }

        private async Task<ActionResult>
            ReactivarCategoriaPublicacionAsync(
                int id,
                CancellationToken cancellationToken)
        {
            CategoriaPublicacion? entity =
                await noticiasDb.CategoriasPublicacion
                    .FirstOrDefaultAsync(
                        x =>
                            x.categoriaPublicacionId ==
                            id,
                        cancellationToken);

            if (entity == null)
                return NoEncontrado("El tipo de publicación");

            if (entity.activo)
                return YaActivo("El tipo de publicación");

            bool duplicado =
                await noticiasDb.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.categoriaPublicacionId != id &&
                            x.activo &&
                            x.nombreCategoriaPublicacion.ToUpper() ==
                            entity.nombreCategoriaPublicacion.ToUpper(),
                        cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("tipo de publicación");

            entity.activo = true;
            await noticiasDb.SaveChangesAsync(cancellationToken);
            return Reactivado("Tipo de publicación");
        }

        private async Task<ActionResult>
            ReactivarCategoriaAlbumAsync(
                int id,
                CancellationToken cancellationToken)
        {
            CategoriaAlbumBotanico? entity =
                await db.CategoriasAlbumBotanico
                    .FirstOrDefaultAsync(
                        x =>
                            x.categoriaAlbumBotanicoId ==
                            id,
                        cancellationToken);

            if (entity == null)
                return NoEncontrado("La categoría del álbum");

            if (entity.activo)
                return YaActivo("La categoría del álbum");

            bool duplicado =
                await db.CategoriasAlbumBotanico
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.categoriaAlbumBotanicoId != id &&
                            x.activo &&
                            x.nombreCategoria.ToUpper() ==
                            entity.nombreCategoria.ToUpper(),
                        cancellationToken);

            if (duplicado)
                return ConflictoIdentidad("categoría del álbum");

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);
            return Reactivado("Categoría del álbum");
        }

        // ==========================================================
        // CREACIÓN Y REACTIVACIÓN CON DATOS DEL FORMULARIO
        // ==========================================================

        private async Task<ActionResult> GuardarPaisAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombrePais"));

            string codigoIso =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "CodigoISOPais"));

            if (string.IsNullOrWhiteSpace(nombre) ||
                codigoIso.Length != 3)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre es obligatorio y el código ISO debe contener 3 caracteres."
                });
            }

            Pais? reactivar =
                reactivarId.HasValue
                    ? await db.Pais.FirstOrDefaultAsync(
                        x => x.PaisId == reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.Pais.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.PaisId != reactivarId.Value) &&
                        x.Activo &&
                        (x.CodigoISOPais == codigoIso ||
                         x.NombrePais.ToUpper() == nombre),
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("país");

            if (reactivar != null)
            {
                reactivar.NombrePais = nombre;
                reactivar.CodigoISOPais = codigoIso;
                reactivar.Activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("País");
            }

            Pais? inactivo =
                await db.Pais.FirstOrDefaultAsync(
                    x =>
                        !x.Activo &&
                        (x.CodigoISOPais == codigoIso ||
                         x.NombrePais.ToUpper() == nombre),
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    CrearItemPais(inactivo),
                    "Ya existe un país eliminado con el mismo nombre o código ISO.",
                    true);
            }

            db.Pais.Add(new Pais
            {
                NombrePais = nombre,
                CodigoISOPais = codigoIso,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("País");
        }

        private async Task<ActionResult> GuardarDepartamentoAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreDepartamento"));

            int paisId =
                ObtenerInt(
                    datos,
                    "PaisId");

            if (string.IsNullOrWhiteSpace(nombre) ||
                paisId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre y el país son obligatorios."
                });
            }

            Pais? pais =
                await db.Pais.AsNoTracking().FirstOrDefaultAsync(
                    x =>
                        x.PaisId == paisId &&
                        x.Activo,
                    cancellationToken);

            if (pais == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El país seleccionado no existe o está inactivo."
                });
            }

            Departamento? reactivar =
                reactivarId.HasValue
                    ? await db.Departamento.FirstOrDefaultAsync(
                        x =>
                            x.DepartamentoId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.Departamento.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.DepartamentoId != reactivarId.Value) &&
                        x.Activo &&
                        x.PaisId == paisId &&
                        x.NombreDepartamento.ToUpper() == nombre,
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("departamento");

            if (reactivar != null)
            {
                reactivar.NombreDepartamento = nombre;
                reactivar.PaisId = paisId;
                reactivar.Activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Departamento");
            }

            Departamento? inactivo =
                await db.Departamento.FirstOrDefaultAsync(
                    x =>
                        !x.Activo &&
                        x.PaisId == paisId &&
                        x.NombreDepartamento.ToUpper() == nombre,
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.DepartamentoId,
                        Catalogo = Catalogos.Departamento,
                        Titulo = inactivo.NombreDepartamento,
                        Subtitulo = "País: " + pais.NombrePais,
                        Codigo = pais.CodigoISOPais,
                        Activo = false
                    },
                    "Ya existe un departamento eliminado con ese nombre dentro del país seleccionado.",
                    true);
            }

            db.Departamento.Add(new Departamento
            {
                NombreDepartamento = nombre,
                PaisId = paisId,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Departamento");
        }

        private async Task<ActionResult> GuardarMunicipioAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreMunicipio"));

            int departamentoId =
                ObtenerInt(
                    datos,
                    "DepartamentoId");

            if (string.IsNullOrWhiteSpace(nombre) ||
                departamentoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre y el departamento son obligatorios."
                });
            }

            Departamento? departamento =
                await db.Departamento
                    .AsNoTracking()
                    .Include(x => x.Pais)
                    .FirstOrDefaultAsync(
                        x =>
                            x.DepartamentoId == departamentoId &&
                            x.Activo &&
                            x.Pais.Activo,
                        cancellationToken);

            if (departamento == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El departamento o su país está inactivo."
                });
            }

            Municipio? reactivar =
                reactivarId.HasValue
                    ? await db.Municipios.FirstOrDefaultAsync(
                        x =>
                            x.MunicipioId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.Municipios.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.MunicipioId != reactivarId.Value) &&
                        x.Activo &&
                        x.DepartamentoId == departamentoId &&
                        x.NombreMunicipio.ToUpper() == nombre,
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("municipio");

            if (reactivar != null)
            {
                reactivar.NombreMunicipio = nombre;
                reactivar.DepartamentoId = departamentoId;
                reactivar.Activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Municipio");
            }

            Municipio? inactivo =
                await db.Municipios.FirstOrDefaultAsync(
                    x =>
                        !x.Activo &&
                        x.DepartamentoId == departamentoId &&
                        x.NombreMunicipio.ToUpper() == nombre,
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.MunicipioId,
                        Catalogo = Catalogos.Municipio,
                        Titulo = inactivo.NombreMunicipio,
                        Subtitulo =
                            departamento.NombreDepartamento +
                            " · " +
                            departamento.Pais.NombrePais,
                        Codigo = departamento.Pais.CodigoISOPais,
                        Activo = false
                    },
                    "Ya existe un municipio eliminado con ese nombre dentro del departamento seleccionado.",
                    true);
            }

            db.Municipios.Add(new Municipio
            {
                NombreMunicipio = nombre,
                DepartamentoId = departamentoId,
                Activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Municipio");
        }

        private async Task<ActionResult> GuardarRolAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreRol",
                        "nombreRol"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionRol",
                        "descripcionRol"));

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del rol es obligatorio."
                });
            }

            Rol? reactivar =
                reactivarId.HasValue
                    ? await db.Roles.FirstOrDefaultAsync(
                        x =>
                            x.rolId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.Roles.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.rolId != reactivarId.Value) &&
                        x.activo &&
                        x.nombreRol.ToUpper() == nombre,
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("rol");

            if (reactivar != null)
            {
                reactivar.nombreRol = nombre;
                reactivar.descripcionRol = descripcion;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Rol");
            }

            Rol? inactivo =
                await db.Roles.FirstOrDefaultAsync(
                    x =>
                        !x.activo &&
                        x.nombreRol.ToUpper() == nombre,
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.rolId,
                        Catalogo = Catalogos.Rol,
                        Titulo = inactivo.nombreRol,
                        Subtitulo = inactivo.descripcionRol,
                        Codigo = "ROL",
                        Activo = false
                    },
                    "Ya existe un rol eliminado con ese nombre.",
                    true);
            }

            db.Roles.Add(new Rol
            {
                nombreRol = nombre,
                descripcionRol = descripcion,
                activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Rol");
        }

        private async Task<ActionResult> GuardarElementoAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string simbolo =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "SimboloElementoQuimico",
                        "simboloElementoQuimico"));

            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreElementoQuimico",
                        "nombreElementoQuimico"));

            decimal peso =
                ObtenerDecimal(
                    datos,
                    "PesoEquivalenteElementoQuimico",
                    "pesoEquivalenteElementoQuimico");

            if (string.IsNullOrWhiteSpace(simbolo) ||
                string.IsNullOrWhiteSpace(nombre) ||
                peso <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El símbolo, el nombre y un peso equivalente mayor que cero son obligatorios."
                });
            }

            ElementoQuimico? reactivar =
                reactivarId.HasValue
                    ? await db.elementoQuimico.FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.elementoQuimico.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.elementoQuimicosId != reactivarId.Value) &&
                        x.activo &&
                        (x.simboloElementoQuimico.ToUpper() == simbolo ||
                         x.nombreElementoQuimico.ToUpper() == nombre),
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("elemento químico");

            if (reactivar != null)
            {
                reactivar.simboloElementoQuimico = simbolo;
                reactivar.nombreElementoQuimico = nombre;
                reactivar.pesoEquivalenteElementoQuimico = peso;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Elemento químico");
            }

            List<ElementoQuimico> inactivos =
                await db.elementoQuimico
                    .Where(x =>
                        !x.activo &&
                        (x.simboloElementoQuimico.ToUpper() == simbolo ||
                         x.nombreElementoQuimico.ToUpper() == nombre))
                    .Take(2)
                    .ToListAsync(cancellationToken);

            if (inactivos.Count > 1)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El símbolo y el nombre coinciden con registros eliminados diferentes. Reactívelos desde la lista de eliminados para resolver el conflicto."
                });
            }

            if (inactivos.Count == 1 && !creacionConfirmada)
            {
                ElementoQuimico inactivo = inactivos[0];

                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.elementoQuimicosId,
                        Catalogo = Catalogos.ElementoQuimico,
                        Titulo = inactivo.nombreElementoQuimico,
                        Subtitulo =
                            "Símbolo: " +
                            inactivo.simboloElementoQuimico,
                        Detalle =
                            "Peso equivalente: " +
                            inactivo.pesoEquivalenteElementoQuimico,
                        Codigo = inactivo.simboloElementoQuimico,
                        Activo = false
                    },
                    "Ya existe un elemento químico eliminado con el mismo símbolo o nombre.",
                    true);
            }

            db.elementoQuimico.Add(new ElementoQuimico
            {
                simboloElementoQuimico = simbolo,
                nombreElementoQuimico = nombre,
                pesoEquivalenteElementoQuimico = peso,
                activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Elemento químico");
        }

        private async Task<ActionResult> GuardarTipoCultivoAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreTipoCultivo",
                        "nombreTipoCultivo"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionTipoCultivo",
                        "descripcionTipoCultivo"));

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de cultivo es obligatorio."
                });
            }

            TipoCultivo? reactivar =
                reactivarId.HasValue
                    ? await db.TipoCultivos.FirstOrDefaultAsync(
                        x =>
                            x.tipoCultivoId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.TipoCultivos.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.tipoCultivoId != reactivarId.Value) &&
                        x.activo &&
                        x.nombreTipoCultivo.ToUpper() == nombre,
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("tipo de cultivo");

            if (reactivar != null)
            {
                reactivar.nombreTipoCultivo = nombre;
                reactivar.descripcionTipoCultivo = descripcion;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Tipo de cultivo");
            }

            TipoCultivo? inactivo =
                await db.TipoCultivos.FirstOrDefaultAsync(
                    x =>
                        !x.activo &&
                        x.nombreTipoCultivo.ToUpper() == nombre,
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.tipoCultivoId,
                        Catalogo = Catalogos.TipoCultivo,
                        Titulo = inactivo.nombreTipoCultivo,
                        Subtitulo = inactivo.descripcionTipoCultivo,
                        Codigo = "CULTIVO",
                        Activo = false
                    },
                    "Ya existe un tipo de cultivo eliminado con ese nombre.",
                    true);
            }

            db.TipoCultivos.Add(new TipoCultivo
            {
                nombreTipoCultivo = nombre,
                descripcionTipoCultivo = descripcion,
                activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Tipo de cultivo");
        }

        private async Task<ActionResult> GuardarTipoAnalisisAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreTipoAnalisisSuelo",
                        "nombreTipoAnalisisSuelo"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionTipoAnalisisSuelo",
                        "descripcionTipoAnalisisSuelo"));

            if (string.IsNullOrWhiteSpace(nombre) ||
                string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre y la descripción del tipo de análisis son obligatorios."
                });
            }

            TipoAnalisisSuelo? reactivar =
                reactivarId.HasValue
                    ? await db.TipoAnalisisSuelos.FirstOrDefaultAsync(
                        x =>
                            x.tipoAnalisisSueloId ==
                            reactivarId.Value,
                        cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.TipoAnalisisSuelos.AsNoTracking().AnyAsync(
                    x =>
                        (!reactivarId.HasValue ||
                         x.tipoAnalisisSueloId != reactivarId.Value) &&
                        x.activo &&
                        x.nombreTipoAnalisisSuelo.ToUpper() == nombre,
                    cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("tipo de análisis");

            if (reactivar != null)
            {
                reactivar.nombreTipoAnalisisSuelo = nombre;
                reactivar.descripcionTipoAnalisisSuelo = descripcion;
                reactivar.activo = true;

                if (string.IsNullOrWhiteSpace(
                        reactivar.codigoTipoAnalisisSuelo))
                {
                    reactivar.codigoTipoAnalisisSuelo =
                        TipoAnalisisSueloCodigos
                            .CrearCodigoPersonalizado();
                }

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Tipo de análisis");
            }

            TipoAnalisisSuelo? inactivo =
                await db.TipoAnalisisSuelos.FirstOrDefaultAsync(
                    x =>
                        !x.activo &&
                        x.nombreTipoAnalisisSuelo.ToUpper() == nombre,
                    cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id = inactivo.tipoAnalisisSueloId,
                        Catalogo = Catalogos.TipoAnalisis,
                        Titulo = inactivo.nombreTipoAnalisisSuelo,
                        Subtitulo = inactivo.descripcionTipoAnalisisSuelo,
                        Codigo = inactivo.codigoTipoAnalisisSuelo,
                        Activo = false
                    },
                    "Ya existe un tipo de análisis eliminado con ese nombre.",
                    true);
            }

            db.TipoAnalisisSuelos.Add(new TipoAnalisisSuelo
            {
                codigoTipoAnalisisSuelo =
                    TipoAnalisisSueloCodigos
                        .CrearCodigoPersonalizado(),
                nombreTipoAnalisisSuelo = nombre,
                descripcionTipoAnalisisSuelo = descripcion,
                activo = true
            });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Tipo de análisis");
        }

        private async Task<ActionResult> GuardarUsuarioAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            /*
             * Los usuarios nuevos continúan creándose mediante
             * UsuarioController. Este método solo reactiva la cuenta
             * encontrada previamente por nombre, correo o identificación.
             *
             * No se cambia la procedencia, la contraseña ni la fotografía.
             */
            if (!reactivarId.HasValue || reactivarId.Value <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El usuario debe reactivarse desde una coincidencia válida."
                });
            }

            Usuario? usuario =
                await db.Usuarios.FirstOrDefaultAsync(
                    x => x.UsuarioId == reactivarId.Value,
                    cancellationToken);

            if (usuario == null)
                return NoEncontrado("El usuario");

            if (usuario.activo)
                return YaActivo("El usuario");

            string nombreUsuario =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreUsuario",
                        "nombreUsuario"));

            string nombreCompleto =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreCompletoUsuario",
                        "nombreCompletoUsuario"));

            string correo =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "CorreoUsuario",
                        "correoUsuario"));

            string identificacion =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "IdentificacionUsuario",
                        "identificacionUsuario"));

            string telefono =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "TelefonoUsuario",
                        "telefonoUsuario"));

            DateOnly? fechaNacimiento =
                ObtenerDateOnly(
                    datos,
                    "FechaNacimientoUsuario",
                    "fechaNacimientoUsuario");

            int rolSolicitadoId =
                ObtenerInt(
                    datos,
                    "RolId",
                    "rolId");

            int municipioSolicitadoId =
                ObtenerInt(
                    datos,
                    "MunicipioId",
                    "municipioId");

            if (string.IsNullOrWhiteSpace(nombreUsuario) ||
                string.IsNullOrWhiteSpace(nombreCompleto) ||
                string.IsNullOrWhiteSpace(correo) ||
                string.IsNullOrWhiteSpace(identificacion) ||
                !fechaNacimiento.HasValue)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Nombre de usuario, nombre completo, correo, identificación y fecha de nacimiento son obligatorios."
                });
            }

            if (!EsIdentificacionValida(identificacion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La identificación debe tener el formato 001-080701-1050R."
                });
            }

            if (!EsMayorDeEdad(fechaNacimiento))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El usuario debe tener al menos 18 años."
                });
            }

            bool duplicadoActivo =
                await db.Usuarios.AsNoTracking().AnyAsync(
                    x =>
                        x.UsuarioId != usuario.UsuarioId &&
                        x.activo &&
                        (x.nombreUsuario == nombreUsuario ||
                         x.correoUsuario == correo ||
                         x.identificacionUsuario == identificacion),
                    cancellationToken);

            if (duplicadoActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Otro usuario activo ya utiliza el nombre de usuario, correo o identificación indicada."
                });
            }

            Procedencia? procedencia =
                await db.Procedencia.FirstOrDefaultAsync(
                    x =>
                        x.procedenciaId == usuario.procedenciaId &&
                        x.activo,
                    cancellationToken);

            if (procedencia == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede reactivar el usuario porque su procedencia está inactiva."
                });
            }

            bool esInterno =
                string.Equals(
                    procedencia.nombreProcedencia,
                    "Interno",
                    StringComparison.OrdinalIgnoreCase);

            Rol? rol;

            if (esInterno)
            {
                if (rolSolicitadoId <= 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Seleccione un rol activo para reactivar el usuario interno."
                    });
                }

                rol =
                    await db.Roles.FirstOrDefaultAsync(
                        x =>
                            x.rolId == rolSolicitadoId &&
                            x.activo,
                        cancellationToken);

                if (rol == null)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "El rol seleccionado no existe o está inactivo."
                    });
                }

                usuario.rolId = rol.rolId;
            }
            else
            {
                /*
                 * Un usuario externo nunca se convierte en interno.
                 * También conserva el rol con el que fue registrado.
                 */
                rol =
                    await db.Roles.FirstOrDefaultAsync(
                        x =>
                            x.rolId == usuario.rolId &&
                            x.activo,
                        cancellationToken);

                if (rol == null)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "No se puede reactivar el usuario externo porque su rol original está inactivo."
                    });
                }
            }

            Municipio? municipio = null;

            if (municipioSolicitadoId > 0)
            {
                municipio =
                    await db.Municipios
                        .AsNoTracking()
                        .Include(x => x.Departamento)
                        .ThenInclude(x => x.Pais)
                        .FirstOrDefaultAsync(
                            x =>
                                x.MunicipioId == municipioSolicitadoId,
                            cancellationToken);

                if (municipio == null ||
                    !municipio.Activo ||
                    !municipio.Departamento.Activo ||
                    !municipio.Departamento.Pais.Activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "El municipio seleccionado o su ubicación superior está inactiva."
                    });
                }
            }

            usuario.nombreUsuario = nombreUsuario;
            usuario.nombreCompletoUsuario = nombreCompleto;
            usuario.correoUsuario = correo;
            usuario.identificacionUsuario = identificacion;
            usuario.telefonoUsuario =
                string.IsNullOrWhiteSpace(telefono)
                    ? null
                    : telefono;
            usuario.fechaNacimientoUsuario = fechaNacimiento;
            usuario.municipioId =
                municipio?.MunicipioId;
            usuario.activo = true;

            /*
             * claveHashUsuario, procedenciaId y urlImagenUsuario se
             * conservan expresamente.
             */
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Usuario reactivado correctamente. Se conservaron su contraseña y procedencia originales.",
                data = new
                {
                    usuarioId = usuario.UsuarioId,
                    nombreUsuario = usuario.nombreUsuario,
                    identificacionUsuario = usuario.identificacionUsuario,
                    nombreCompletoUsuario = usuario.nombreCompletoUsuario,
                    correoUsuario = usuario.correoUsuario,
                    telefonoUsuario = usuario.telefonoUsuario,
                    fechaNacimientoUsuario = usuario.fechaNacimientoUsuario,
                    rolId = usuario.rolId,
                    procedenciaId = usuario.procedenciaId,
                    municipioId = usuario.municipioId,
                    rolNombre = rol.nombreRol,
                    procedenciaNombre = procedencia.nombreProcedencia,
                    urlImagenUsuario = usuario.urlImagenUsuario ?? string.Empty,
                    esInterno
                }
            });
        }

        private async Task<ActionResult> GuardarExtraccionAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            int elementoId =
                ObtenerInt(
                    datos,
                    "ElementoQuimicosId",
                    "elementoQuimicosId");

            decimal cantidad =
                ObtenerDecimal(
                    datos,
                    "CantidadExtraidaPorQQOro",
                    "cantidadExtraidaPorQQOro");

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionParametro",
                        "descripcionParametro"));

            if (elementoId <= 0 ||
                cantidad <= 0 ||
                string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El elemento químico, la cantidad y la descripción son obligatorios."
                });
            }

            ElementoQuimico? elemento =
                await db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == elementoId &&
                            x.activo,
                        cancellationToken);

            if (elemento == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El elemento químico seleccionado no existe o está inactivo."
                });
            }

            ParametroExtraccionNutrienteCafe? reactivar =
                reactivarId.HasValue
                    ? await db.ParametroExtraccionNutrienteCafe
                        .FirstOrDefaultAsync(
                            x =>
                                x.parametroExtraccionNutrienteCafeId ==
                                reactivarId.Value,
                            cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            (!reactivarId.HasValue ||
                             x.parametroExtraccionNutrienteCafeId !=
                                reactivarId.Value) &&
                            x.activo &&
                            x.elementoQuimicosId == elementoId,
                        cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("parámetro de extracción");

            if (reactivar != null)
            {
                reactivar.elementoQuimicosId = elementoId;
                reactivar.cantidadExtraidaPorQQOro = cantidad;
                reactivar.descripcionParametro = descripcion;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Parámetro de extracción");
            }

            ParametroExtraccionNutrienteCafe? inactivo =
                await db.ParametroExtraccionNutrienteCafe
                    .FirstOrDefaultAsync(
                        x =>
                            !x.activo &&
                            x.elementoQuimicosId == elementoId,
                        cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id =
                            inactivo
                                .parametroExtraccionNutrienteCafeId,
                        Catalogo =
                            Catalogos.ExtraccionNutriente,
                        Titulo =
                            elemento.nombreElementoQuimico,
                        Subtitulo =
                            "Símbolo: " +
                            elemento.simboloElementoQuimico,
                        Detalle =
                            inactivo.cantidadExtraidaPorQQOro +
                            " por QQ oro · " +
                            inactivo.descripcionParametro,
                        Codigo =
                            elemento.simboloElementoQuimico,
                        Activo = false
                    },
                    "Ya existe un parámetro de extracción eliminado para ese elemento químico.",
                    true);
            }

            db.ParametroExtraccionNutrienteCafe.Add(
                new ParametroExtraccionNutrienteCafe
                {
                    elementoQuimicosId = elementoId,
                    cantidadExtraidaPorQQOro = cantidad,
                    descripcionParametro = descripcion,
                    activo = true
                });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Parámetro de extracción");
        }

        private async Task<ActionResult> GuardarRangoAsync(
            JsonElement datos,
            int? reactivarId,
            bool creacionConfirmada,
            CancellationToken cancellationToken)
        {
            int cultivoId =
                ObtenerInt(
                    datos,
                    "TipoCultivoId",
                    "tipoCultivoId");

            int elementoId =
                ObtenerInt(
                    datos,
                    "ElementoQuimicosId",
                    "elementoQuimicosId");

            decimal minimo =
                ObtenerDecimal(
                    datos,
                    "ValorMinimo",
                    "valorMinimo");

            decimal maximo =
                ObtenerDecimal(
                    datos,
                    "ValorMaximo",
                    "valorMaximo");

            string unidad =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "UnidadBase",
                        "unidadBase"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionParametro",
                        "descripcionParametro"));

            if (cultivoId <= 0 ||
                elementoId <= 0 ||
                minimo < 0 ||
                maximo <= minimo ||
                string.IsNullOrWhiteSpace(unidad))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Seleccione cultivo y elemento; el máximo debe ser mayor que el mínimo y la unidad es obligatoria."
                });
            }

            TipoCultivo? cultivo =
                await db.TipoCultivos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.tipoCultivoId == cultivoId &&
                            x.activo,
                        cancellationToken);

            ElementoQuimico? elemento =
                await db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == elementoId &&
                            x.activo,
                        cancellationToken);

            if (cultivo == null ||
                elemento == null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El cultivo o el elemento químico está inactivo."
                });
            }

            ParametroRangoNutrienteCultivo? reactivar =
                reactivarId.HasValue
                    ? await db.ParametroRangoNutrienteCultivo
                        .FirstOrDefaultAsync(
                            x =>
                                x.parametroRangoNutrienteCultivoId ==
                                reactivarId.Value,
                            cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            (!reactivarId.HasValue ||
                             x.parametroRangoNutrienteCultivoId !=
                                reactivarId.Value) &&
                            x.activo &&
                            x.tipoCultivoId == cultivoId &&
                            x.elementoQuimicosId == elementoId,
                        cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("rango nutricional");

            if (reactivar != null)
            {
                reactivar.tipoCultivoId = cultivoId;
                reactivar.elementoQuimicosId = elementoId;
                reactivar.valorMinimo = minimo;
                reactivar.valorMaximo = maximo;
                reactivar.unidadBase = unidad;
                reactivar.descripcionParametro = descripcion;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Rango nutricional");
            }

            ParametroRangoNutrienteCultivo? inactivo =
                await db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(
                        x =>
                            !x.activo &&
                            x.tipoCultivoId == cultivoId &&
                            x.elementoQuimicosId == elementoId,
                        cancellationToken);

            if (inactivo != null && !creacionConfirmada)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id =
                            inactivo
                                .parametroRangoNutrienteCultivoId,
                        Catalogo =
                            Catalogos.RangoNutriente,
                        Titulo =
                            cultivo.nombreTipoCultivo +
                            " · " +
                            elemento.nombreElementoQuimico,
                        Subtitulo =
                            inactivo.valorMinimo +
                            " - " +
                            inactivo.valorMaximo +
                            " " +
                            inactivo.unidadBase,
                        Detalle =
                            inactivo.descripcionParametro,
                        Codigo =
                            elemento.simboloElementoQuimico,
                        Activo = false
                    },
                    "Ya existe un rango nutricional eliminado para la misma combinación de cultivo y elemento.",
                    true);
            }

            db.ParametroRangoNutrienteCultivo.Add(
                new ParametroRangoNutrienteCultivo
                {
                    tipoCultivoId = cultivoId,
                    elementoQuimicosId = elementoId,
                    valorMinimo = minimo,
                    valorMaximo = maximo,
                    unidadBase = unidad,
                    descripcionParametro = descripcion,
                    activo = true
                });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Rango nutricional");
        }

        private async Task<ActionResult>
            GuardarCategoriaPublicacionAsync(
                JsonElement datos,
                int? reactivarId,
                bool creacionConfirmada,
                CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreCategoriaPublicacion",
                        "nombreCategoriaPublicacion"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "DescripcionCategoriaPublicacion",
                        "descripcionCategoriaPublicacion"));

            string color =
                NormalizarColor(
                    ObtenerString(
                        datos,
                        "ColorHex",
                        "colorHex"));

            int orden =
                ObtenerInt(
                    datos,
                    "Orden",
                    "orden");

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de publicación es obligatorio."
                });
            }

            CategoriaPublicacion? reactivar =
                reactivarId.HasValue
                    ? await noticiasDb.CategoriasPublicacion
                        .FirstOrDefaultAsync(
                            x =>
                                x.categoriaPublicacionId ==
                                reactivarId.Value,
                            cancellationToken)
                    : null;

            bool activoDuplicado =
                await noticiasDb.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            (!reactivarId.HasValue ||
                             x.categoriaPublicacionId !=
                                reactivarId.Value) &&
                            x.activo &&
                            x.nombreCategoriaPublicacion.ToUpper() ==
                            nombre,
                        cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("tipo de publicación");

            if (reactivar != null)
            {
                reactivar.nombreCategoriaPublicacion = nombre;
                reactivar.descripcionCategoriaPublicacion =
                    descripcion;
                reactivar.colorHex = color;
                reactivar.orden = orden;
                reactivar.activo = true;

                await noticiasDb.SaveChangesAsync(cancellationToken);
                return Reactivado("Tipo de publicación");
            }

            CategoriaPublicacion? inactivo =
                await noticiasDb.CategoriasPublicacion
                    .FirstOrDefaultAsync(
                        x =>
                            !x.activo &&
                            x.nombreCategoriaPublicacion.ToUpper() ==
                            nombre,
                        cancellationToken);

            if (inactivo != null)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id =
                            inactivo.categoriaPublicacionId,
                        Catalogo =
                            Catalogos.CategoriaPublicacion,
                        Titulo =
                            inactivo.nombreCategoriaPublicacion,
                        Subtitulo =
                            inactivo.descripcionCategoriaPublicacion,
                        Codigo = inactivo.colorHex,
                        Activo = false
                    },
                    "Ya existe un tipo de publicación eliminado con ese nombre.",
                    false);
            }

            noticiasDb.CategoriasPublicacion.Add(
                new CategoriaPublicacion
                {
                    nombreCategoriaPublicacion = nombre,
                    descripcionCategoriaPublicacion =
                        descripcion,
                    colorHex = color,
                    orden = orden,
                    activo = true
                });

            await noticiasDb.SaveChangesAsync(cancellationToken);
            return Creado("Tipo de publicación");
        }

        private async Task<ActionResult>
            GuardarCategoriaAlbumAsync(
                JsonElement datos,
                int? reactivarId,
                bool creacionConfirmada,
                CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(
                    ObtenerString(
                        datos,
                        "NombreCategoria",
                        "nombreCategoria"));

            string descripcion =
                NormalizarDescripcion(
                    ObtenerString(
                        datos,
                        "Descripcion",
                        "descripcion"));

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre de la categoría es obligatorio."
                });
            }

            CategoriaAlbumBotanico? reactivar =
                reactivarId.HasValue
                    ? await db.CategoriasAlbumBotanico
                        .FirstOrDefaultAsync(
                            x =>
                                x.categoriaAlbumBotanicoId ==
                                reactivarId.Value,
                            cancellationToken)
                    : null;

            bool activoDuplicado =
                await db.CategoriasAlbumBotanico
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            (!reactivarId.HasValue ||
                             x.categoriaAlbumBotanicoId !=
                                reactivarId.Value) &&
                            x.activo &&
                            x.nombreCategoria.ToUpper() == nombre,
                        cancellationToken);

            if (activoDuplicado)
                return ConflictoIdentidad("categoría del álbum");

            if (reactivar != null)
            {
                reactivar.nombreCategoria = nombre;
                reactivar.descripcion = descripcion;
                reactivar.activo = true;

                await db.SaveChangesAsync(cancellationToken);
                return Reactivado("Categoría del álbum");
            }

            CategoriaAlbumBotanico? inactivo =
                await db.CategoriasAlbumBotanico
                    .FirstOrDefaultAsync(
                        x =>
                            !x.activo &&
                            x.nombreCategoria.ToUpper() == nombre,
                        cancellationToken);

            if (inactivo != null)
            {
                return ConflictoInactivo(
                    new CatalogoEliminadoItemDto
                    {
                        Id =
                            inactivo.categoriaAlbumBotanicoId,
                        Catalogo =
                            Catalogos.CategoriaAlbum,
                        Titulo = inactivo.nombreCategoria,
                        Subtitulo = inactivo.descripcion ?? string.Empty,
                        Codigo = "ÁLBUM",
                        Activo = false
                    },
                    "Ya existe una categoría del álbum eliminada con ese nombre.",
                    false);
            }

            db.CategoriasAlbumBotanico.Add(
                new CategoriaAlbumBotanico
                {
                    nombreCategoria = nombre,
                    descripcion = descripcion,
                    activo = true
                });

            await db.SaveChangesAsync(cancellationToken);
            return Creado("Categoría del álbum");
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            string catalogo,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            string? interfaz =
                catalogo switch
                {
                    Catalogos.Pais => "paisPage",
                    Catalogos.Departamento => "departamentoPage",
                    Catalogos.Municipio => "municipioPage",
                    Catalogos.Rol => "rolPage",
                    Catalogos.ElementoQuimico => "elementoQuimicoPage",
                    Catalogos.TipoCultivo => "tipoCultivoPage",
                    Catalogos.TipoAnalisis => "tipoAnalisisSueloPage",
                    Catalogos.Usuario => "userPage",
                    Catalogos.Terreno => "terrenoPage",
                    Catalogos.ExtraccionNutriente =>
                        "extraccionNutrientePage",
                    Catalogos.RangoNutriente => "rangoNutrientePage",
                    Catalogos.CategoriaPublicacion =>
                        "categoriaPublicacionPage",
                    Catalogos.CategoriaAlbum => "albumFotosPage",
                    _ => null
                };

            if (string.IsNullOrWhiteSpace(interfaz))
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El catálogo solicitado no admite reactivación."
                });
            }

            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    interfaz,
                    permiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            string accion =
                permiso switch
                {
                    TipoPermisoApi.Leer =>
                        "consultar los registros eliminados",
                    TipoPermisoApi.Agregar =>
                        "crear el registro",
                    TipoPermisoApi.Actualizar =>
                        "reactivar el registro",
                    _ =>
                        "realizar esta operación"
                };

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        resultado.CodigoEstado ==
                            StatusCodes.Status401Unauthorized
                            ? resultado.Mensaje
                            : $"No tiene permiso para {accion} en este catálogo."
                });
        }

        // ==========================================================
        // RESPUESTAS Y UTILIDADES
        // ==========================================================

        private ConflictObjectResult ConflictoInactivo(
            CatalogoEliminadoItemDto registro,
            string mensaje,
            bool puedeCrearNuevo) =>
            Conflict(new
            {
                success = false,
                message = mensaje,
                data = new
                {
                    registro,
                    puedeCrearNuevo
                }
            });

        private ConflictObjectResult ConflictoIdentidad(
            string entidad) =>
            Conflict(new
            {
                success = false,
                message =
                    $"Ya existe un {entidad} activo con la misma identidad."
            });

        private NotFoundObjectResult NoEncontrado(
            string entidad) =>
            NotFound(new
            {
                success = false,
                message =
                    $"{entidad} no existe."
            });

        private ConflictObjectResult YaActivo(
            string entidad) =>
            Conflict(new
            {
                success = false,
                message =
                    $"{entidad} ya se encuentra activo."
            });

        private OkObjectResult Reactivado(
            string entidad) =>
            Ok(new
            {
                success = true,
                message =
                    $"{entidad} reactivado correctamente."
            });

        private ObjectResult Creado(
            string entidad) =>
            StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    success = true,
                    message =
                        $"{entidad} creado correctamente."
                });

        private CatalogoEliminadoItemDto CrearItemPais(
            Pais pais) =>
            new()
            {
                Id = pais.PaisId,
                Catalogo = Catalogos.Pais,
                Titulo = pais.NombrePais,
                Subtitulo =
                    "Código ISO: " +
                    pais.CodigoISOPais,
                Codigo = pais.CodigoISOPais,
                Activo = pais.Activo
            };

        private static string NormalizarCatalogo(
            string? valor) =>
            (valor ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        private static string NormalizarNombre(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarDescripcion(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

        private static string NormalizarColor(
            string? valor)
        {
            string color =
                (valor ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            if (color.Length == 7 &&
                color[0] == '#' &&
                color.Skip(1).All(Uri.IsHexDigit))
            {
                return color;
            }

            return "#3B655B";
        }

        private static string ObtenerString(
            JsonElement datos,
            params string[] nombres)
        {
            if (!TryGetProperty(
                    datos,
                    nombres,
                    out JsonElement valor))
            {
                return string.Empty;
            }

            return valor.ValueKind switch
            {
                JsonValueKind.String =>
                    valor.GetString() ??
                    string.Empty,

                JsonValueKind.Number =>
                    valor.GetRawText(),

                _ => string.Empty
            };
        }

        private static int ObtenerInt(
            JsonElement datos,
            params string[] nombres)
        {
            if (!TryGetProperty(
                    datos,
                    nombres,
                    out JsonElement valor))
            {
                return 0;
            }

            if (valor.ValueKind ==
                    JsonValueKind.Number &&
                valor.TryGetInt32(
                    out int numero))
            {
                return numero;
            }

            return int.TryParse(
                valor.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out numero)
                    ? numero
                    : 0;
        }

        private static decimal ObtenerDecimal(
            JsonElement datos,
            params string[] nombres)
        {
            if (!TryGetProperty(
                    datos,
                    nombres,
                    out JsonElement valor))
            {
                return 0;
            }

            if (valor.ValueKind ==
                    JsonValueKind.Number &&
                valor.TryGetDecimal(
                    out decimal numero))
            {
                return numero;
            }

            return decimal.TryParse(
                valor.ToString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out numero)
                    ? numero
                    : 0;
        }

        private static DateOnly? ObtenerDateOnly(
            JsonElement datos,
            params string[] nombres)
        {
            string valor =
                ObtenerString(
                    datos,
                    nombres);

            if (string.IsNullOrWhiteSpace(valor))
                return null;

            if (DateOnly.TryParseExact(
                    valor,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly fecha))
            {
                return fecha;
            }

            return DateOnly.TryParse(
                valor,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out fecha)
                    ? fecha
                    : null;
        }

        private static bool EsMayorDeEdad(
            DateOnly? fechaNacimiento)
        {
            if (!fechaNacimiento.HasValue)
                return false;

            DateOnly hoy =
                DateOnly.FromDateTime(
                    DateTime.Today);

            int edad =
                hoy.Year -
                fechaNacimiento.Value.Year;

            if (hoy <
                fechaNacimiento.Value.AddYears(edad))
            {
                edad--;
            }

            return edad >= 18;
        }

        private static bool EsIdentificacionValida(
            string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            if (texto.Length != 16 ||
                texto[3] != '-' ||
                texto[10] != '-')
            {
                return false;
            }

            return texto
                    .Take(3)
                    .All(char.IsDigit) &&
                texto
                    .Skip(4)
                    .Take(6)
                    .All(char.IsDigit) &&
                texto
                    .Skip(11)
                    .Take(4)
                    .All(char.IsDigit) &&
                char.IsLetter(texto[15]);
        }

        private static bool TryGetProperty(
            JsonElement datos,
            IEnumerable<string> nombres,
            out JsonElement valor)
        {
            foreach (
                JsonProperty propiedad
                in datos.EnumerateObject())
            {
                if (nombres.Any(nombre =>
                        string.Equals(
                            propiedad.Name,
                            nombre,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    valor =
                        propiedad.Value;

                    return true;
                }
            }

            valor =
                default;

            return false;
        }

        private static class Catalogos
        {
            public const string Pais = "pais";
            public const string Departamento = "departamento";
            public const string Municipio = "municipio";
            public const string Rol = "rol";
            public const string ElementoQuimico = "elemento-quimico";
            public const string TipoCultivo = "tipo-cultivo";
            public const string TipoAnalisis = "tipo-analisis";
            public const string Usuario = "usuario";
            public const string Terreno = "terreno";
            public const string ExtraccionNutriente =
                "extraccion-nutriente";
            public const string RangoNutriente =
                "rango-nutriente";
            public const string CategoriaPublicacion =
                "categoria-publicacion";
            public const string CategoriaAlbum =
                "categoria-album";
        }

        private sealed class CatalogoEliminadoItemDto
        {
            public int Id { get; set; }
            public string Catalogo { get; set; } = string.Empty;
            public string Titulo { get; set; } = string.Empty;
            public string Subtitulo { get; set; } = string.Empty;
            public string Detalle { get; set; } = string.Empty;
            public string Codigo { get; set; } = string.Empty;
            public bool Activo { get; set; }
        }
    }
}
