using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Services;

/// <summary>
/// Delimita las consultas geoespaciales a propietarios O colaboradores con
/// cobertura activa. Ningún rol (incluido administrador) omite este filtro.
/// Las relaciones contradictorias se cierran por defecto.
/// </summary>
public static class TerritorioAccesoMapa
{
    public static int? UsuarioId(ClaimsPrincipal usuario)
    {
        string? valor = usuario.FindFirstValue("uid") ??
                        usuario.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(valor, out int id) && id > 0 ? id : null;
    }

    public static IQueryable<int> TerrenosPermitidos(DBContext db, int? usuarioId)
    {
        if (usuarioId is not > 0)
            return db.Terreno.Where(_ => false).Select(t => t.terrenoId);

        // El alias Value permite componer el SELECT con Contains() en EF Core.
        return db.Database.SqlQuery<int>($"""
            SELECT t.terrenoId AS Value
            FROM dbo.terreno t
            WHERE t.activo = 1 AND
            (
                (
                    EXISTS (SELECT 1 FROM dbo.usuarioPropietario up
                            WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1)
                    AND NOT EXISTS (SELECT 1 FROM dbo.usuarioCoberturaTerritorial c
                                    WHERE c.usuarioId = {usuarioId.Value} AND c.activo = 1)
                    AND EXISTS
                    (
                        SELECT 1 FROM dbo.usuarioPropietario up
                        INNER JOIN dbo.propietarioTerreno pt
                            ON pt.propietarioId = up.propietarioId AND pt.activo = 1
                        WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1
                          AND pt.terrenoId = t.terrenoId
                    )
                )
                OR
                (
                    NOT EXISTS (SELECT 1 FROM dbo.usuarioPropietario up
                                WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1)
                    AND EXISTS
                    (
                        SELECT 1 FROM dbo.usuarioCoberturaTerritorial c
                        INNER JOIN dbo.municipio m ON m.municipioId = t.municipioId
                        WHERE c.usuarioId = {usuarioId.Value} AND c.activo = 1
                          AND (c.tipoCobertura = N'NACIONAL'
                               OR (c.tipoCobertura = N'DEPARTAMENTO'
                                   AND c.departamentoId = m.departamentoId)
                               OR (c.tipoCobertura = N'MUNICIPIO'
                                   AND c.municipioId = m.municipioId))
                    )
                )
            )
            """);
    }

    public static async Task<List<int>> MunicipiosPermitidosAsync(
        DBContext db, int? usuarioId, CancellationToken cancellationToken)
    {
        if (usuarioId is not > 0) return [];

        return await db.Database.SqlQuery<int>($"""
            SELECT m.municipioId AS Value
            FROM dbo.municipio m
            WHERE m.activo = 1
              AND
              (
                  (
                      EXISTS (SELECT 1 FROM dbo.usuarioPropietario up
                              WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1)
                      AND NOT EXISTS (SELECT 1 FROM dbo.usuarioCoberturaTerritorial c
                                      WHERE c.usuarioId = {usuarioId.Value} AND c.activo = 1)
                      AND EXISTS
                      (
                          SELECT 1 FROM dbo.usuarioPropietario up
                          JOIN dbo.propietarioTerreno pt
                              ON pt.propietarioId = up.propietarioId AND pt.activo = 1
                          JOIN dbo.terreno t
                              ON t.terrenoId = pt.terrenoId AND t.activo = 1
                          WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1
                            AND t.municipioId = m.municipioId
                      )
                  )
                  OR
                  (
                      NOT EXISTS (SELECT 1 FROM dbo.usuarioPropietario up
                                  WHERE up.usuarioId = {usuarioId.Value} AND up.activo = 1)
                      AND EXISTS
                      (
                          SELECT 1 FROM dbo.usuarioCoberturaTerritorial c
                          WHERE c.usuarioId = {usuarioId.Value} AND c.activo = 1
                            AND (c.tipoCobertura = N'NACIONAL'
                                 OR (c.tipoCobertura = N'DEPARTAMENTO'
                                     AND c.departamentoId = m.departamentoId)
                                 OR (c.tipoCobertura = N'MUNICIPIO'
                                     AND c.municipioId = m.municipioId))
                      )
                  )
              )
            """).ToListAsync(cancellationToken);
    }
}
