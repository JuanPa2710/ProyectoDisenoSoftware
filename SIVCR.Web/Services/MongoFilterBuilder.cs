using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;

namespace SIVCR.Web.Services;

public static class MongoFilterBuilder
{
    public static FilterDefinition<BsonDocument> CrearFiltro(
        DashboardFiltrosViewModel filtros)
    {
        var constructor = Builders<BsonDocument>.Filter;
        var condiciones = new List<FilterDefinition<BsonDocument>>();

        AgregarTexto(condiciones, constructor, "ubicacion.provincia", filtros.Provincia);
        AgregarTexto(condiciones, constructor, "ubicacion.canton", filtros.Canton);
        AgregarTexto(condiciones, constructor, "ubicacion.distrito", filtros.Distrito);
        AgregarTexto(condiciones, constructor, "ubicacion.ruta", filtros.Ruta);
        AgregarNumero(condiciones, constructor, "tiempo.anio", filtros.Anio);
        AgregarNumero(condiciones, constructor, "tiempo.mes", filtros.Mes);
        AgregarNumero(condiciones, constructor, "tiempo.dia_semana", filtros.DiaSemana);
        AgregarTexto(condiciones, constructor, "franja_horaria", filtros.FranjaHoraria);
        AgregarTexto(condiciones, constructor, "tipo_accidente", filtros.TipoAccidente);
        AgregarTexto(condiciones, constructor, "condiciones.clima", filtros.Clima);
        AgregarTexto(condiciones, constructor, "condiciones.estado_calzada", filtros.EstadoCalzada);
        AgregarTexto(condiciones, constructor, "clase_accidente", filtros.Severidad);

        if (condiciones.Count == 0)
        {
            return constructor.Empty;
        }
        return constructor.And(condiciones);
    }

    private static void AgregarTexto(
        List<FilterDefinition<BsonDocument>> condiciones,
        FilterDefinitionBuilder<BsonDocument> constructor,
        string campo,
        string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
        {
            condiciones.Add(constructor.Eq(campo, valor));
        }
    }

    private static void AgregarNumero(
        List<FilterDefinition<BsonDocument>> condiciones,
        FilterDefinitionBuilder<BsonDocument> constructor,
        string campo,
        int? valor)
    {
        if (valor.HasValue)
        {
            condiciones.Add(constructor.Eq(campo, valor.Value));
        }
    }
}
