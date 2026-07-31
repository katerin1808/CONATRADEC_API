using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Consulta administrativa especializada en los análisis de suelo.
/// Este controlador es únicamente de lectura y no altera la lógica de cálculo.
/// </summary>
[ApiController]
[Route("api/auditoria-analisis")]
public sealed class AuditoriaAnalisisController : ControllerBase
{
    public const string PermisoAuditoria = "auditoriaAnalisisPage";

    private static readonly SemaphoreSlim InfraestructuraLock = new(1, 1);
    private static bool infraestructuraVerificada;

    private readonly DBContext db;
    private readonly BitacoraDbContext bitacoraDb;

    public AuditoriaAnalisisController(
        DBContext db,
        BitacoraDbContext bitacoraDb)
    {
        this.db = db;
        this.bitacoraDb = bitacoraDb;
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
        [FromQuery] string? buscar,
        [FromQuery] int? usuarioId,
        [FromQuery] string? laboratorio,
        [FromQuery] DateOnly? fechaLaboratorioDesde,
        [FromQuery] DateOnly? fechaLaboratorioHasta,
        [FromQuery] DateTime? fechaRegistroDesde,
        [FromQuery] DateTime? fechaRegistroHasta,
        [FromQuery] string? origen,
        [FromQuery] string? estado,
        [FromQuery] bool? tieneFormula,
        [FromQuery] bool? tieneEnmienda,
        [FromQuery] bool? tieneMixta,
        [FromQuery] decimal? phMinimo,
        [FromQuery] decimal? phMaximo,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 25,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarAccesoAsync(
            usuarioSesionId,
            cancellationToken);

        if (acceso != null)
            return acceso;

        await AsegurarInfraestructuraAsync(cancellationToken);

        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

        Dictionary<string, OperacionOffline> operacionesOffline =
            await ObtenerOperacionesOfflineAsync(cancellationToken);

        string[] identificadoresOffline = operacionesOffline.Keys.ToArray();
        IQueryable<FilaAuditoria> consulta = CrearConsultaBase();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            string texto = buscar.Trim();
            consulta = consulta.Where(x =>
                x.Identificador.Contains(texto) ||
                x.Laboratorio.Contains(texto) ||
                x.CodigoTerreno.Contains(texto) ||
                x.Propietario.Contains(texto) ||
                x.UsuarioNombre.Contains(texto) ||
                x.UsuarioCuenta.Contains(texto));
        }

        if (usuarioId is > 0)
            consulta = consulta.Where(x => x.UsuarioId == usuarioId.Value);

        if (!string.IsNullOrWhiteSpace(laboratorio))
        {
            string valor = laboratorio.Trim();
            consulta = consulta.Where(x => x.Laboratorio == valor);
        }

        if (fechaLaboratorioDesde.HasValue)
        {
            consulta = consulta.Where(x =>
                x.FechaLaboratorio >= fechaLaboratorioDesde.Value);
        }

        if (fechaLaboratorioHasta.HasValue)
        {
            consulta = consulta.Where(x =>
                x.FechaLaboratorio <= fechaLaboratorioHasta.Value);
        }

        if (fechaRegistroDesde.HasValue)
        {
            DateTime desde = fechaRegistroDesde.Value.Date;
            consulta = consulta.Where(x => x.FechaRegistro >= desde);
        }

        if (fechaRegistroHasta.HasValue)
        {
            DateTime hastaExclusiva = fechaRegistroHasta.Value.Date.AddDays(1);
            consulta = consulta.Where(x => x.FechaRegistro < hastaExclusiva);
        }

        if (phMinimo.HasValue)
            consulta = consulta.Where(x => x.Ph >= phMinimo.Value);

        if (phMaximo.HasValue)
            consulta = consulta.Where(x => x.Ph <= phMaximo.Value);

        if (tieneFormula.HasValue)
            consulta = consulta.Where(x => x.TieneFormula == tieneFormula.Value);

        if (tieneEnmienda.HasValue)
            consulta = consulta.Where(x => x.TieneEnmienda == tieneEnmienda.Value);

        if (tieneMixta.HasValue)
            consulta = consulta.Where(x => x.TieneMixta == tieneMixta.Value);

        if (!string.IsNullOrWhiteSpace(origen))
        {
            string valor = origen.Trim().ToUpperInvariant();

            if (valor == "OFFLINE")
            {
                consulta = identificadoresOffline.Length == 0
                    ? consulta.Where(_ => false)
                    : consulta.Where(x =>
                        identificadoresOffline.Contains(x.Identificador));
            }
            else if (valor == "ONLINE" && identificadoresOffline.Length > 0)
            {
                consulta = consulta.Where(x =>
                    !identificadoresOffline.Contains(x.Identificador));
            }
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            consulta = estado.Trim().ToUpperInvariant() switch
            {
                "ACTIVO" => consulta.Where(x =>
                    x.AnalisisActivo && x.CalculoActivo),
                "INACTIVO" => consulta.Where(x =>
                    !x.AnalisisActivo || !x.CalculoActivo),
                "INCONSISTENTE" => consulta.Where(x =>
                    x.TieneInconsistencia),
                "CORRECTO" => consulta.Where(x =>
                    x.AnalisisActivo &&
                    x.CalculoActivo &&
                    !x.TieneInconsistencia),
                _ => consulta
            };
        }

        int totalRegistros = await consulta.CountAsync(cancellationToken);
        int totalActivos = await consulta.CountAsync(
            x => x.AnalisisActivo && x.CalculoActivo,
            cancellationToken);
        int totalInactivos = await consulta.CountAsync(
            x => !x.AnalisisActivo || !x.CalculoActivo,
            cancellationToken);
        int totalInconsistentes = await consulta.CountAsync(
            x => x.TieneInconsistencia,
            cancellationToken);

        List<string> identificadoresFiltrados = await consulta
            .Select(x => x.Identificador)
            .ToListAsync(cancellationToken);

        int totalOffline = identificadoresFiltrados.Count(
            operacionesOffline.ContainsKey);
        int totalOnline = totalRegistros - totalOffline;

        List<FilaAuditoria> filas = await consulta
            .OrderByDescending(x => x.FechaRegistro)
            .ThenByDescending(x => x.FechaCalculo)
            .ThenByDescending(x => x.AnalisisSueloCalculoId)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(cancellationToken);

        var items = filas.Select(x =>
        {
            operacionesOffline.TryGetValue(
                x.Identificador,
                out OperacionOffline? operacionOffline);

            return new
            {
                analisisSueloId = x.AnalisisSueloId,
                analisisSueloCalculoId = x.AnalisisSueloCalculoId,
                identificador = x.Identificador,
                laboratorio = x.Laboratorio,
                fechaLaboratorio = x.FechaLaboratorio,
                fechaRegistro = x.FechaRegistro,
                fechaCalculo = x.FechaCalculo,
                terrenoId = x.TerrenoId,
                codigoTerreno = x.CodigoTerreno,
                propietario = x.Propietario,
                usuarioId = x.UsuarioId,
                usuarioNombre = NombreUsuario(x),
                ph = x.Ph,
                activo = x.AnalisisActivo && x.CalculoActivo,
                origen = operacionOffline == null ? "ONLINE" : "OFFLINE",
                tipoOperacionOffline = operacionOffline?.TipoOperacion,
                fechaSincronizacionUtc =
                    operacionOffline?.FechaCompletadoUtc ??
                    operacionOffline?.FechaRecepcionUtc,
                tieneFormula = x.TieneFormula,
                tieneEnmienda = x.TieneEnmienda,
                tieneMixta = x.TieneMixta,
                tieneInconsistencia = x.TieneInconsistencia,
                estado = ObtenerEstado(x),
                alertas = ConstruirAlertas(x)
            };
        }).ToList();

        int totalPaginas = totalRegistros == 0
            ? 1
            : (int)Math.Ceiling(totalRegistros / (double)tamanoPagina);

        return Ok(new
        {
            success = true,
            message = "Auditoría de análisis obtenida correctamente.",
            data = new
            {
                items,
                pagina,
                tamanoPagina,
                totalRegistros,
                totalPaginas,
                resumen = new
                {
                    total = totalRegistros,
                    activos = totalActivos,
                    inactivos = totalInactivos,
                    inconsistentes = totalInconsistentes,
                    online = totalOnline,
                    offline = totalOffline
                }
            }
        });
    }

    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos(
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarAccesoAsync(
            usuarioSesionId,
            cancellationToken);

        if (acceso != null)
            return acceso;

        await AsegurarInfraestructuraAsync(cancellationToken);

        var usuariosBase = await (
            from calculo in db.AnalisisSueloCalculos.AsNoTracking()
            join usuario in db.Usuarios.AsNoTracking()
                on calculo.usuarioId equals (int?)usuario.UsuarioId
            select new
            {
                usuario.UsuarioId,
                usuario.nombreCompletoUsuario,
                usuario.nombreUsuario
            })
            .Distinct()
            .OrderBy(x => x.nombreCompletoUsuario)
            .ThenBy(x => x.nombreUsuario)
            .ToListAsync(cancellationToken);

        var usuarios = usuariosBase.Select(x => new
        {
            usuarioId = x.UsuarioId,
            nombre = string.IsNullOrWhiteSpace(x.nombreCompletoUsuario)
                ? x.nombreUsuario
                : x.nombreCompletoUsuario
        }).ToList();

        List<string> laboratorios = await db.AnalisisSuelos
            .AsNoTracking()
            .Where(x => x.laboratorioAnalasisSuelo != "")
            .Select(x => x.laboratorioAnalasisSuelo)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = new
            {
                usuarios,
                laboratorios,
                origenes = new[]
                {
                    new { valor = "ONLINE", nombre = "En línea" },
                    new { valor = "OFFLINE", nombre = "Sincronizado sin conexión" }
                },
                estados = new[]
                {
                    new { valor = "CORRECTO", nombre = "Correcto" },
                    new { valor = "ACTIVO", nombre = "Activo" },
                    new { valor = "INACTIVO", nombre = "Inactivo" },
                    new { valor = "INCONSISTENTE", nombre = "Con inconsistencias" }
                }
            }
        });
    }

    [HttpGet("{analisisSueloId:int}")]
    public async Task<IActionResult> ObtenerDetalle(
        int analisisSueloId,
        [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
        CancellationToken cancellationToken = default)
    {
        IActionResult? acceso = await ValidarAccesoAsync(
            usuarioSesionId,
            cancellationToken);

        if (acceso != null)
            return acceso;

        await AsegurarInfraestructuraAsync(cancellationToken);

        int? calculoId = await db.AnalisisSueloCalculos
            .AsNoTracking()
            .Where(x => x.analisisSueloId == analisisSueloId)
            .OrderByDescending(x => x.fechaCalculo)
            .ThenByDescending(x => x.analisisSueloCalculoId)
            .Select(x => (int?)x.analisisSueloCalculoId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!calculoId.HasValue)
        {
            return NotFound(new
            {
                success = false,
                message = "El análisis no existe o no posee un cálculo asociado."
            });
        }

        int idCalculo = calculoId.Value;

        var cabecera = await (
            from analisis in db.AnalisisSuelos.AsNoTracking()
            join calculo in db.AnalisisSueloCalculos.AsNoTracking()
                on analisis.analisisSueloId equals calculo.analisisSueloId
            join terrenoBase in db.Terreno.AsNoTracking()
                on calculo.terrenoId equals terrenoBase.terrenoId
                into terrenosJoin
            from terreno in terrenosJoin.DefaultIfEmpty()
            join usuario in db.Usuarios.AsNoTracking()
                on calculo.usuarioId equals (int?)usuario.UsuarioId
                into usuariosJoin
            from usuario in usuariosJoin.DefaultIfEmpty()
            join cultivo in db.TipoCultivos.AsNoTracking()
                on calculo.tipoCultivoId equals cultivo.tipoCultivoId
                into cultivosJoin
            from cultivo in cultivosJoin.DefaultIfEmpty()
            join tipoAnalisis in db.TipoAnalisisSuelos.AsNoTracking()
                on calculo.tipoAnalisisSueloId equals tipoAnalisis.tipoAnalisisSueloId
                into tiposJoin
            from tipoAnalisis in tiposJoin.DefaultIfEmpty()
            where analisis.analisisSueloId == analisisSueloId &&
                  calculo.analisisSueloCalculoId == idCalculo
            select new
            {
                analisis.analisisSueloId,
                calculo.analisisSueloCalculoId,
                analisis.identificadorAnalisisSuelo,
                analisis.laboratorioAnalasisSuelo,
                analisis.fechaAnalisisSuelo,
                analisis.fechaCreacionAnalisisSuelo,
                analisisActivo = analisis.activo,
                calculo.fechaCalculo,
                calculoActivo = calculo.activo,
                calculo.phAnalisisSuelo,
                calculo.materiaOrganica,
                calculo.acidezTotal,
                calculo.cantidadQuintalesOro,
                calculo.tamanoFinca,
                calculo.recomendacionGeneral,
                calculo.observacion,
                terrenoId = calculo.terrenoId,
                codigoTerreno = terreno == null
                    ? string.Empty
                    : terreno.codigoTerreno,
                NombrePropietario = terreno == null
                    ? string.Empty
                    : terreno.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            relacion.Propietario.nombreCompleto)
                        .FirstOrDefault() ??
                      string.Empty,
                direccionTerreno = terreno == null
                    ? string.Empty
                    : terreno.direccionTerreno,
                extensionManzanaTerreno = terreno == null
                    ? 0
                    : terreno.extensionManzanaTerreno,
                terrenoActivo = terreno != null && terreno.activo,
                calculo.usuarioId,
                usuarioNombre = usuario == null
                    ? string.Empty
                    : usuario.nombreCompletoUsuario,
                usuarioCuenta = usuario == null
                    ? string.Empty
                    : usuario.nombreUsuario,
                usuarioActivo = usuario != null && usuario.activo,
                tipoCultivo = cultivo == null
                    ? string.Empty
                    : cultivo.nombreTipoCultivo,
                tipoAnalisis = tipoAnalisis == null
                    ? string.Empty
                    : tipoAnalisis.nombreTipoAnalisisSuelo
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (cabecera == null)
        {
            return NotFound(new
            {
                success = false,
                message = "El análisis solicitado no existe."
            });
        }

        var elementosIngresados = await db.AnalisisSueloElementos
            .AsNoTracking()
            .Where(x => x.analisisSueloId == analisisSueloId)
            .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
            .Select(x => new
            {
                x.analisisSueloElementoQuimicoId,
                x.elementoQuimicosId,
                simbolo = x.ElementoQuimico.simboloElementoQuimico,
                nombre = x.ElementoQuimico.nombreElementoQuimico,
                cantidad = x.cantidadElemento,
                unidad = x.UnidadMedida.nombreUnidadMedida,
                x.activo
            })
            .ToListAsync(cancellationToken);

        var elementosCalculados = await db.AnalisisSueloCalculoElementoQuimicos
            .AsNoTracking()
            .Where(x => x.analisisSueloCalculoId == idCalculo)
            .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
            .Select(x => new
            {
                x.analisisSueloCalculoElementoQuimicoId,
                x.elementoQuimicosId,
                simbolo = x.ElementoQuimico.simboloElementoQuimico,
                nombre = x.ElementoQuimico.nombreElementoQuimico,
                x.cantidadIngresada,
                x.cantidadConvertidaLbMz,
                x.requerimientoCalculado,
                x.clasificacion,
                x.observacion,
                x.incluirCalculosComplementarios,
                x.activo
            })
            .ToListAsync(cancellationToken);

        var formula = await db.formulaNutricional
            .AsNoTracking()
            .Where(x => x.analisisSueloCalculoId == idCalculo)
            .OrderByDescending(x => x.fechaCreacion)
            .Select(x => new
            {
                x.formulaNutricionalId,
                x.nombreFormula,
                x.fechaCreacion,
                x.totalLibras,
                x.mezclaTotalQq,
                x.precioTotalFormula,
                x.totalPlantas,
                x.totalAplicaciones,
                x.esComplementoFertilizacionMixta,
                x.activo,
                cantidadDetalles = x.detalles == null ? 0 : x.detalles.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        var enmienda = await db.enmiendaCalcarea
            .AsNoTracking()
            .Where(x => x.analisisSueloCalculoId == idCalculo)
            .OrderByDescending(x => x.fechaCreacion)
            .Select(x => new
            {
                x.enmiendaCalcareaId,
                x.nombreAnalisis,
                x.fechaCreacion,
                x.ph,
                x.cice,
                x.saturacionActual,
                x.saturacionDeseada,
                x.necesidadEncaladoLbMz,
                x.dosisPlantaAnualOz,
                x.totalPlantas,
                x.totalAplicaciones,
                x.activo
            })
            .FirstOrDefaultAsync(cancellationToken);

        var mixta = await db.fertilizacionMixta
            .AsNoTracking()
            .Where(x => x.analisisSueloCalculoId == idCalculo)
            .OrderByDescending(x => x.fechaCalculo)
            .Select(x => new
            {
                x.fertilizacionMixtaId,
                x.fechaCalculo,
                x.observacion,
                x.esComplementoBalance,
                x.activo,
                cantidadFuentes = x.fuentes == null ? 0 : x.fuentes.Count,
                cantidadDetalles = x.detalles == null ? 0 : x.detalles.Count
            })
            .FirstOrDefaultAsync(cancellationToken);

        var inconsistencias = new List<string>();

        if (cabecera.analisisActivo != cabecera.calculoActivo)
            inconsistencias.Add("El estado del análisis no coincide con el cálculo principal.");

        if (!cabecera.terrenoActivo)
            inconsistencias.Add("El terreno relacionado se encuentra inactivo.");

        if (!cabecera.usuarioActivo)
            inconsistencias.Add("El usuario responsable no existe o está inactivo.");

        if (!elementosIngresados.Any(x => x.activo))
            inconsistencias.Add("No existen elementos químicos de laboratorio activos.");

        if (!elementosCalculados.Any(x => x.activo))
            inconsistencias.Add("No existen resultados de elementos químicos activos.");

        if (formula is { activo: true, cantidadDetalles: 0 })
            inconsistencias.Add("La fórmula nutricional está activa, pero no contiene detalles.");

        if (mixta != null && mixta.activo &&
            (mixta.cantidadFuentes == 0 || mixta.cantidadDetalles == 0))
        {
            inconsistencias.Add(
                "La fertilización mixta está activa, pero no contiene todas sus fuentes o detalles.");
        }

        Dictionary<string, OperacionOffline> operacionesOffline =
            await ObtenerOperacionesOfflineAsync(
                cancellationToken,
                cabecera.identificadorAnalisisSuelo);

        operacionesOffline.TryGetValue(
            cabecera.identificadorAnalisisSuelo,
            out OperacionOffline? operacionOffline);

        var llavesEntidades = new List<string>
        {
            $"analisisSueloId={analisisSueloId}",
            $"analisisSueloCalculoId={idCalculo}"
        };

        llavesEntidades.AddRange(elementosIngresados.Select(x =>
            $"analisisSueloElementoQuimicoId={x.analisisSueloElementoQuimicoId}"));
        llavesEntidades.AddRange(elementosCalculados.Select(x =>
            $"analisisSueloCalculoElementoQuimicoId={x.analisisSueloCalculoElementoQuimicoId}"));

        if (formula != null)
            llavesEntidades.Add($"formulaNutricionalId={formula.formulaNutricionalId}");

        if (enmienda != null)
            llavesEntidades.Add($"enmiendaCalcareaId={enmienda.enmiendaCalcareaId}");

        if (mixta != null)
            llavesEntidades.Add($"fertilizacionMixtaId={mixta.fertilizacionMixtaId}");

        string[] llaves = llavesEntidades.Distinct().ToArray();

        var historial = await bitacoraDb.BitacoraDetalles
            .AsNoTracking()
            .Where(x => llaves.Contains(x.entidadId))
            .OrderByDescending(x => x.fechaHoraUtc)
            .Take(250)
            .Select(x => new
            {
                x.bitacoraDetalleId,
                x.fechaHoraUtc,
                x.entidad,
                x.entidadId,
                x.operacion,
                x.valoresAnteriores,
                x.valoresNuevos,
                x.propiedadesModificadas,
                x.bitacoraId,
                usuarioId = x.bitacora.usuarioId,
                usuarioNombre = x.bitacora.usuarioNombre,
                rolNombre = x.bitacora.rolNombre,
                accion = x.bitacora.accion,
                modulo = x.bitacora.modulo,
                endpoint = x.bitacora.endpoint,
                paginaOrigen = x.bitacora.paginaOrigen,
                exitoso = x.bitacora.exitoso,
                codigoEstado = x.bitacora.codigoEstado,
                dispositivo = x.bitacora.dispositivo,
                plataforma = x.bitacora.plataforma,
                versionApp = x.bitacora.versionApp,
                correlationId = x.bitacora.correlationId
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Detalle de auditoría obtenido correctamente.",
            data = new
            {
                resumen = new
                {
                    cabecera.analisisSueloId,
                    cabecera.analisisSueloCalculoId,
                    identificador = cabecera.identificadorAnalisisSuelo,
                    laboratorio = cabecera.laboratorioAnalasisSuelo,
                    fechaLaboratorio = cabecera.fechaAnalisisSuelo,
                    fechaRegistro = cabecera.fechaCreacionAnalisisSuelo,
                    cabecera.fechaCalculo,
                    activo = cabecera.analisisActivo && cabecera.calculoActivo,
                    estado = inconsistencias.Count > 0
                        ? "INCONSISTENTE"
                        : cabecera.analisisActivo && cabecera.calculoActivo
                            ? "CORRECTO"
                            : "INACTIVO",
                    origen = operacionOffline == null ? "ONLINE" : "OFFLINE",
                    cabecera.usuarioId,
                    usuarioNombre = string.IsNullOrWhiteSpace(cabecera.usuarioNombre)
                        ? cabecera.usuarioCuenta
                        : cabecera.usuarioNombre,
                    terreno = new
                    {
                        cabecera.terrenoId,
                        cabecera.codigoTerreno,
                        propietario = cabecera.NombrePropietario,
                        direccion = cabecera.direccionTerreno,
                        extensionManzanas = cabecera.extensionManzanaTerreno,
                        activo = cabecera.terrenoActivo
                    },
                    cabecera.tipoCultivo,
                    cabecera.tipoAnalisis,
                    ph = cabecera.phAnalisisSuelo,
                    cabecera.materiaOrganica,
                    cabecera.acidezTotal,
                    cabecera.cantidadQuintalesOro,
                    cabecera.tamanoFinca,
                    cabecera.recomendacionGeneral,
                    cabecera.observacion
                },
                procedenciaOffline = operacionOffline,
                modulos = new
                {
                    requerimientoAnual = true,
                    formulaNutricional = formula,
                    enmiendaCalcarea = enmienda,
                    fertilizacionMixta = mixta
                },
                elementosIngresados,
                elementosCalculados,
                inconsistencias,
                historial
            }
        });
    }

    private async Task AsegurarInfraestructuraAsync(
        CancellationToken cancellationToken)
    {
        if (infraestructuraVerificada)
            return;

        await InfraestructuraLock.WaitAsync(cancellationToken);

        try
        {
            if (infraestructuraVerificada)
                return;

            const string sql = """
                IF OBJECT_ID(N'dbo.bitacoraDetalle', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.indexes
                       WHERE name = N'IX_bitacoraDetalle_entidad_entidadId'
                         AND object_id = OBJECT_ID(N'dbo.bitacoraDetalle')
                   )
                BEGIN
                    CREATE INDEX IX_bitacoraDetalle_entidad_entidadId
                        ON dbo.bitacoraDetalle(entidad, entidadId);
                END;

                IF OBJECT_ID(N'dbo.analisisOfflineOperacion', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.indexes
                       WHERE name = N'IX_analisisOfflineOperacion_identificador_fecha'
                         AND object_id = OBJECT_ID(N'dbo.analisisOfflineOperacion')
                   )
                BEGIN
                    CREATE INDEX IX_analisisOfflineOperacion_identificador_fecha
                        ON dbo.analisisOfflineOperacion
                           (identificadorAnalisis, fechaRecepcionUtc DESC);
                END;
                """;

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);
            }
            catch (DbException)
            {
                // Algunos usuarios SQL de producción no poseen permiso DDL.
                // La consulta continúa; únicamente queda sin los índices opcionales.
            }

            infraestructuraVerificada = true;
        }
        finally
        {
            InfraestructuraLock.Release();
        }
    }

    private IQueryable<FilaAuditoria> CrearConsultaBase()
    {
        IQueryable<int> idsUltimosCalculos = db.AnalisisSueloCalculos
            .AsNoTracking()
            .GroupBy(x => x.analisisSueloId)
            .Select(grupo => grupo.Max(x => x.analisisSueloCalculoId));

        return
            from calculo in db.AnalisisSueloCalculos.AsNoTracking()
            join analisis in db.AnalisisSuelos.AsNoTracking()
                on calculo.analisisSueloId equals analisis.analisisSueloId
            join terreno in db.Terreno.AsNoTracking()
                on calculo.terrenoId equals terreno.terrenoId
                into terrenosJoin
            from terreno in terrenosJoin.DefaultIfEmpty()
            join usuario in db.Usuarios.AsNoTracking()
                on calculo.usuarioId equals (int?)usuario.UsuarioId
                into usuariosJoin
            from usuario in usuariosJoin.DefaultIfEmpty()
            where idsUltimosCalculos.Contains(calculo.analisisSueloCalculoId)
            let tieneElementosIngresados = db.AnalisisSueloElementos.Any(x =>
                x.analisisSueloId == analisis.analisisSueloId && x.activo)
            let tieneElementosCalculados = db.AnalisisSueloCalculoElementoQuimicos.Any(x =>
                x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
            let tieneFormula = db.formulaNutricional.Any(x =>
                x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
            let tieneEnmienda = db.enmiendaCalcarea.Any(x =>
                x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
            let tieneMixta = db.fertilizacionMixta.Any(x =>
                x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
            select new FilaAuditoria
            {
                AnalisisSueloId = analisis.analisisSueloId,
                AnalisisSueloCalculoId = calculo.analisisSueloCalculoId,
                Identificador = analisis.identificadorAnalisisSuelo,
                Laboratorio = analisis.laboratorioAnalasisSuelo,
                FechaLaboratorio = analisis.fechaAnalisisSuelo,
                FechaRegistro = analisis.fechaCreacionAnalisisSuelo,
                FechaCalculo = calculo.fechaCalculo,
                AnalisisActivo = analisis.activo,
                CalculoActivo = calculo.activo,
                TerrenoId = calculo.terrenoId,
                CodigoTerreno = terreno == null ? string.Empty : terreno.codigoTerreno,
                Propietario = terreno == null
                    ? string.Empty
                    : terreno.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            relacion.Propietario.nombreCompleto)
                        .FirstOrDefault() ??
                      string.Empty,
                TerrenoActivo = terreno != null && terreno.activo,
                UsuarioId = calculo.usuarioId,
                UsuarioNombre = usuario == null ? string.Empty : usuario.nombreCompletoUsuario,
                UsuarioCuenta = usuario == null ? string.Empty : usuario.nombreUsuario,
                UsuarioActivo = usuario != null && usuario.activo,
                Ph = calculo.phAnalisisSuelo,
                TieneFormula = tieneFormula,
                TieneEnmienda = tieneEnmienda,
                TieneMixta = tieneMixta,
                TieneElementosIngresados = tieneElementosIngresados,
                TieneElementosCalculados = tieneElementosCalculados,
                TieneInconsistencia =
                    analisis.activo != calculo.activo ||
                    terreno == null ||
                    !terreno.activo ||
                    usuario == null ||
                    !usuario.activo ||
                    !tieneElementosIngresados ||
                    !tieneElementosCalculados
            };
    }

    private async Task<IActionResult?> ValidarAccesoAsync(
        int? usuarioId,
        CancellationToken cancellationToken)
    {
        if (!usuarioId.HasValue || usuarioId.Value <= 0)
        {
            return Unauthorized(new
            {
                success = false,
                message = "No se recibió una sesión válida."
            });
        }

        var usuario = await db.Usuarios
            .AsNoTracking()
            .Where(x => x.UsuarioId == usuarioId.Value && x.activo)
            .Select(x => new
            {
                x.UsuarioId,
                x.rolId,
                rolNombre = x.Rol == null ? string.Empty : x.Rol.nombreRol
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "El usuario no existe o se encuentra inactivo."
            });
        }

        bool esAdministrador = usuario.rolNombre.Contains(
            "ADMIN",
            StringComparison.OrdinalIgnoreCase);

        if (esAdministrador)
            return null;

        bool tienePermiso = await (
            from relacion in db.RolInterfaz.AsNoTracking()
            join interfaz in db.Interfaz.AsNoTracking()
                on relacion.interfazId equals interfaz.interfazId
            where relacion.rolId == usuario.rolId &&
                  interfaz.activo &&
                  relacion.leer == true &&
                  (interfaz.nombreInterfaz == PermisoAuditoria ||
                   interfaz.nombreInterfaz == "AuditoriaAnalisisWeb")
            select relacion.rolInterfazId)
            .AnyAsync(cancellationToken);

        if (!tienePermiso)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    success = false,
                    message = "El usuario no tiene permiso para consultar la auditoría de análisis."
                });
        }

        return null;
    }

    private async Task<Dictionary<string, OperacionOffline>>
        ObtenerOperacionesOfflineAsync(
            CancellationToken cancellationToken,
            string? identificador = null)
    {
        var resultado = new Dictionary<string, OperacionOffline>(
            StringComparer.OrdinalIgnoreCase);

        DbConnection connection = db.Database.GetDbConnection();
        bool cerrar = connection.State != ConnectionState.Open;

        try
        {
            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            await using DbCommand verificar = connection.CreateCommand();
            verificar.CommandText = """
                SELECT CASE
                    WHEN OBJECT_ID(N'dbo.analisisOfflineOperacion', N'U') IS NULL
                    THEN 0 ELSE 1 END;
                """;

            object? existeValor = await verificar.ExecuteScalarAsync(cancellationToken);
            if (Convert.ToInt32(existeValor) == 0)
                return resultado;

            await using DbCommand command = connection.CreateCommand();
            command.CommandText = string.IsNullOrWhiteSpace(identificador)
                ? """
                    SELECT
                        identificadorAnalisis,
                        tipoOperacion,
                        versionMotor,
                        hashPaquete,
                        fechaCalculoLocalUtc,
                        fechaRecepcionUtc,
                        fechaCompletadoUtc,
                        estado
                    FROM dbo.analisisOfflineOperacion
                    WHERE identificadorAnalisis IS NOT NULL
                      AND LTRIM(RTRIM(identificadorAnalisis)) <> N''
                    ORDER BY fechaRecepcionUtc DESC;
                    """
                : """
                    SELECT
                        identificadorAnalisis,
                        tipoOperacion,
                        versionMotor,
                        hashPaquete,
                        fechaCalculoLocalUtc,
                        fechaRecepcionUtc,
                        fechaCompletadoUtc,
                        estado
                    FROM dbo.analisisOfflineOperacion
                    WHERE identificadorAnalisis = @identificador
                    ORDER BY fechaRecepcionUtc DESC;
                    """;

            if (!string.IsNullOrWhiteSpace(identificador))
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@identificador";
                parameter.Value = identificador.Trim();
                command.Parameters.Add(parameter);
            }

            await using DbDataReader reader = await command.ExecuteReaderAsync(
                cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                string clave = reader.IsDBNull(0)
                    ? string.Empty
                    : reader.GetString(0).Trim();

                if (string.IsNullOrWhiteSpace(clave) || resultado.ContainsKey(clave))
                    continue;

                resultado[clave] = new OperacionOffline
                {
                    IdentificadorAnalisis = clave,
                    TipoOperacion = Texto(reader, 1),
                    VersionMotor = Texto(reader, 2),
                    HashPaquete = Texto(reader, 3),
                    FechaCalculoLocalUtc = Fecha(reader, 4),
                    FechaRecepcionUtc = Fecha(reader, 5),
                    FechaCompletadoUtc = Fecha(reader, 6),
                    Estado = Texto(reader, 7)
                };
            }
        }
        catch (DbException)
        {
            // La auditoría principal sigue funcionando en instalaciones antiguas
            // que todavía no tengan la tabla de sincronización offline.
        }
        finally
        {
            if (cerrar && connection.State == ConnectionState.Open)
                await connection.CloseAsync();
        }

        return resultado;
    }

    private static string Texto(DbDataReader reader, int indice) =>
        reader.IsDBNull(indice) ? string.Empty : reader.GetString(indice);

    private static DateTime? Fecha(DbDataReader reader, int indice) =>
        reader.IsDBNull(indice) ? null : reader.GetDateTime(indice);

    private static string NombreUsuario(FilaAuditoria fila) =>
        string.IsNullOrWhiteSpace(fila.UsuarioNombre)
            ? string.IsNullOrWhiteSpace(fila.UsuarioCuenta)
                ? "Sin usuario"
                : fila.UsuarioCuenta
            : fila.UsuarioNombre;

    private static string ObtenerEstado(FilaAuditoria fila)
    {
        if (fila.TieneInconsistencia)
            return "INCONSISTENTE";

        return fila.AnalisisActivo && fila.CalculoActivo
            ? "CORRECTO"
            : "INACTIVO";
    }

    private static List<string> ConstruirAlertas(FilaAuditoria fila)
    {
        var alertas = new List<string>();

        if (fila.AnalisisActivo != fila.CalculoActivo)
            alertas.Add("Estado diferente entre análisis y cálculo");
        if (!fila.TerrenoActivo)
            alertas.Add("Terreno inactivo o inexistente");
        if (!fila.UsuarioActivo)
            alertas.Add("Usuario inactivo o inexistente");
        if (!fila.TieneElementosIngresados)
            alertas.Add("Sin elementos de laboratorio activos");
        if (!fila.TieneElementosCalculados)
            alertas.Add("Sin elementos calculados activos");

        return alertas;
    }

    private sealed class FilaAuditoria
    {
        public int AnalisisSueloId { get; init; }
        public int AnalisisSueloCalculoId { get; init; }
        public string Identificador { get; init; } = string.Empty;
        public string Laboratorio { get; init; } = string.Empty;
        public DateOnly FechaLaboratorio { get; init; }
        public DateTime FechaRegistro { get; init; }
        public DateTime FechaCalculo { get; init; }
        public bool AnalisisActivo { get; init; }
        public bool CalculoActivo { get; init; }
        public int TerrenoId { get; init; }
        public string CodigoTerreno { get; init; } = string.Empty;
        public string Propietario { get; init; } = string.Empty;
        public bool TerrenoActivo { get; init; }
        public int? UsuarioId { get; init; }
        public string UsuarioNombre { get; init; } = string.Empty;
        public string UsuarioCuenta { get; init; } = string.Empty;
        public bool UsuarioActivo { get; init; }
        public decimal Ph { get; init; }
        public bool TieneFormula { get; init; }
        public bool TieneEnmienda { get; init; }
        public bool TieneMixta { get; init; }
        public bool TieneElementosIngresados { get; init; }
        public bool TieneElementosCalculados { get; init; }
        public bool TieneInconsistencia { get; init; }
    }

    private sealed class OperacionOffline
    {
        public string IdentificadorAnalisis { get; init; } = string.Empty;
        public string TipoOperacion { get; init; } = string.Empty;
        public string VersionMotor { get; init; } = string.Empty;
        public string HashPaquete { get; init; } = string.Empty;
        public DateTime? FechaCalculoLocalUtc { get; init; }
        public DateTime? FechaRecepcionUtc { get; init; }
        public DateTime? FechaCompletadoUtc { get; init; }
        public string Estado { get; init; } = string.Empty;
    }
}
