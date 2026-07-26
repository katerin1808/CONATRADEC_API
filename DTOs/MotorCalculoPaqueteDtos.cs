using System.Text.Json.Serialization;

namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Paquete versionado que permite ejecutar el cálculo de requerimiento
    /// anual sin depender de la conexión al servidor.
    ///
    /// El código estable del motor permanece compilado en la aplicación. Este
    /// paquete contiene las reglas y parametrizaciones administradas por la API.
    /// </summary>
    public sealed class MotorCalculoPaqueteDto
    {
        public int versionEsquema { get; set; } = 1;
        public string versionMotorBase { get; set; } = "1.0.0";
        public string versionPaquete { get; set; } = string.Empty;
        public string hashSha256 { get; set; } = string.Empty;
        public DateTime fechaGeneracionUtc { get; set; }
        public string versionMinimaAplicacion { get; set; } = "1.0.0";

        public MotorCalculoModulosDto modulos { get; set; } = new();
        public MotorCalculoContenidoDto contenido { get; set; } = new();
    }

    public sealed class MotorCalculoEstadoDto
    {
        public int versionEsquema { get; set; }
        public string versionMotorBase { get; set; } = string.Empty;
        public string versionPaquete { get; set; } = string.Empty;
        public string hashSha256 { get; set; } = string.Empty;
        public string versionMinimaAplicacion { get; set; } = string.Empty;
        public DateTime fechaGeneracionUtc { get; set; }
        public MotorCalculoModulosDto modulos { get; set; } = new();
    }

    public sealed class MotorCalculoModulosDto
    {
        public bool requerimientoAnual { get; set; } = true;

        /*
         * Estos módulos forman parte del diseño del paquete, pero se activarán
         * cuando sus algoritmos actuales sean extraídos a la siguiente fase.
         */
        public bool enmiendaCalcarea { get; set; }
        public bool balanceFormula { get; set; }
        public bool fertilizacionMixta { get; set; }
    }

    public sealed class MotorCalculoContenidoDto
    {
        public int unidadResultadoId { get; set; }
        public string unidadResultado { get; set; } = "lb/Mz";
        public int unidadRangoKgHaId { get; set; }

        public List<MotorTipoCultivoDto> tiposCultivo { get; set; } = new();
        public List<MotorTipoAnalisisDto> tiposAnalisis { get; set; } = new();
        public List<MotorElementoDto> elementos { get; set; } = new();
        public List<MotorUnidadDto> unidades { get; set; } = new();

        public List<MotorConversionElementoDto>
            conversionesElementos { get; set; } = new();

        public List<MotorConversionMateriaOrganicaDto>
            conversionesMateriaOrganica { get; set; } = new();

        public List<MotorExtraccionDto> parametrosExtraccion { get; set; } =
            new();

        public List<MotorRangoCultivoDto> rangosCultivo { get; set; } =
            new();
    }

    public sealed class MotorTipoCultivoDto
    {
        public int tipoCultivoId { get; set; }
        public string nombreTipoCultivo { get; set; } = string.Empty;
        public bool activo { get; set; }
    }

    public sealed class MotorTipoAnalisisDto
    {
        public int tipoAnalisisSueloId { get; set; }
        public string nombreTipoAnalisisSuelo { get; set; } = string.Empty;
        public bool activo { get; set; }
    }

    public sealed class MotorElementoDto
    {
        public int elementoQuimicosId { get; set; }
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public decimal pesoEquivalenteElementoQuimico { get; set; }
        public bool activo { get; set; }
    }

    public sealed class MotorUnidadDto
    {
        public int unidadMedidaId { get; set; }
        public string nombreUnidadMedida { get; set; } = string.Empty;
        public bool activo { get; set; }
    }

    public abstract class MotorConversionBaseDto
    {
        public int unidadMedidaId { get; set; }
        public string codigoFormulaConversion { get; set; } = "LINEAL";
        public decimal factorPrincipal { get; set; } = 1m;
        public decimal factorSecundario { get; set; } = 1m;
        public decimal factorTerciario { get; set; } = 1m;
        public decimal divisor { get; set; } = 1m;
        public decimal desplazamiento { get; set; }
        public bool activo { get; set; }
    }

    public sealed class MotorConversionElementoDto :
        MotorConversionBaseDto
    {
        public int elementoQuimicosId { get; set; }
    }

    public sealed class MotorConversionMateriaOrganicaDto :
        MotorConversionBaseDto
    {
    }

    public sealed class MotorExtraccionDto
    {
        public int elementoQuimicosId { get; set; }
        public decimal cantidadExtraidaPorQQOro { get; set; }
        public bool activo { get; set; }
    }

    public sealed class MotorRangoCultivoDto
    {
        public int tipoCultivoId { get; set; }
        public int elementoQuimicosId { get; set; }
        public decimal valorMinimo { get; set; }
        public decimal valorMaximo { get; set; }
        public string unidadBase { get; set; } = string.Empty;
        public bool activo { get; set; }
    }
}
