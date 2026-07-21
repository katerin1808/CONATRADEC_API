using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
namespace CONATRADEC_API.DTOs { 
    public class CrearAlbumBotanicoCafeDto { 
        [Required] 
        public int categoriaAlbumBotanicoId {get;set;}
        [Required,MaxLength(200)] public string titulo {get;set;}=string.Empty; 
        [MaxLength(200)] public string? nombreCientifico {get;set;} 
        [Required] public string descripcion {get;set;}=string.Empty;
        public string? caracteristicas {get;set;} 
        public string? sintomas {get;set;}
        public string? causas {get;set;}
        public string? recomendaciones {get;set;}
        public string? observaciones {get;set;}
    }
    public class ActualizarAlbumBotanicoCafeDto:CrearAlbumBotanicoCafeDto { 
        [Required] 
        public int albumBotanicoCafeId {get;set;}
    } 
    public class SubirFotoAlbumBotanicoDto {
        [Required] 
        public IFormFile archivo {get;set;}=null!;
        [MaxLength(500)] 
        public string? descripcionFoto {get;set;}
        public bool esPortada {get;set;} 
        public int orden {get;set;}
    }
    public class ActualizarFotoAlbumBotanicoDto {
        [MaxLength(500)]
        public string? descripcionFoto {get;set;}
        public int orden {get;set;} 
    } 
}