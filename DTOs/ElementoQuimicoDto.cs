namespace CONATRADEC_API.DTOs
{
    public class ElementoQuimicoDto
    {

        // ================= LISTAR =================
        public class ElementoQuimicoListarDto
        {
            public int elementoQuimicosId { get; set; }
            public string simboloElementoQuimico { get; set; } = null!;
            public string nombreElementoQuimico { get; set; } = null!;
            public decimal pesoEquivalentEelementoQuimico { get; set; }
            // No exponemos "activo" aquí
        }

        // ================= CREAR =================
        public class ElementoQuimicoCrearDto
        {
            public string simboloElementoQuimico { get; set; } = null!;
            public string nombreElementoQuimico { get; set; } = null!;
            public decimal pesoEquivalentEelementoQuimico { get; set; }
        }

        // ================= EDITAR =================
        public class ElementoQuimicoEditarDto
        {
            public string simboloElementoQuimico { get; set; } = null!;
            public string nombreElementoQuimico { get; set; } = null!;
            public decimal pesoEquivalentEelementoQuimico { get; set; }
        }
    }
}
