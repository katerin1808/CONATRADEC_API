namespace CONATRADEC_API.Models
{
    public class Procedencia
    {
        public int ProcedenciaId { get; set; }
        public string NombreProcedencia { get; set; } = default!; // "Interno"|"Externo"
        public string? DescripcionProcedencia { get; set; }
        public bool Activo { get; set; } = true;

        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    }
}
