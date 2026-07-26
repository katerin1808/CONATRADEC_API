using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Inicializa de forma idempotente la estructura adicional del análisis
    /// de suelo y el sistema parametrizable de unidades y conversiones.
    ///
    /// No sobrescribe configuraciones existentes. Únicamente crea tablas,
    /// unidades o asociaciones que todavía no existan.
    /// </summary>
    public sealed class AnalisisSueloDatabaseInitializer
    {
        private readonly DBContext db;

        private readonly ILogger<
            AnalisisSueloDatabaseInitializer> logger;

        public AnalisisSueloDatabaseInitializer(
            DBContext db,
            ILogger<
                AnalisisSueloDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken =
                default)
        {
            await using var transaction =
                await db.Database
                    .BeginTransactionAsync(
                        cancellationToken);

            try
            {
                await AsegurarBanderaElementosAsync(
                    cancellationToken);

                await AsegurarTablasConversionesAsync(
                    cancellationToken);

                await AsegurarUnidadesBaseAsync(
                    cancellationToken);

                await AsegurarConfiguracionesBaseAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                logger.LogInformation(
                    "Estructura del análisis y configuraciones de unidades verificadas correctamente.");
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "No fue posible inicializar las unidades y conversiones del análisis de suelo.");

                throw;
            }
        }

        private async Task
            AsegurarBanderaElementosAsync(
                CancellationToken cancellationToken)
        {
            const string sql = """
DECLARE @ColumnaCreada BIT = 0;

IF OBJECT_ID(
       N'[dbo].[analisisSueloCalculoElementoQuimico]',
       N'U') IS NOT NULL
   AND COL_LENGTH(
       N'dbo.analisisSueloCalculoElementoQuimico',
       N'incluirCalculosComplementarios') IS NULL
BEGIN
    ALTER TABLE
        [dbo].[analisisSueloCalculoElementoQuimico]
    ADD
        [incluirCalculosComplementarios] BIT NOT NULL
        CONSTRAINT
            [DF_analisisCalculoElemento_incluirComplementarios]
        DEFAULT (1);

    SET @ColumnaCreada = 1;
END;

IF @ColumnaCreada = 1
BEGIN
    EXEC
    (
        N'
        UPDATE
            [dbo].[analisisSueloCalculoElementoQuimico]
        SET
            [incluirCalculosComplementarios] = 0
        WHERE
            UPPER
            (
                LTRIM
                (
                    RTRIM
                    (
                        ISNULL
                        (
                            [clasificacion],
                            SPACE(0)
                        )
                    )
                )
            ) = N''EXCESIVO'';
        '
    );
END;
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }

        private async Task
            AsegurarTablasConversionesAsync(
                CancellationToken cancellationToken)
        {
            const string sql = """
IF OBJECT_ID(
       N'[dbo].[elementoQuimicoUnidadMedida]',
       N'U') IS NULL
BEGIN
    CREATE TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    (
        [elementoQuimicoUnidadMedidaId]
            INT IDENTITY(1,1) NOT NULL,

        [elementoQuimicosId]
            INT NOT NULL,

        [unidadMedidaId]
            INT NOT NULL,

        [codigoFormulaConversion]
            NVARCHAR(50) NOT NULL,

        [factorPrincipal]
            DECIMAL(18,8) NOT NULL,

        [factorSecundario]
            DECIMAL(18,8) NOT NULL,

        [factorTerciario]
            DECIMAL(18,8) NOT NULL,

        [divisor]
            DECIMAL(18,8) NOT NULL,

        [desplazamiento]
            DECIMAL(18,8) NOT NULL,

        [unidadPredeterminada]
            BIT NOT NULL,

        [visibleEnFormulario]
            BIT NOT NULL,

        [orden]
            INT NOT NULL,

        [observacion]
            NVARCHAR(300) NULL,

        [activo]
            BIT NOT NULL,

        CONSTRAINT
            [PK_elementoQuimicoUnidadMedida]
        PRIMARY KEY
        (
            [elementoQuimicoUnidadMedidaId]
        ),

        CONSTRAINT
            [UQ_elementoQuimicoUnidadMedida]
        UNIQUE
        (
            [elementoQuimicosId],
            [unidadMedidaId]
        ),

        CONSTRAINT
            [FK_elementoUnidad_elemento]
        FOREIGN KEY
        (
            [elementoQuimicosId]
        )
        REFERENCES
            [dbo].[elementoQuimico]
            ([elementoQuimicosId]),

        CONSTRAINT
            [FK_elementoUnidad_unidad]
        FOREIGN KEY
        (
            [unidadMedidaId]
        )
        REFERENCES
            [dbo].[unidadMedida]
            ([unidadMedidaId])
    );

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_formula]
    DEFAULT (N'LINEAL')
    FOR [codigoFormulaConversion];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_factorPrincipal]
    DEFAULT (1)
    FOR [factorPrincipal];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_factorSecundario]
    DEFAULT (1)
    FOR [factorSecundario];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_factorTerciario]
    DEFAULT (1)
    FOR [factorTerciario];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_divisor]
    DEFAULT (1)
    FOR [divisor];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_desplazamiento]
    DEFAULT (0)
    FOR [desplazamiento];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_predeterminada]
    DEFAULT (0)
    FOR [unidadPredeterminada];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_visible]
    DEFAULT (1)
    FOR [visibleEnFormulario];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_orden]
    DEFAULT (0)
    FOR [orden];

    ALTER TABLE
        [dbo].[elementoQuimicoUnidadMedida]
    ADD CONSTRAINT
        [DF_elementoUnidad_activo]
    DEFAULT (1)
    FOR [activo];
END;

IF OBJECT_ID(
       N'[dbo].[materiaOrganicaUnidadMedida]',
       N'U') IS NULL
BEGIN
    CREATE TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    (
        [materiaOrganicaUnidadMedidaId]
            INT IDENTITY(1,1) NOT NULL,

        [unidadMedidaId]
            INT NOT NULL,

        [codigoFormulaConversion]
            NVARCHAR(50) NOT NULL,

        [factorPrincipal]
            DECIMAL(18,8) NOT NULL,

        [factorSecundario]
            DECIMAL(18,8) NOT NULL,

        [factorTerciario]
            DECIMAL(18,8) NOT NULL,

        [divisor]
            DECIMAL(18,8) NOT NULL,

        [desplazamiento]
            DECIMAL(18,8) NOT NULL,

        [unidadPredeterminada]
            BIT NOT NULL,

        [visibleEnFormulario]
            BIT NOT NULL,

        [orden]
            INT NOT NULL,

        [observacion]
            NVARCHAR(300) NULL,

        [activo]
            BIT NOT NULL,

        CONSTRAINT
            [PK_materiaOrganicaUnidadMedida]
        PRIMARY KEY
        (
            [materiaOrganicaUnidadMedidaId]
        ),

        CONSTRAINT
            [UQ_materiaOrganicaUnidadMedida]
        UNIQUE
        (
            [unidadMedidaId]
        ),

        CONSTRAINT
            [FK_materiaOrganicaUnidad_unidad]
        FOREIGN KEY
        (
            [unidadMedidaId]
        )
        REFERENCES
            [dbo].[unidadMedida]
            ([unidadMedidaId])
    );

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_formula]
    DEFAULT (N'LINEAL')
    FOR [codigoFormulaConversion];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_factorPrincipal]
    DEFAULT (1)
    FOR [factorPrincipal];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_factorSecundario]
    DEFAULT (1)
    FOR [factorSecundario];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_factorTerciario]
    DEFAULT (1)
    FOR [factorTerciario];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_divisor]
    DEFAULT (1)
    FOR [divisor];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_desplazamiento]
    DEFAULT (0)
    FOR [desplazamiento];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_predeterminada]
    DEFAULT (0)
    FOR [unidadPredeterminada];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_visible]
    DEFAULT (1)
    FOR [visibleEnFormulario];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_orden]
    DEFAULT (0)
    FOR [orden];

    ALTER TABLE
        [dbo].[materiaOrganicaUnidadMedida]
    ADD CONSTRAINT
        [DF_materiaUnidad_activo]
    DEFAULT (1)
    FOR [activo];
END;
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }

        private async Task AsegurarUnidadesBaseAsync(
            CancellationToken cancellationToken)
        {
            string[] nombresRequeridos =
            {
                "%",
                "g/100g",
                "ppm",
                "mg/kg",
                "meq/100g",
                "meq",
                "kg/ha",
                "lb/ha",
                "lb/Mz",
                "kg/ha MO"
            };

            List<UnidadMedida> existentes =
                await db.UnidadMedidas
                    .ToListAsync(
                        cancellationToken);

            foreach (
                string nombre
                in nombresRequeridos)
            {
                bool existe =
                    existentes.Any(x =>
                        Normalizar(x.nombreUnidadMedida) ==
                        Normalizar(nombre));

                if (existe)
                    continue;

                UnidadMedida unidad =
                    new()
                    {
                        nombreUnidadMedida =
                            nombre,
                        activo = true
                    };

                db.UnidadMedidas.Add(
                    unidad);

                existentes.Add(
                    unidad);
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }

        private async Task
            AsegurarConfiguracionesBaseAsync(
                CancellationToken cancellationToken)
        {
            List<UnidadMedida> unidades =
                await db.UnidadMedidas
                    .Where(x => x.activo)
                    .ToListAsync(
                        cancellationToken);

            Dictionary<string, UnidadMedida>
                porNombre =
                    unidades
                        .GroupBy(x =>
                            Normalizar(
                                x.nombreUnidadMedida))
                        .ToDictionary(
                            grupo => grupo.Key,
                            grupo => grupo.First());

            List<ElementoQuimico> elementos =
                await db.elementoQuimico
                    .Where(x => x.activo)
                    .ToListAsync(
                        cancellationToken);

            List<ElementoQuimicoUnidadMedida>
                existentesElemento =
                    await db
                        .Set<ElementoQuimicoUnidadMedida>()
                        .ToListAsync(
                            cancellationToken);

            foreach (
                ElementoQuimico elemento
                in elementos)
            {
                string simbolo =
                    Normalizar(
                        elemento
                            .simboloElementoQuimico);

                if (simbolo == "N")
                {
                    AsegurarElementoUnidad(
                        elemento,
                        "%",
                        UnidadConversionService
                            .FormulaNitrogenoMateriaOrganicaLegado,
                        factorPrincipal: 1000000m,
                        factorSecundario: 0.015m,
                        factorTerciario: 1.54m,
                        divisor: 100m,
                        predeterminada: true,
                        orden: 10,
                        observacion:
                            "Fórmula histórica de nitrógeno basada en materia orgánica.",
                        porNombre: porNombre,
                        existentes: existentesElemento);

                    AsegurarConversionesMasaArea(
                        elemento,
                        porNombre,
                        existentesElemento,
                        iniciarOrden: 20);

                    continue;
                }

                if (simbolo == "P")
                {
                    AsegurarElementoUnidad(
                        elemento,
                        "ppm",
                        UnidadConversionService
                            .FormulaLineal,
                        factorPrincipal: 3.08m,
                        predeterminada: true,
                        orden: 10,
                        observacion:
                            "ppm × 2 × 2.2 × 0.7 = lb/Mz.",
                        porNombre: porNombre,
                        existentes: existentesElemento);

                    AsegurarElementoUnidad(
                        elemento,
                        "mg/kg",
                        UnidadConversionService
                            .FormulaLineal,
                        factorPrincipal: 3.08m,
                        predeterminada: false,
                        orden: 20,
                        observacion:
                            "mg/kg equivalente a ppm para esta conversión.",
                        porNombre: porNombre,
                        existentes: existentesElemento);

                    AsegurarConversionesMasaArea(
                        elemento,
                        porNombre,
                        existentesElemento,
                        iniciarOrden: 30);

                    continue;
                }

                if (simbolo is "K" or "CA" or "MG")
                {
                    AsegurarElementoUnidad(
                        elemento,
                        "meq/100g",
                        UnidadConversionService
                            .FormulaMeqPesoEquivalente,
                        factorPrincipal: 30.8m,
                        predeterminada: true,
                        orden: 10,
                        observacion:
                            "Valor × peso equivalente × 10 × 2 × 2.2 × 0.7.",
                        porNombre: porNombre,
                        existentes: existentesElemento);

                    AsegurarElementoUnidad(
                        elemento,
                        "meq",
                        UnidadConversionService
                            .FormulaMeqPesoEquivalente,
                        factorPrincipal: 30.8m,
                        predeterminada: false,
                        orden: 20,
                        observacion:
                            "Conversión mediante peso equivalente del elemento.",
                        porNombre: porNombre,
                        existentes: existentesElemento);

                    AsegurarConversionesMasaArea(
                        elemento,
                        porNombre,
                        existentesElemento,
                        iniciarOrden: 30);

                    continue;
                }

                AsegurarElementoUnidad(
                    elemento,
                    "ppm",
                    UnidadConversionService
                        .FormulaLineal,
                    factorPrincipal: 3.08m,
                    predeterminada: true,
                    orden: 10,
                    observacion:
                        "Conversión general desde ppm a lb/Mz.",
                    porNombre: porNombre,
                    existentes: existentesElemento);

                AsegurarElementoUnidad(
                    elemento,
                    "mg/kg",
                    UnidadConversionService
                        .FormulaLineal,
                    factorPrincipal: 3.08m,
                    predeterminada: false,
                    orden: 20,
                    observacion:
                        "Conversión general desde mg/kg a lb/Mz.",
                    porNombre: porNombre,
                    existentes: existentesElemento);

                AsegurarConversionesMasaArea(
                    elemento,
                    porNombre,
                    existentesElemento,
                    iniciarOrden: 30);
            }

            List<MateriaOrganicaUnidadMedida>
                existentesMateria =
                    await db
                        .Set<MateriaOrganicaUnidadMedida>()
                        .ToListAsync(
                            cancellationToken);

            AsegurarMateriaOrganicaUnidad(
                "%",
                1m,
                predeterminada: true,
                orden: 10,
                porNombre: porNombre,
                existentes: existentesMateria);

            AsegurarMateriaOrganicaUnidad(
                "g/100g",
                1m,
                predeterminada: false,
                orden: 20,
                porNombre: porNombre,
                existentes: existentesMateria);

            AsegurarMateriaOrganicaUnidad(
                "ppm",
                0.0001m,
                predeterminada: false,
                orden: 30,
                porNombre: porNombre,
                existentes: existentesMateria);

            AsegurarMateriaOrganicaUnidad(
                "mg/kg",
                0.0001m,
                predeterminada: false,
                orden: 40,
                porNombre: porNombre,
                existentes: existentesMateria);

            AsegurarMateriaOrganicaUnidad(
                "kg/ha MO",
                0.000001m,
                predeterminada: false,
                orden: 50,
                porNombre: porNombre,
                existentes: existentesMateria);

            await db.SaveChangesAsync(
                cancellationToken);
        }

        private void AsegurarConversionesMasaArea(
            ElementoQuimico elemento,
            Dictionary<string, UnidadMedida> porNombre,
            List<ElementoQuimicoUnidadMedida>
                existentes,
            int iniciarOrden)
        {
            AsegurarElementoUnidad(
                elemento,
                "kg/ha",
                UnidadConversionService.FormulaLineal,
                factorPrincipal: 1.54m,
                predeterminada: false,
                orden: iniciarOrden,
                observacion:
                    "kg/ha × 2.2 × 0.7 = lb/Mz.",
                porNombre: porNombre,
                existentes: existentes);

            AsegurarElementoUnidad(
                elemento,
                "lb/ha",
                UnidadConversionService.FormulaLineal,
                factorPrincipal: 0.7m,
                predeterminada: false,
                orden: iniciarOrden + 10,
                observacion:
                    "lb/ha × 0.7 = lb/Mz.",
                porNombre: porNombre,
                existentes: existentes);

            AsegurarElementoUnidad(
                elemento,
                "lb/Mz",
                UnidadConversionService.FormulaLineal,
                factorPrincipal: 1m,
                predeterminada: false,
                orden: iniciarOrden + 20,
                observacion:
                    "La entrada ya se encuentra en la unidad final.",
                porNombre: porNombre,
                existentes: existentes);
        }

        private void AsegurarElementoUnidad(
            ElementoQuimico elemento,
            string nombreUnidad,
            string formula,
            decimal factorPrincipal,
            bool predeterminada,
            int orden,
            string observacion,
            Dictionary<string, UnidadMedida> porNombre,
            List<ElementoQuimicoUnidadMedida>
                existentes,
            decimal factorSecundario = 1m,
            decimal factorTerciario = 1m,
            decimal divisor = 1m)
        {
            if (!porNombre.TryGetValue(
                    Normalizar(nombreUnidad),
                    out UnidadMedida? unidad))
            {
                return;
            }

            bool existe =
                existentes.Any(x =>
                    x.elementoQuimicosId ==
                        elemento.elementoQuimicosId &&
                    x.unidadMedidaId ==
                        unidad.unidadMedidaId);

            if (existe)
                return;

            bool yaTienePredeterminada =
                existentes.Any(x =>
                    x.elementoQuimicosId ==
                        elemento.elementoQuimicosId &&
                    x.activo &&
                    x.unidadPredeterminada);

            ElementoQuimicoUnidadMedida configuracion =
                new()
                {
                    elementoQuimicosId =
                        elemento.elementoQuimicosId,
                    unidadMedidaId =
                        unidad.unidadMedidaId,
                    codigoFormulaConversion =
                        formula,
                    factorPrincipal =
                        factorPrincipal,
                    factorSecundario =
                        factorSecundario,
                    factorTerciario =
                        factorTerciario,
                    divisor =
                        divisor,
                    desplazamiento =
                        0m,
                    unidadPredeterminada =
                        predeterminada &&
                        !yaTienePredeterminada,
                    visibleEnFormulario =
                        true,
                    orden =
                        orden,
                    observacion =
                        observacion,
                    activo =
                        true
                };

            db.Set<ElementoQuimicoUnidadMedida>()
                .Add(configuracion);

            existentes.Add(
                configuracion);
        }

        private void AsegurarMateriaOrganicaUnidad(
            string nombreUnidad,
            decimal factorPrincipal,
            bool predeterminada,
            int orden,
            Dictionary<string, UnidadMedida> porNombre,
            List<MateriaOrganicaUnidadMedida>
                existentes)
        {
            if (!porNombre.TryGetValue(
                    Normalizar(nombreUnidad),
                    out UnidadMedida? unidad))
            {
                return;
            }

            bool existe =
                existentes.Any(x =>
                    x.unidadMedidaId ==
                        unidad.unidadMedidaId);

            if (existe)
                return;

            bool yaTienePredeterminada =
                existentes.Any(x =>
                    x.activo &&
                    x.unidadPredeterminada);

            MateriaOrganicaUnidadMedida configuracion =
                new()
                {
                    unidadMedidaId =
                        unidad.unidadMedidaId,
                    codigoFormulaConversion =
                        UnidadConversionService
                            .FormulaLineal,
                    factorPrincipal =
                        factorPrincipal,
                    factorSecundario =
                        1m,
                    factorTerciario =
                        1m,
                    divisor =
                        1m,
                    desplazamiento =
                        0m,
                    unidadPredeterminada =
                        predeterminada &&
                        !yaTienePredeterminada,
                    visibleEnFormulario =
                        true,
                    orden =
                        orden,
                    observacion =
                        "Conversión de materia orgánica a porcentaje.",
                    activo =
                        true
                };

            db.Set<MateriaOrganicaUnidadMedida>()
                .Add(configuracion);

            existentes.Add(
                configuracion);
        }

        private static string Normalizar(
            string? valor)
        {
            return (
                valor ??
                string.Empty)
                .Trim()
                .ToUpperInvariant();
        }
    }
}
