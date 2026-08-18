namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Contrato de la aplicación instalada. La URL y la credencial temporal se
    /// transportan separadas para impedir que el permiso aparezca en URLs,
    /// historial de navegación o bitácoras.
    /// </summary>
    public sealed class ActualizacionDisponibleV2Dto
    {
        public int ActualizacionAplicacionId { get; set; }
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long VersionCodigo { get; set; }
        public string NotasVersion { get; set; } = string.Empty;
        public bool Obligatoria { get; set; }
        public long? VersionMinimaCodigo { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public string TipoContenido { get; set; } =
            "application/octet-stream";
        public long TamanoBytes { get; set; }
        public string HashSha256 { get; set; } = string.Empty;
        public string UrlDescarga { get; set; } = string.Empty;
        public string PermisoDescarga { get; set; } = string.Empty;
        public DateTime? FechaPublicacionUtc { get; set; }
    }
}
