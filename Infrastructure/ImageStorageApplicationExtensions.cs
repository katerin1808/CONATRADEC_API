using CONATRADEC_API.Services;
using Microsoft.Extensions.FileProviders;

namespace CONATRADEC_API.Infrastructure
{
    public static class ImageStorageApplicationExtensions
    {
        private static readonly (string Carpeta, string RequestPath)[]
            PublicMappings =
            [
                ("users/img", "/resources/uploads/users/img"),
                ("terrenos", "/resources/uploads/terrenos"),
                ("album-botanico", "/resources/uploads/album-botanico"),
                ("categorias-album", "/resources/uploads/categorias-album"),
                ("noticias", "/resources/uploads/noticias"),
                ("diagnosticos-ia", "/resources/uploads/diagnosticos-ia"),

                /*
                 * Las imágenes marcadas de Inspección Fitosanitaria son
                 * derivados de la evidencia original y se almacenan en una
                 * carpeta independiente. Este mapeo permite servir también las
                 * revisiones ya generadas sin modificar ni duplicar la foto
                 * original ubicada en diagnosticos-ia.
                 */
                ("diagnostico-ia", "/resources/uploads/diagnostico-ia")
            ];

        public static IServiceCollection AddConatradecImageStorage(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<ImageStorageOptions>(
                configuration.GetSection(
                    ImageStorageOptions.Seccion));

            services.AddSingleton<ImageStoragePathService>();

            return services;
        }

        public static WebApplication UseConatradecImageStorage(
            this WebApplication app)
        {
            ImageStoragePathService storage =
                app.Services.GetRequiredService<
                    ImageStoragePathService>();

            storage.Inicializar();

            foreach ((string carpeta, string requestPath)
                     in PublicMappings)
            {
                string rutaFisica =
                    storage.ObtenerCarpeta(carpeta);

                Directory.CreateDirectory(rutaFisica);

                app.UseStaticFiles(
                    new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(
                            rutaFisica),
                        RequestPath = requestPath,
                        OnPrepareResponse = context =>
                        {
                            context.Context.Response.Headers[
                                "X-Content-Type-Options"] =
                                "nosniff";

                            context.Context.Response.Headers[
                                "Cache-Control"] =
                                "public,max-age=604800";
                        }
                    });
            }

            return app;
        }
    }
}
