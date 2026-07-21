using System.ComponentModel.DataAnnotations;
namespace CONATRADEC_API.DTOs 

{
    public class CrearCategoriaAlbumBotanicoDto 
    { 
        [Required,MaxLength(100)]
        public string nombreCategoria {get;set;}=string.Empty;
        [MaxLength(500)] 
        public string? descripcion {get;set;}
    } 
    public class ActualizarCategoriaAlbumBotanicoDto : CrearCategoriaAlbumBotanicoDto 
    {
        [Required]
        public int categoriaAlbumBotanicoId {get;set;} 
    }
}