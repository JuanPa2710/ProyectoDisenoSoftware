using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using SIVCR.Web.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SIVCR.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly IConfiguration configuracion;
    private readonly MongoClient? mongoClient;

    public HomeController(
        IConfiguration configuracion,
        MongoClient? mongoClient = null)
    {
        this.configuracion = configuracion;
        this.mongoClient = mongoClient;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] DashboardFiltrosViewModel filtros)
    {
        var modelo = new DashboardViewModel { Filtros = filtros };

        // "Variable global": los filtros seleccionados quedan en la sesión
        // para mostrarlos y guardarlos desde "Filtros guardados".
        GuardarFiltrosEnSesion(filtros);

        modelo.ParametrosExplorador = ConstruirParametros(
            CrearDocumentoFiltros(LimpiarFiltros(filtros)));

        try
        {
            if (mongoClient is null)
                throw new InvalidOperationException("MongoDB no está configurado.");

            var coleccion = ObtenerColeccion();
            var filtro = CrearFiltro(filtros);

            // Todas las consultas se ejecutan en MongoDB. La Web recibe
            // únicamente conteos y agrupaciones, no los documentos completos.
            var opciones = ObtenerOpcionesAsync(coleccion, filtros);
            var total = coleccion.CountDocumentsAsync(filtro);
            var provincias = AgruparAsync(coleccion, filtro, "ubicacion.provincia", 7);
            var tipos = AgruparAsync(coleccion, filtro, "tipo_accidente", 5);
            var severidades = AgruparAsync(coleccion, filtro, "clase_accidente", 4);
            var rutas = AgruparAsync(coleccion, filtro, "ubicacion.ruta", 7);
            var matriz = ObtenerMatrizTemporalAsync(coleccion, filtro);

            await Task.WhenAll(opciones, total, provincias, tipos, severidades, rutas, matriz);

            modelo.Opciones = await opciones;
            ConstruirIndicadores(
                modelo,
                (int)await total,
                await provincias,
                await tipos,
                await severidades,
                await rutas,
                await matriz);
        }
        catch (MongoException)
        {
            modelo.ErrorMongoDB =
                "No fue posible conectar o consultar MongoDB.";
        }
        catch (InvalidOperationException)
        {
            modelo.ErrorMongoDB =
                "MongoDB no está configurado para esta ejecución.";
        }

        return View(modelo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarFiltros(
        [FromBody] GuardarFiltrosRequest solicitud)
    {
        var nombre = solicitud.Nombre?.Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            return BadRequest(new { mensaje = "Escribe un nombre para la combinación." });
        if (nombre.Length > 80)
            return BadRequest(new { mensaje = "El nombre no puede superar los 80 caracteres." });

        var filtros = LimpiarFiltros(solicitud.Filtros);
        if (!TieneFiltrosActivos(filtros))
            return BadRequest(new { mensaje = "Selecciona al menos un filtro antes de guardar." });

        try
        {
            var documento = await InsertarCombinacionAsync(nombre, filtros);

            return Ok(new
            {
                id = documento["_id"].ToString(),
                mensaje = $"Filtros «{nombre}» guardados correctamente."
            });
        }
        catch (MongoException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "No fue posible guardar los filtros en MongoDB." });
        }
        catch (InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "MongoDB no está configurado para esta ejecución." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarFiltrosActuales(string? nombre)
    {
        var nombreLimpio = nombre?.Trim();
        var filtros = LeerFiltrosDeSesion();

        if (string.IsNullOrWhiteSpace(nombreLimpio))
            TempData["ErrorFiltros"] = "Escribe un nombre para la combinación.";
        else if (nombreLimpio.Length > 80)
            TempData["ErrorFiltros"] = "El nombre no puede superar los 80 caracteres.";
        else if (!TieneFiltrosActivos(filtros))
            TempData["ErrorFiltros"] = "No hay filtros seleccionados en el dashboard.";
        else
        {
            try
            {
                await InsertarCombinacionAsync(nombreLimpio, filtros);
                TempData["MensajeFiltros"] = $"Filtros «{nombreLimpio}» guardados correctamente.";
            }
            catch (MongoException)
            {
                TempData["ErrorFiltros"] = "No fue posible guardar los filtros en MongoDB.";
            }
            catch (InvalidOperationException)
            {
                TempData["ErrorFiltros"] = "MongoDB no está configurado para esta ejecución.";
            }
        }

        return RedirectToAction(nameof(FiltrosGuardados));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarFiltro(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return BadRequest();

        try
        {
            if (mongoClient is null)
                throw new InvalidOperationException("MongoDB no está configurado.");

            var resultado = await mongoClient
                .GetDatabase(configuracion["MongoDB:Database"])
                .GetCollection<BsonDocument>(
                    configuracion["MongoDB:CollectionFiltros"] ?? "filtros")
                .DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", objectId));

            if (resultado.DeletedCount > 0)
                TempData["MensajeFiltros"] = "Combinación eliminada correctamente.";
            else
                TempData["ErrorFiltros"] = "La combinación ya no existe.";
        }
        catch (MongoException)
        {
            TempData["ErrorFiltros"] = "No fue posible eliminar la combinación en MongoDB.";
        }
        catch (InvalidOperationException)
        {
            TempData["ErrorFiltros"] = "MongoDB no está configurado para esta ejecución.";
        }

        return RedirectToAction(nameof(FiltrosGuardados));
    }

    public IActionResult Privacy() => View();

    [HttpGet]
    public async Task<IActionResult> FiltrosGuardados()
    {
        var modelo = new FiltrosGuardadosViewModel
        {
            Mensaje = TempData["MensajeFiltros"] as string,
            ErrorMongoDB = TempData["ErrorFiltros"] as string,
            FiltrosActuales = ExtraerEtiquetas(
                CrearDocumentoFiltros(LeerFiltrosDeSesion()))
        };

        try
        {
            if (mongoClient is null)
                throw new InvalidOperationException("MongoDB no está configurado.");

            var documentos = await mongoClient
                .GetDatabase(configuracion["MongoDB:Database"])
                .GetCollection<BsonDocument>(
                    configuracion["MongoDB:CollectionFiltros"] ?? "filtros")
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("fecha_creacion"))
                .Limit(100)
                .ToListAsync();

            // "Registros": cuántos incidentes devuelve hoy cada combinación guardada.
            var incidencias = ObtenerColeccion();
            var conteos = await Task.WhenAll(
                documentos.Select(d => ContarResultadosAsync(incidencias, d)));

            modelo.Filtros = documentos
                .Select((d, indice) => MapearFiltroGuardado(d, conteos[indice]))
                .ToList();

            var consultas = await ObtenerColeccionHistorial()
                .Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("fecha_consulta"))
                .Limit(5)
                .ToListAsync();

            modelo.Historial = consultas.Select(MapearConsultaHistorial).ToList();
        }
        catch (MongoException)
        {
            modelo.ErrorMongoDB ??= "No fue posible conectar o consultar MongoDB.";
        }
        catch (InvalidOperationException)
        {
            modelo.ErrorMongoDB ??= "MongoDB no está configurado para esta ejecución.";
        }

        return View(modelo);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });

    // Campo en Mongo, etiqueta visible y propiedad equivalente en
    // DashboardFiltrosViewModel (null si el dashboard aún no tiene ese filtro).
    private static readonly (string Campo, string Etiqueta, string? Parametro)[] CamposFiltroGuardado =
    [
        ("provincia", "Provincia", "Provincia"),
        ("canton", "Cantón", "Canton"),
        ("distrito", "Distrito", "Distrito"),
        ("ruta", "Ruta", "Ruta"),
        ("anio", "Año", "Anio"),
        ("mes", "Mes", "Mes"),
        ("dia_semana", "Día", "DiaSemana"),
        ("franja_horaria", "Franja", "FranjaHoraria"),
        ("tipo_accidente", "Tipo de accidente", "TipoAccidente"),
        ("condicion_climatica", "Clima", "Clima"),
        ("estado_calzada", "Calzada", "EstadoCalzada"),
        ("severidad", "Severidad", "Severidad"),
        ("genero", "Género", null),
        ("rango_edad", "Rango de edad", null)
    ];

    private const string ClaveFiltrosSesion = "FiltrosDashboard";

    private void GuardarFiltrosEnSesion(DashboardFiltrosViewModel filtros) =>
        HttpContext.Session.SetString(
            ClaveFiltrosSesion,
            JsonSerializer.Serialize(LimpiarFiltros(filtros)));

    private DashboardFiltrosViewModel LeerFiltrosDeSesion()
    {
        var json = HttpContext.Session.GetString(ClaveFiltrosSesion);
        if (string.IsNullOrEmpty(json))
            return new DashboardFiltrosViewModel();

        try
        {
            return LimpiarFiltros(
                JsonSerializer.Deserialize<DashboardFiltrosViewModel>(json));
        }
        catch (JsonException)
        {
            return new DashboardFiltrosViewModel();
        }
    }

    private static BsonDocument CrearDocumentoFiltros(DashboardFiltrosViewModel filtros) =>
        FiltrosDocumento.Crear(filtros);

    // Etiquetas visibles ("Provincia: San José") solo de los filtros activos.
    private static List<(string Nombre, string Valor)> ExtraerEtiquetas(BsonDocument filtros)
    {
        var etiquetas = new List<(string Nombre, string Valor)>();
        foreach (var (campo, etiqueta, _) in CamposFiltroGuardado)
        {
            if (filtros.TryGetValue(campo, out var valor)
                && FormatearValorFiltroGuardado(campo, valor) is { } texto)
            {
                etiquetas.Add((etiqueta, texto));
            }
        }
        return etiquetas;
    }

    private async Task<BsonDocument> InsertarCombinacionAsync(
        string nombre, DashboardFiltrosViewModel filtros)
    {
        if (mongoClient is null)
            throw new InvalidOperationException("MongoDB no está configurado.");

        var documento = new BsonDocument
        {
            { "nombre_filtro", nombre },
            { "filtros", CrearDocumentoFiltros(filtros) },
            { "fecha_creacion", DateTime.UtcNow }
        };

        await mongoClient
            .GetDatabase(configuracion["MongoDB:Database"])
            .GetCollection<BsonDocument>(
                configuracion["MongoDB:CollectionFiltros"] ?? "filtros")
            .InsertOneAsync(documento);

        return documento;
    }

    private static FiltroGuardadoViewModel MapearFiltroGuardado(
        BsonDocument documento, long? totalRegistros)
    {
        var etiquetas = new List<(string Nombre, string Valor)>();
        var parametros = new Dictionary<string, string>();

        if (documento.TryGetValue("filtros", out var valorFiltros) && valorFiltros.IsBsonDocument)
        {
            var filtros = valorFiltros.AsBsonDocument;
            etiquetas = ExtraerEtiquetas(filtros);

            parametros = ConstruirParametros(filtros);
        }

        var fecha = documento.TryGetValue("fecha_creacion", out var f) && f.IsBsonDateTime
            ? f.ToUniversalTime().ToLocalTime()
                .ToString("dd/MM/yyyy", new CultureInfo("es-CR"))
            : "";

        var total = totalRegistros?.ToString("N0", new CultureInfo("es-CR"))
            ?? "No disponible";

        return new FiltroGuardadoViewModel
        {
            Id = documento.TryGetValue("_id", out var id) ? id.ToString()! : "",
            Nombre = documento.TryGetValue("nombre_filtro", out var nombre) && nombre.IsString
                && !string.IsNullOrWhiteSpace(nombre.AsString)
                ? nombre.AsString
                : "(Sin nombre)",
            Fecha = fecha,
            TotalRegistros = total,
            Etiquetas = etiquetas,
            Parametros = parametros
        };
    }

    private static string? ValorCrudoFiltroGuardado(BsonValue valor)
    {
        if (valor.IsString)
        {
            var texto = valor.AsString.Trim();
            return texto is "" or "Todos" or "Todas" ? null : texto;
        }

        return valor.IsInt32 || valor.IsInt64
            ? valor.ToInt64().ToString(CultureInfo.InvariantCulture)
            : null;
    }

    // Devuelve null cuando el filtro no está activo (null, "Todos" o "Todas").
    private static string? FormatearValorFiltroGuardado(string campo, BsonValue valor)
    {
        if (valor.IsString)
        {
            var texto = valor.AsString.Trim();
            return texto is "" or "Todos" or "Todas" ? null : texto;
        }

        if (valor.IsInt32 || valor.IsInt64)
        {
            var numero = valor.ToInt32();
            return campo switch
            {
                "mes" when numero is >= 1 and <= 12 =>
                    new CultureInfo("es-CR").DateTimeFormat.GetMonthName(numero),
                "dia_semana" when numero is >= 1 and <= 7 =>
                    new[] { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" }[numero - 1],
                _ => numero.ToString(CultureInfo.InvariantCulture)
            };
        }

        return null;
    }

    private static DashboardFiltrosViewModel LimpiarFiltros(
        DashboardFiltrosViewModel? filtros) =>
        FiltrosDocumento.Limpiar(filtros);

    private static bool TieneFiltrosActivos(DashboardFiltrosViewModel filtros) =>
        typeof(DashboardFiltrosViewModel).GetProperties()
            .Any(propiedad => propiedad.GetValue(filtros) is not null);

    private IMongoCollection<BsonDocument> ObtenerColeccionHistorial() =>
        HistorialConsultas.ObtenerColeccion(mongoClient!, configuracion);

    private static HistorialConsultaViewModel MapearConsultaHistorial(BsonDocument documento)
    {
        var etiquetas = new List<(string Nombre, string Valor)>();
        var parametros = new Dictionary<string, string>();

        if (documento.TryGetValue("filtros", out var valorFiltros) && valorFiltros.IsBsonDocument)
        {
            etiquetas = ExtraerEtiquetas(valorFiltros.AsBsonDocument);
            parametros = ConstruirParametros(valorFiltros.AsBsonDocument);
        }

        var cultura = new CultureInfo("es-CR");
        var fecha = documento.TryGetValue("fecha_consulta", out var f) && f.IsBsonDateTime
            ? f.ToUniversalTime().ToLocalTime().ToString("dd/MM/yyyy HH:mm", cultura)
            : "";
        var resultados = documento.TryGetValue("total_resultados", out var t)
            && (t.IsInt32 || t.IsInt64)
            ? t.ToInt64().ToString("N0", cultura)
            : "No disponible";

        return new HistorialConsultaViewModel
        {
            Fecha = fecha,
            Filtros = etiquetas.Count == 0
                ? "Sin filtros"
                : string.Join(" · ", etiquetas.Select(e => $"{e.Nombre}: {e.Valor}")),
            Resultados = resultados,
            Parametros = parametros
        };
    }

    private static Dictionary<string, string> ConstruirParametros(BsonDocument filtros)
    {
        var parametros = new Dictionary<string, string>();
        foreach (var (campo, _, parametro) in CamposFiltroGuardado)
        {
            if (parametro is not null
                && filtros.TryGetValue(campo, out var valor)
                && ValorCrudoFiltroGuardado(valor) is { } crudo)
            {
                parametros["Filtros." + parametro] = crudo;
            }
        }
        return parametros;
    }

    // Reconstruye los filtros del dashboard a partir de una combinación guardada.
    private static DashboardFiltrosViewModel FiltrosDesdeDocumento(BsonDocument documento)
    {
        var modelo = new DashboardFiltrosViewModel();
        if (!documento.TryGetValue("filtros", out var valorFiltros) || !valorFiltros.IsBsonDocument)
            return modelo;

        var filtros = valorFiltros.AsBsonDocument;
        string? Texto(string campo) =>
            filtros.TryGetValue(campo, out var valor) ? ValorCrudoFiltroGuardado(valor) : null;
        int? Numero(string campo) =>
            int.TryParse(Texto(campo), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n : null;

        modelo.Provincia = Texto("provincia");
        modelo.Canton = Texto("canton");
        modelo.Distrito = Texto("distrito");
        modelo.Ruta = Texto("ruta");
        modelo.Anio = Numero("anio");
        modelo.Mes = Numero("mes");
        modelo.DiaSemana = Numero("dia_semana");
        modelo.FranjaHoraria = Texto("franja_horaria");
        modelo.TipoAccidente = Texto("tipo_accidente");
        modelo.Clima = Texto("condicion_climatica");
        modelo.EstadoCalzada = Texto("estado_calzada");
        modelo.Severidad = Texto("severidad");
        return modelo;
    }

    // Cantidad de incidentes que cumplen una combinación guardada (null si falla).
    private static async Task<long?> ContarResultadosAsync(
        IMongoCollection<BsonDocument> incidencias, BsonDocument documento)
    {
        try
        {
            return await incidencias.CountDocumentsAsync(
                CrearFiltro(FiltrosDesdeDocumento(documento)));
        }
        catch (MongoException)
        {
            return null;
        }
    }

    private IMongoCollection<BsonDocument> ObtenerColeccion() =>
        mongoClient!.GetDatabase(configuracion["MongoDB:Database"])
            .GetCollection<BsonDocument>(configuracion["MongoDB:Collection"]);

    private static FilterDefinition<BsonDocument> CrearFiltro(
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

        return condiciones.Count == 0
            ? constructor.Empty
            : constructor.And(condiciones);
    }

    private static void AgregarTexto(
        List<FilterDefinition<BsonDocument>> condiciones,
        FilterDefinitionBuilder<BsonDocument> constructor,
        string campo,
        string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor))
            condiciones.Add(constructor.Eq(campo, valor));
    }

    private static void AgregarNumero(
        List<FilterDefinition<BsonDocument>> condiciones,
        FilterDefinitionBuilder<BsonDocument> constructor,
        string campo,
        int? valor)
    {
        if (valor.HasValue)
            condiciones.Add(constructor.Eq(campo, valor.Value));
    }

    private async Task<DashboardOpcionesFiltros> ObtenerOpcionesAsync(
        IMongoCollection<BsonDocument> coleccion, DashboardFiltrosViewModel filtros)
    {
        var provincias = ObtenerTextosAsync(coleccion, "ubicacion.provincia", FiltroOpciones(filtros, "Provincia"));
        var cantones = ObtenerTextosAsync(coleccion, "ubicacion.canton", FiltroOpciones(filtros, "Canton"));
        var distritos = ObtenerTextosAsync(coleccion, "ubicacion.distrito", FiltroOpciones(filtros, "Distrito"));
        var rutas = ObtenerTextosAsync(coleccion, "ubicacion.ruta", FiltroOpciones(filtros, "Ruta"));
        var anios = ObtenerNumerosAsync(coleccion, "tiempo.anio", FiltroOpciones(filtros, "Anio"));
        var meses = ObtenerNumerosAsync(coleccion, "tiempo.mes", FiltroOpciones(filtros, "Mes"));
        var dias = ObtenerNumerosAsync(coleccion, "tiempo.dia_semana", FiltroOpciones(filtros, "DiaSemana"));
        var franjas = ObtenerTextosAsync(coleccion, "franja_horaria", FiltroOpciones(filtros, "FranjaHoraria"));
        var tipos = ObtenerTextosAsync(coleccion, "tipo_accidente", FiltroOpciones(filtros, "TipoAccidente"));
        var climas = ObtenerTextosAsync(coleccion, "condiciones.clima", FiltroOpciones(filtros, "Clima"));
        var calzadas = ObtenerTextosAsync(coleccion, "condiciones.estado_calzada", FiltroOpciones(filtros, "EstadoCalzada"));
        var severidades = ObtenerTextosAsync(coleccion, "clase_accidente", FiltroOpciones(filtros, "Severidad"));

        await Task.WhenAll(
            provincias, cantones, distritos, rutas, anios, meses, dias,
            franjas, tipos, climas, calzadas, severidades);

        return new DashboardOpcionesFiltros
        {
            Provincias = await provincias,
            Cantones = await cantones,
            Distritos = await distritos,
            Rutas = await rutas,
            Anios = await anios,
            Meses = await meses,
            DiasSemana = await dias,
            FranjasHorarias = await franjas,
            TiposAccidente = await tipos,
            Climas = await climas,
            EstadosCalzada = await calzadas,
            Severidades = await severidades
        };
    }

    private static FilterDefinition<BsonDocument> FiltroOpciones(
        DashboardFiltrosViewModel filtros, string excluida)
    {
        var copia = new DashboardFiltrosViewModel();
        foreach (var propiedad in typeof(DashboardFiltrosViewModel).GetProperties())
            if (propiedad.Name != excluida)
                propiedad.SetValue(copia, propiedad.GetValue(filtros));
        if (excluida == "Provincia") { copia.Canton = null; copia.Distrito = null; }
        if (excluida == "Canton") copia.Distrito = null;
        return CrearFiltro(copia);
    }

    private static async Task<IReadOnlyList<string>> ObtenerTextosAsync(
        IMongoCollection<BsonDocument> coleccion,
        string campo, FilterDefinition<BsonDocument> filtro)
    {
        var valores = await coleccion.Distinct<BsonValue>(
            campo,
            filtro).ToListAsync();

        return valores
            .Where(valor => valor.IsString && !string.IsNullOrWhiteSpace(valor.AsString))
            .Select(valor => valor.AsString)
            .OrderBy(valor => valor, StringComparer.Create(
                new CultureInfo("es-CR"), true))
            .ToList();
    }

    private static async Task<IReadOnlyList<int>> ObtenerNumerosAsync(
        IMongoCollection<BsonDocument> coleccion,
        string campo, FilterDefinition<BsonDocument> filtro)
    {
        var valores = await coleccion.Distinct<BsonValue>(
            campo,
            filtro).ToListAsync();

        return valores
            .Where(valor => valor.IsInt32 || valor.IsInt64)
            .Select(valor => valor.ToInt32())
            .OrderBy(valor => valor)
            .ToList();
    }

    private static async Task<IReadOnlyList<DashboardDatoViewModel>> AgruparAsync(
        IMongoCollection<BsonDocument> coleccion,
        FilterDefinition<BsonDocument> filtro,
        string campo,
        int limite)
    {
        var constructor = Builders<BsonDocument>.Filter;
        var grupo = new BsonDocument
        {
            { "_id", "$" + campo },
            { "cantidad", new BsonDocument("$sum", 1) }
        };

        var resultados = await coleccion.Aggregate()
            .Match(constructor.And(
                filtro,
                constructor.Ne(campo, BsonNull.Value),
                constructor.Ne(campo, "")))
            .Group(grupo)
            .Sort(new BsonDocument("cantidad", -1))
            .Limit(limite)
            .ToListAsync();

        return resultados
            .Where(resultado => resultado.TryGetValue("_id", out var valor)
                && valor.IsString)
            .Select(resultado => new DashboardDatoViewModel
            {
                Etiqueta = resultado["_id"].AsString,
                Cantidad = resultado["cantidad"].ToInt32()
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<DatoTemporal>> ObtenerMatrizTemporalAsync(
        IMongoCollection<BsonDocument> coleccion,
        FilterDefinition<BsonDocument> filtro)
    {
        var constructor = Builders<BsonDocument>.Filter;
        var grupo = new BsonDocument
        {
            {
                "_id",
                new BsonDocument
                {
                    { "dia", "$tiempo.dia_semana" },
                    { "franja", "$franja_horaria" }
                }
            },
            { "cantidad", new BsonDocument("$sum", 1) }
        };

        var resultados = await coleccion.Aggregate()
            .Match(constructor.And(
                filtro,
                constructor.Ne("tiempo.dia_semana", BsonNull.Value),
                constructor.Ne("franja_horaria", BsonNull.Value),
                constructor.Ne("franja_horaria", "")))
            .Group(grupo)
            .ToListAsync();

        return resultados
            .Where(resultado =>
                resultado.TryGetValue("_id", out var identificador)
                && identificador.IsBsonDocument)
            .Select(resultado => new DatoTemporal
            {
                Dia = resultado["_id"]["dia"].ToInt32(),
                Franja = resultado["_id"]["franja"].AsString,
                Cantidad = resultado["cantidad"].ToInt32()
            })
            .ToList();
    }

    private static void ConstruirIndicadores(
        DashboardViewModel modelo,
        int total,
        IReadOnlyList<DashboardDatoViewModel> provincias,
        IReadOnlyList<DashboardDatoViewModel> tipos,
        IReadOnlyList<DashboardDatoViewModel> severidades,
        IReadOnlyList<DashboardDatoViewModel> rutas,
        IReadOnlyList<DatoTemporal> datosTemporales)
    {
        modelo.TotalIncidentes = total;
        AsignarPorcentajes(provincias, total);
        AsignarPorcentajes(tipos, total);
        AsignarPorcentajes(severidades, total);

        var provinciaMayor = provincias.FirstOrDefault();
        modelo.Indicadores =
        [
            new DashboardKpiViewModel
            {
                Titulo = "Incidentes filtrados",
                Valor = Formatear(total),
                Detalle = "Registros que cumplen los filtros actuales",
                Formula = "Conteo de documentos en Incidencias"
            },
            new DashboardKpiViewModel
            {
                Titulo = "Tasa de siniestralidad",
                Valor = provinciaMayor is null ? "—" : Porcentaje(provinciaMayor.Porcentaje),
                Detalle = provinciaMayor is null
                    ? "No hay incidentes para los filtros seleccionados"
                    : $"{provinciaMayor.Etiqueta}, provincia con mayor proporción",
                Formula = "N provincia / N incidentes filtrados × 100"
            },
            NoDisponible("Índice de severidad", "Faltan los pesos W1, W2 y W3 definidos por el SAD."),
            NoDisponible("Tiempo promedio de respuesta", "La fuente no incluye tiempos de atención o llegada."),
            NoDisponible("Víctimas", "La fuente no incluye víctimas, género ni rango de edad."),
            NoDisponible("Fatalidad · motocicleta", "La fuente no incluye el tipo de vehículo involucrado.")
        ];

        modelo.Provincias = provincias;
        modelo.TiposAccidente = tipos;
        modelo.Severidades = severidades;
        modelo.TipoAccidenteDonutCss = CrearGraficoCircular(modelo.TiposAccidente);
        modelo.SeveridadPieCss = CrearGraficoCircular(modelo.Severidades);

        var acumulado = 0;
        modelo.Rutas = rutas.Select(dato =>
        {
            acumulado += dato.Cantidad;
            return new DashboardRutaViewModel
            {
                Ruta = dato.Etiqueta,
                Cantidad = dato.Cantidad,
                PorcentajeAcumulado = total == 0 ? 0 :
                    Math.Round(
                        acumulado * 100m / total,
                        1)
            };
        }).ToList();

        var franjas = datosTemporales
            .GroupBy(dato => dato.Franja)
            .OrderByDescending(grupo => grupo.Sum(dato => dato.Cantidad))
            .ThenBy(grupo => grupo.Key)
            .Take(4)
            .Select(grupo => grupo.Key!)
            .ToList();

        modelo.MatrizTemporal = DiasSemana.Select(dia => new DashboardFilaTemporalViewModel
        {
            Dia = NombreDia(dia),
            Valores = franjas.Select(franja => datosTemporales
                .Where(dato => dato.Dia == dia && dato.Franja == franja)
                .Sum(dato => dato.Cantidad))
                .ToList()
        }).ToList();

        modelo.FranjasMatriz = franjas;
    }

    private static void AsignarPorcentajes(
        IReadOnlyList<DashboardDatoViewModel> datos,
        int total)
    {
        foreach (var dato in datos)
            dato.Porcentaje = total == 0
                ? 0
                : Math.Round(dato.Cantidad * 100m / total, 1);
    }

    private static DashboardKpiViewModel NoDisponible(
        string titulo,
        string detalle) =>
        new()
        {
            Titulo = titulo,
            Valor = "N/D",
            Detalle = detalle,
            Formula = "Dato no disponible en la fuente actual"
        };

    private sealed class DatoTemporal
    {
        public int Dia { get; set; }
        public string Franja { get; set; } = "";
        public int Cantidad { get; set; }
    }

    private static string CrearGraficoCircular(
        IReadOnlyList<DashboardDatoViewModel> datos)
    {
        if (datos.Count == 0)
            return "#3d3d3d";

        var colores = new[]
        {
            "#479ef5", "#2886de", "#7e57c2", "#f5a623", "#53c3a6"
        };
        var inicio = 0m;
        var segmentos = new List<string>();

        for (var indice = 0; indice < datos.Count; indice++)
        {
            var final = inicio + datos[indice].Porcentaje;
            segmentos.Add(
                $"{colores[indice % colores.Length]} {inicio.ToString(CultureInfo.InvariantCulture)}% {final.ToString(CultureInfo.InvariantCulture)}%");
            inicio = final;
        }

        return $"conic-gradient({string.Join(", ", segmentos)})";
    }

    private static string ObtenerTexto(BsonDocument documento, params string[] campos)
    {
        BsonDocument actual = documento;
        for (var indice = 0; indice < campos.Length - 1; indice++)
        {
            if (!actual.TryGetValue(campos[indice], out var anidado)
                || !anidado.IsBsonDocument)
                return "";

            actual = anidado.AsBsonDocument;
        }

        return actual.TryGetValue(campos[^1], out var valor) && valor.IsString
            ? valor.AsString
            : "";
    }

    private static int? ObtenerNumero(
        BsonDocument documento,
        params string[] campos)
    {
        BsonDocument actual = documento;
        for (var indice = 0; indice < campos.Length - 1; indice++)
        {
            if (!actual.TryGetValue(campos[indice], out var anidado)
                || !anidado.IsBsonDocument)
                return null;

            actual = anidado.AsBsonDocument;
        }

        if (!actual.TryGetValue(campos[^1], out var valor))
            return null;

        return valor.IsInt32 || valor.IsInt64 ? valor.ToInt32() : null;
    }

    private static string Formatear(int valor) =>
        valor.ToString("N0", new CultureInfo("es-CR"));

    private static string Porcentaje(decimal valor) =>
        valor.ToString("N1", new CultureInfo("es-CR")) + " %";

    private static readonly int[] DiasSemana = [2, 3, 4, 5, 6, 7, 1];

    private static string NombreDia(int dia) => dia switch
    {
        1 => "Dom",
        2 => "Lun",
        3 => "Mar",
        4 => "Mié",
        5 => "Jue",
        6 => "Vie",
        7 => "Sáb",
        _ => ""
    };
}