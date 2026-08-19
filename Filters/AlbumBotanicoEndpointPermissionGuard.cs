using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Protege los endpoints históricos del Álbum Botánico que fueron creados
    /// antes de que la API validara permisos funcionales por interfaz.
    ///
    /// El flujo de Diagnóstico IA conserva lectura del catálogo de categorías,
    /// porque esa pantalla necesita clasificar evidencias aunque el usuario no
    /// administre directamente el Álbum Botánico.
    /// </summary>
    internal static class AlbumBotanicoEndpointPermissionGuard
    {
        private const string InterfazAlbum = "albumFotosPage";
        private const string InterfazSolicitud = "diagnosticoIASolicitudPage";
        private const string InterfazAnalizador = "diagnosticoIAAnalizadorPage";
        private const string InterfazAprobador = "diagnosticoIAAprobadorPage";

        public static async Task<IActionResult?> ValidarAsync(
            HttpContext context,
            CancellationToken cancellationToken)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            bool esAlbum = path.StartsWith(
                "/api/album-botanico",
                StringComparison.OrdinalIgnoreCase);

            bool esCategoria = path.StartsWith(
                "/api/categoria-album-botanico",
                StringComparison.OrdinalIgnoreCase);

            if (!esAlbum && !esCategoria)
                return null;

            PermisoApiService permisos = context.RequestServices
                .GetRequiredService<PermisoApiService>();

            int? usuarioId = ObtenerUsuarioId(context);
            string metodo = context.Request.Method;

            if (esCategoria && HttpMethods.IsGet(metodo))
            {
                return await ValidarLecturaCategoriaAsync(
                    permisos,
                    usuarioId,
                    cancellationToken);
            }

            if (HttpMethods.IsGet(metodo))
            {
                return await ValidarUnPermisoAsync(
                    permisos,
                    usuarioId,
                    TipoPermisoApi.Leer,
                    cancellationToken);
            }

            if (esCategoria &&
                HttpMethods.IsPost(metodo) &&
                path.EndsWith(
                    "/portada",
                    StringComparison.OrdinalIgnoreCase))
            {
                ResultadoPermisoApi agregar = await permisos.ValidarAsync(
                    usuarioId,
                    InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                if (agregar.Permitido)
                    return null;

                ResultadoPermisoApi actualizar = await permisos.ValidarAsync(
                    usuarioId,
                    InterfazAlbum,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

                return actualizar.Permitido
                    ? null
                    : CrearDenegado(actualizar);
            }

            TipoPermisoApi permiso = ResolverPermiso(
                esAlbum,
                path,
                metodo);

            return await ValidarUnPermisoAsync(
                permisos,
                usuarioId,
                permiso,
                cancellationToken);
        }

        private static TipoPermisoApi ResolverPermiso(
            bool esAlbum,
            string path,
            string metodo)
        {
            if (HttpMethods.IsDelete(metodo))
                return TipoPermisoApi.Eliminar;

            if (HttpMethods.IsPut(metodo))
                return TipoPermisoApi.Actualizar;

            if (HttpMethods.IsPatch(metodo))
            {
                if (esAlbum &&
                    path.Contains(
                        "/fotos/",
                        StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(
                        "/portada",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return TipoPermisoApi.Actualizar;
                }

                return TipoPermisoApi.Eliminar;
            }

            if (HttpMethods.IsPost(metodo))
                return TipoPermisoApi.Agregar;

            return TipoPermisoApi.Leer;
        }

        private static async Task<IActionResult?> ValidarLecturaCategoriaAsync(
            PermisoApiService permisos,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            foreach (string interfaz in new[]
            {
                InterfazAlbum,
                InterfazSolicitud,
                InterfazAnalizador,
                InterfazAprobador
            })
            {
                ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                    usuarioId,
                    interfaz,
                    TipoPermisoApi.Leer,
                    cancellationToken);

                if (resultado.Permitido)
                    return null;
            }

            return new ObjectResult(new
            {
                success = false,
                message = "No tiene permiso para consultar las categorías del Álbum Botánico."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        private static async Task<IActionResult?> ValidarUnPermisoAsync(
            PermisoApiService permisos,
            int? usuarioId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                InterfazAlbum,
                permiso,
                cancellationToken);

            return resultado.Permitido
                ? null
                : CrearDenegado(resultado);
        }

        private static ObjectResult CrearDenegado(
            ResultadoPermisoApi resultado) =>
            new(new
            {
                success = false,
                message = resultado.Mensaje
            })
            {
                StatusCode = resultado.CodigoEstado
            };

        private static int? ObtenerUsuarioId(HttpContext context)
        {
            string texto = context.Request.Headers["X-Usuario-Id"].ToString();

            return int.TryParse(texto, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }
    }
}
