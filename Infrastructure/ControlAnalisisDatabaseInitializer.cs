using Microsoft.EntityFrameworkCore;
using CONATRADEC_API.Models;

namespace CONATRADEC_API.Infrastructure;

/// <summary>
/// Mantiene estructuras auxiliares del análisis y permisos de alcance.
/// No depende de migraciones para poder instalarse en servidores existentes.
/// </summary>
public sealed class ControlAnalisisDatabaseInitializer
{
    public const string AnalisisSueloTodos =
        "AnalisisSueloTodosPage";

    private readonly DBContext db;

    public ControlAnalisisDatabaseInitializer(DBContext db)
    {
        this.db = db;
    }

    public async Task InicializarAsync(
        CancellationToken cancellationToken = default)
    {
        await AsegurarControlEliminacionAsync(cancellationToken);
        await AsegurarPermisoVerTodosAsync(cancellationToken);
    }

    private Task AsegurarControlEliminacionAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.analisisSueloEliminacion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.analisisSueloEliminacion
    (
        analisisSueloEliminacionId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_analisisSueloEliminacion PRIMARY KEY,
        analisisSueloId INT NOT NULL,
        analisisSueloCalculoId INT NULL,
        usuarioEliminacionId INT NOT NULL,
        fechaEliminacionUtc DATETIME2(0) NOT NULL,
        motivoEliminacion NVARCHAR(500) NOT NULL,
        manifiestoRestauracionJson NVARCHAR(MAX) NOT NULL,
        pdfHistorico VARBINARY(MAX) NULL,
        nombreArchivoPdf NVARCHAR(260) NULL,
        estado NVARCHAR(20) NOT NULL,
        usuarioRecuperacionId INT NULL,
        fechaRecuperacionUtc DATETIME2(0) NULL,
        motivoRecuperacion NVARCHAR(500) NULL
    );

    CREATE INDEX IX_analisisSueloEliminacion_AnalisisEstado
        ON dbo.analisisSueloEliminacion(
            analisisSueloId,
            estado,
            fechaEliminacionUtc DESC);
END;
""";

        return db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    /// <summary>
    /// Crea un permiso independiente para el alcance global del historial.
    ///
    /// MainPage/Leer continúa significando "puede consultar análisis".
    /// AnalisisSueloTodosPage/Leer significa "puede consultar análisis de
    /// cualquier usuario". Si no está habilitado, el backend obliga a mostrar
    /// únicamente los análisis propios.
    ///
    /// En la primera creación se concede a ADMINISTRADOR para conservar el
    /// comportamiento existente. Después la matriz de permisos queda como
    /// única fuente de verdad y el inicializador no vuelve a forzar la relación.
    /// </summary>
    private async Task AsegurarPermisoVerTodosAsync(
        CancellationToken cancellationToken)
    {
        Interfaz? interfaz = await db.Interfaz
            .FirstOrDefaultAsync(
                item => item.nombreInterfaz == AnalisisSueloTodos,
                cancellationToken);

        bool interfazNueva = interfaz == null;

        if (interfaz == null)
        {
            interfaz = new Interfaz
            {
                nombreInterfaz = AnalisisSueloTodos,
                nombreAmigableInterfaz =
                    "Análisis de suelo · ver todos",
                descripcionInterfaz =
                    "Ver todos los análisis.",
                activo = true
            };

            db.Interfaz.Add(interfaz);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            interfaz.nombreAmigableInterfaz =
                "Análisis de suelo · ver todos";
            interfaz.descripcionInterfaz =
                "Ver todos los análisis.";
            interfaz.activo = true;

            await db.SaveChangesAsync(cancellationToken);
        }

        if (!interfazNueva)
            return;

        List<int> rolesAdministradores = await db.Roles
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.nombreRol.Trim().ToUpper() == "ADMINISTRADOR")
            .Select(item => item.rolId)
            .ToListAsync(cancellationToken);

        foreach (int rolId in rolesAdministradores)
        {
            bool existe = await db.RolInterfaz
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.rolId == rolId &&
                        item.interfazId == interfaz.interfazId,
                    cancellationToken);

            if (existe)
                continue;

            db.RolInterfaz.Add(new RolInterfaz
            {
                rolId = rolId,
                interfazId = interfaz.interfazId,
                leer = true,
                agregar = false,
                actualizar = false,
                eliminar = false
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
