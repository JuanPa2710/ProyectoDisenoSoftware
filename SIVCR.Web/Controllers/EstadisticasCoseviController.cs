using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SIVCR.Web.Controllers;

public sealed class EstadisticasCoseviController : Controller
{
    private readonly IConfiguration _configuracion;
    private readonly MongoClient? _cliente;

    public EstadisticasCoseviController(IConfiguration configuracion, MongoClient? mongoClient = null)
    {
        _configuracion = configuracion;
        _cliente = mongoClient;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int anio = 2024, int? mes = null, string? provincia = null,
        string? canton = null, string? distrito = null, int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        var modelo = new EstadisticasCoseviViewModel
        {
            Anio = anio, Mes = mes,
            Provincia = Limpiar(provincia), Canton = Limpiar(canton),
            Distrito = Limpiar(distrito), Pagina = Math.Max(1, pagina)
        };
        if (!ModelState.IsValid || anio < 1900 || anio > DateTime.UtcNow.Year
            || (mes.HasValue && (mes < 1 || mes > 12))
            || new[] { provincia, canton, distrito }.Any(v => v?.Length > 100))
        {
            modelo.Error = "Revisá el año, el mes y los filtros de ubicación (máximo 100 caracteres).";
            return View(modelo);
        }

        try
        {
            if (_cliente is null)
                throw new InvalidOperationException("MongoDB no configurado.");
            var db = _cliente.GetDatabase(_configuracion["MongoDB:Database"] ?? "proyecto");
            var accidentes = db.GetCollection<BsonDocument>(
                _configuracion["MongoDB:Collection"] ?? "Incidencias");
            var personas = db.GetCollection<BsonDocument>("PersonasAccidentes");

            var filtro = new BsonDocument { { "fuente", "COSEVI" }, { "tiempo.anio", anio } };
            if (mes.HasValue) filtro.Add("tiempo.mes", mes.Value);
            AgregarUbicacion(filtro, "provincia", modelo.Provincia);
            AgregarUbicacion(filtro, "canton", modelo.Canton);
            AgregarUbicacion(filtro, "distrito", modelo.Distrito);

            // Solo se incluyen personas con la estructura producida por este cargador.
            // No se presupone el esquema de documentos anteriores en la colección.
            var filtroPersonas = filtro.DeepClone().AsBsonDocument;
            filtroPersonas.Add("perfil", "personas_cosevi");

            var consultaAccidentes = Agrupar(accidentes, filtro, cancellationToken);
            var consultaPersonas = Agrupar(personas, filtroPersonas, cancellationToken);
            await Task.WhenAll(consultaAccidentes, consultaPersonas);

            // Primero se cuenta cada fuente. Después se combinan las agrupaciones.
            // Nunca se unen filas individuales de personas con accidentes.
            var filas = new Dictionary<(int, string, string, string), EstadisticaCoseviFila>();
            Incorporar(filas, await consultaAccidentes, esPersona: false);
            Incorporar(filas, await consultaPersonas, esPersona: true);

            var ordenadas = filas.Values.OrderBy(f => f.Mes)
                .ThenBy(f => f.Provincia).ThenBy(f => f.Canton).ThenBy(f => f.Distrito).ToList();
            modelo.TotalAccidentes = ordenadas.Sum(f => f.Accidentes ?? 0);
            modelo.TotalPersonas = ordenadas.Sum(f => f.Personas ?? 0);
            const int tamanoPagina = 100;
            modelo.TotalPaginas = Math.Max(1, (int)Math.Ceiling(ordenadas.Count / (double)tamanoPagina));
            modelo.Pagina = Math.Min(modelo.Pagina, modelo.TotalPaginas);
            modelo.Filas = ordenadas.Skip((modelo.Pagina - 1) * tamanoPagina).Take(tamanoPagina).ToList();
        }
        catch (MongoException)
        {
            modelo.Error = "No se pudieron consultar las estadísticas en MongoDB.";
        }
        catch (InvalidOperationException)
        {
            modelo.Error = "MongoDB no está configurado para esta ejecución.";
        }
        return View(modelo);
    }

    private static string Limpiar(string? texto) => Regex.Replace(
        (texto ?? "").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();

    private static void AgregarUbicacion(BsonDocument filtro, string campo, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor)) filtro.Add("ubicacion." + campo, valor);
    }

    private static Task<List<BsonDocument>> Agrupar(
        IMongoCollection<BsonDocument> coleccion, BsonDocument filtro,
        CancellationToken cancellationToken)
    {
        var grupo = new BsonDocument
        {
            { "_id", new BsonDocument
                {
                    { "mes", "$tiempo.mes" },
                    { "provincia", "$ubicacion.provincia" },
                    { "canton", "$ubicacion.canton" },
                    { "distrito", "$ubicacion.distrito" }
                }
            },
            { "cantidad", new BsonDocument("$sum", 1) }
        };
        return coleccion.Aggregate(new AggregateOptions
            {
                Collation = new Collation("es", strength: CollationStrength.Secondary),
                MaxTime = TimeSpan.FromSeconds(30)
            })
            .Match(filtro).Group(grupo).ToListAsync(cancellationToken);
    }

    private static void Incorporar(
        Dictionary<(int, string, string, string), EstadisticaCoseviFila> filas,
        List<BsonDocument> grupos, bool esPersona)
    {
        foreach (var grupo in grupos)
        {
            var id = grupo["_id"].AsBsonDocument;
            var mesValor = id.GetValue("mes", BsonNull.Value);
            var mes = mesValor.IsInt32 || mesValor.IsInt64 ? mesValor.ToInt32() : 0;
            var provincia = Texto(id, "provincia");
            var canton = Texto(id, "canton");
            var distrito = Texto(id, "distrito");
            var clave = (mes, provincia.ToUpperInvariant(), canton.ToUpperInvariant(), distrito.ToUpperInvariant());
            if (!filas.TryGetValue(clave, out var fila))
            {
                fila = new EstadisticaCoseviFila
                {
                    Mes = mes, Provincia = provincia, Canton = canton, Distrito = distrito
                };
                filas.Add(clave, fila);
            }
            var cantidad = grupo["cantidad"].ToInt64();
            if (esPersona) fila.Personas = (fila.Personas ?? 0) + cantidad;
            else fila.Accidentes = (fila.Accidentes ?? 0) + cantidad;
        }
    }

    private static string Texto(BsonDocument documento, string campo)
    {
        var valor = documento.GetValue(campo, BsonNull.Value);
        return valor.IsString ? Limpiar(valor.AsString) : "";
    }
}