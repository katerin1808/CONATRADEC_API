namespace CONATRADEC_API.Models
{
    public class Procedencia
    {
        public int procedenciaId { get; set; }
        public string nombreProcedencia { get; set; } = default!; // "Interno"|"Externo"
        public string? descripcionProcedencia { get; set; }
        public bool activo { get; set; } = true;

        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    }
}
