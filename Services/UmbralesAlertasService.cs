using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public sealed class UmbralesAlertasService
    {
        public const string PhBajoCriticoMaximo =
            "PH_BAJO_CRITICO_MAXIMO";

        public const string PhBajoAtencionMaximo =
            "PH_BAJO_ATENCION_MAXIMO";

        public const string PhAltoAtencionMinimo =
            "PH_ALTO_ATENCION_MINIMO";

        public const string PhAltoCriticoMinimo =
            "PH_ALTO_CRITICO_MINIMO";

        public const string MateriaOrganicaBajaMaxima =
            "MATERIA_ORGANICA_BAJA_MAXIMA";

        public const string AcidezAltaMinima =
            "ACIDEZ_ALTA_MINIMA";

        private const string PhCriticoLegacy =
            "PH_CRITICO_MAXIMO";

        private const string PhAtencionLegacy =
            "PH_ATENCION_MAXIMO";

        private readonly AlertasAgricolasDbContext db;

        public UmbralesAlertasService(
            AlertasAgricolasDbContext db)
        {
            this.db = db;
        }

        public async Task<UmbralesAlertas>
            ObtenerAsync(
                CancellationToken cancellationToken = default)
        {
            Dictionary<string, decimal> valores =
                await db.Configuraciones
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .ToDictionaryAsync(
                        item => item.Clave,
                        item => item.Valor,
                        StringComparer.OrdinalIgnoreCase,
                        cancellationToken);

            decimal bajoCritico =
                ObtenerValor(
                    valores,
                    PhBajoCriticoMaximo,
                    5.50m,
                    PhCriticoLegacy);

            decimal bajoAtencion =
                ObtenerValor(
                    valores,
                    PhBajoAtencionMaximo,
                    6.00m,
                    PhAtencionLegacy);

            var resultado =
                new UmbralesAlertas
                {
                    PhBajoCriticoMaximo =
                        bajoCritico,

                    PhBajoAtencionMaximo =
                        bajoAtencion,

                    PhAltoAtencionMinimo =
                        ObtenerValor(
                            valores,
                            PhAltoAtencionMinimo,
                            6.50m),

                    PhAltoCriticoMinimo =
                        ObtenerValor(
                            valores,
                            PhAltoCriticoMinimo,
                            7.00m),

                    MateriaOrganicaBajaMaxima =
                        ObtenerValor(
                            valores,
                            MateriaOrganicaBajaMaxima,
                            3.00m),

                    AcidezAltaMinima =
                        ObtenerValor(
                            valores,
                            AcidezAltaMinima,
                            1.00m)
                };

            Validar(resultado);

            return resultado;
        }

        public async Task ValidarCambioAsync(
            string clave,
            decimal valor,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clave))
            {
                throw new ArgumentException(
                    "La clave del umbral es obligatoria.");
            }

            UmbralesAlertas actual =
                await ObtenerAsync(cancellationToken);

            string normalizada =
                NormalizarClave(clave);

            UmbralesAlertas propuesta =
                actual with
                {
                    PhBajoCriticoMaximo =
                        normalizada ==
                        PhBajoCriticoMaximo
                            ? valor
                            : actual.PhBajoCriticoMaximo,

                    PhBajoAtencionMaximo =
                        normalizada ==
                        PhBajoAtencionMaximo
                            ? valor
                            : actual.PhBajoAtencionMaximo,

                    PhAltoAtencionMinimo =
                        normalizada ==
                        PhAltoAtencionMinimo
                            ? valor
                            : actual.PhAltoAtencionMinimo,

                    PhAltoCriticoMinimo =
                        normalizada ==
                        PhAltoCriticoMinimo
                            ? valor
                            : actual.PhAltoCriticoMinimo,

                    MateriaOrganicaBajaMaxima =
                        normalizada ==
                        MateriaOrganicaBajaMaxima
                            ? valor
                            : actual.MateriaOrganicaBajaMaxima,

                    AcidezAltaMinima =
                        normalizada ==
                        AcidezAltaMinima
                            ? valor
                            : actual.AcidezAltaMinima
                };

            Validar(propuesta);
        }

        public static string NormalizarClave(
            string clave)
        {
            string valor =
                clave.Trim().ToUpperInvariant();

            return valor switch
            {
                PhCriticoLegacy =>
                    PhBajoCriticoMaximo,

                PhAtencionLegacy =>
                    PhBajoAtencionMaximo,

                _ => valor
            };
        }

        private static decimal ObtenerValor(
            IReadOnlyDictionary<string, decimal> valores,
            string clave,
            decimal valorPredeterminado,
            params string[] alternativas)
        {
            if (valores.TryGetValue(
                    clave,
                    out decimal valor))
            {
                return valor;
            }

            foreach (string alternativa
                     in alternativas)
            {
                if (valores.TryGetValue(
                        alternativa,
                        out valor))
                {
                    return valor;
                }
            }

            return valorPredeterminado;
        }

        private static void Validar(
            UmbralesAlertas umbrales)
        {
            ValidarPh(
                umbrales.PhBajoCriticoMaximo,
                nameof(
                    umbrales.PhBajoCriticoMaximo));

            ValidarPh(
                umbrales.PhBajoAtencionMaximo,
                nameof(
                    umbrales.PhBajoAtencionMaximo));

            ValidarPh(
                umbrales.PhAltoAtencionMinimo,
                nameof(
                    umbrales.PhAltoAtencionMinimo));

            ValidarPh(
                umbrales.PhAltoCriticoMinimo,
                nameof(
                    umbrales.PhAltoCriticoMinimo));

            if (umbrales.PhBajoCriticoMaximo >
                umbrales.PhBajoAtencionMaximo)
            {
                throw new InvalidOperationException(
                    "El pH bajo crítico no puede ser mayor que el pH bajo de atención.");
            }

            if (umbrales.PhBajoAtencionMaximo >=
                umbrales.PhAltoAtencionMinimo)
            {
                throw new InvalidOperationException(
                    "El límite bajo de atención debe ser menor que el límite alto de atención.");
            }

            if (umbrales.PhAltoAtencionMinimo >
                umbrales.PhAltoCriticoMinimo)
            {
                throw new InvalidOperationException(
                    "El pH alto de atención no puede ser mayor que el pH alto crítico.");
            }

            if (umbrales.MateriaOrganicaBajaMaxima < 0 ||
                umbrales.MateriaOrganicaBajaMaxima > 100)
            {
                throw new InvalidOperationException(
                    "La materia orgánica debe estar entre 0 y 100%.");
            }

            if (umbrales.AcidezAltaMinima < 0)
            {
                throw new InvalidOperationException(
                    "La acidez alta mínima no puede ser negativa.");
            }
        }

        private static void ValidarPh(
            decimal valor,
            string nombre)
        {
            if (valor < 0 || valor > 14)
            {
                throw new InvalidOperationException(
                    $"El umbral {nombre} debe estar entre 0 y 14.");
            }
        }
    }

    public sealed record UmbralesAlertas
    {
        public decimal PhBajoCriticoMaximo
        {
            get;
            init;
        }

        public decimal PhBajoAtencionMaximo
        {
            get;
            init;
        }

        public decimal PhAltoAtencionMinimo
        {
            get;
            init;
        }

        public decimal PhAltoCriticoMinimo
        {
            get;
            init;
        }

        public decimal MateriaOrganicaBajaMaxima
        {
            get;
            init;
        }

        public decimal AcidezAltaMinima
        {
            get;
            init;
        }
    }
}
