namespace CONATRADEC_API.DTOs
{
    public class CrearElementoQuimicoDto
    {
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public decimal pesoEquivalenteElementoQuimico { get; set; }
        public bool activo { get; set; } = true;
    }

    public class EditarElementoQuimicoDto
    {
        public int elementoQuimicosId { get; set; }
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public decimal pesoEquivalenteElementoQuimico { get; set; }
        public bool activo { get; set; }
    }

    public class ElementoQuimicoRespuestaDto
    {
        public int elementoQuimicosId { get; set; }
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public decimal pesoEquivalenteElementoQuimico { get; set; }
        public bool activo { get; set; }
    }
}
