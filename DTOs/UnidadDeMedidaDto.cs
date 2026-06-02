namespace CONATRADEC_API.DTOs
{
    public class UnidadDeMedidaDto
    {
        public class UnidadMedidaCrearDto
        {
            public string nombreUnidadMedida { get; set; } = string.Empty;
        }

        public class UnidadMedidaEditarDto
        {
            public int unidadMedidaId { get; set; }
            public string nombreUnidadMedida { get; set; } = string.Empty;
        }

        public class UnidadMedidaRespuestaDto
        {
            public int unidadMedidaId { get; set; }
            public string nombreUnidadMedida { get; set; } = string.Empty;
            public bool activo { get; set; }
        }
    }
}
