using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;

namespace SIVCR.Web.Services;

// Historial de consultas del Explorador de registros
public static class HistorialConsultas
{
    //Ignora la consultas si se hizo la misma en poco tiempo
    private static readonly TimeSpan VentanaDuplicados = TimeSpan.FromMinutes(2);

    public static IMongoCollection<BsonDocument> ObtenerColeccion(
        MongoClient cliente, IConfiguration configuracion) =>
        cliente.GetDatabase(configuracion["MongoDB:Database"])
            .GetCollection<BsonDocument>(
                configuracion["MongoDB:CollectionHistorial"] ?? "HistorialConsulta");

    
    public static async Task RegistrarAsync(
        MongoClient cliente,
        IConfiguration configuracion,
        DashboardFiltrosViewModel filtros,
        long totalResultados)
    {
        try
        {
            var coleccion = ObtenerColeccion(cliente, configuracion);
            var documentoFiltros = FiltrosDocumento.Crear(FiltrosDocumento.Limpiar(filtros));

            var ultima = await coleccion
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("fecha_consulta"))
                .Limit(1)
                .FirstOrDefaultAsync();

            if (ultima is not null
                && ultima.TryGetValue("filtros", out var filtrosAnteriores)
                && filtrosAnteriores.Equals(documentoFiltros)
                && ultima.TryGetValue("fecha_consulta", out var fechaAnterior)
                && fechaAnterior.IsBsonDateTime
                && DateTime.UtcNow - fechaAnterior.ToUniversalTime() < VentanaDuplicados)
            {
                return;
            }

            await coleccion.InsertOneAsync(new BsonDocument
            {
                { "fecha_consulta", DateTime.UtcNow },
                { "filtros", documentoFiltros },
                { "total_resultados", totalResultados }
            });
        }
        catch (MongoException)
        {
    
        }
    }
}