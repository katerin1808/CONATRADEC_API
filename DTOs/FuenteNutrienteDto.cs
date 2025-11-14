namespace CONATRADEC_API.DTOs
{
    public class FuenteNutrienteDto
    {

        // ================= LISTAR =================
        public class FuenteNutrienteListarDto
        {
            public int fuenteNutrientesId { get; set; }
            public string nombreNutriente { get; set; } = null!;
            public string descripcionNutriente { get; set; } = null!;
            public decimal precioNutriente { get; set; }
            // No exponemos "activo" aquí (igual que con Terreno)
        }

        // ================= CREAR =================
        public class FuenteNutrienteCrearDto
        {
            public string nombreNutriente { get; set; } = null!;
            public string descripcionNutriente { get; set; } = null!;
            public decimal precioNutriente { get; set; }
        }

        // ================= EDITAR =================
        public class FuenteNutrienteEditarDto
        {
            public string nombreNutriente { get; set; } = null!;
            public string descripcionNutriente { get; set; } = null!;
            public decimal precioNutriente { get; set; }
        }
    }
}
