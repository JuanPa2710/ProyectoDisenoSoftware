using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SIVCR.Web.Models;
using SIVCR.Web.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        filtros = LimpiarFiltros(filtros);
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
            var personas = ObtenerColeccionPersonas();
            var filtro = CrearFiltro(filtros);
            var filtroPersonas = CrearFiltroPersonas(filtros);
            var filtroVictimas = CrearFiltroVictimas(filtros);

            // Todas las consultas se ejecutan en MongoDB. La Web recibe
            // únicamente conteos y agrupaciones, no los documentos completos.
            var opciones = ObtenerOpcionesAsync(coleccion, filtros);
            var opcionesPersonas = ObtenerOpcionesPersonasAsync(personas, filtros);
            var total = coleccion.CountDocumentsAsync(filtro);
            var totalNacional = coleccion.CountDocumentsAsync(
                CrearFiltroTasaNacional(filtros));
            var totalPersonas = personas.CountDocumentsAsync(filtroPersonas);
            var totalVictimas = personas.CountDocumentsAsync(filtroVictimas);
            var provincias = AgruparAsync(coleccion, filtro, "ubicacion.provincia", 7);
            var vehiculos = AgruparAsync(personas, filtroPersonas, "vehiculo", 0);
            var demografia = ObtenerDemografiaAsync(personas, filtroVictimas);
            var severidades = AgruparAsync(coleccion, filtro, "clase_accidente", 4);
            var rutas = AgruparAsync(coleccion, filtro, "ubicacion.ruta", 7);
            var matriz = ObtenerMatrizTemporalAsync(coleccion, filtro);

            await Task.WhenAll(
                opciones, opcionesPersonas, total, totalNacional, totalPersonas,
                totalVictimas, provincias, vehiculos, demografia, severidades,
                rutas, matriz);

            modelo.Opciones = await opciones;
            var opcionesDePersonas = await opcionesPersonas;
            modelo.Opciones.Vehiculos = opcionesDePersonas.Vehiculos;
            modelo.Opciones.Generos = opcionesDePersonas.Generos;
            modelo.Opciones.RangosEdad = opcionesDePersonas.RangosEdad;
            ConstruirIndicadores(
                modelo,
                (int)await total,
                (int)await totalNacional,
                (int)await totalPersonas,
                (int)await totalVictimas,
                await provincias,
                await vehiculos,
                await demografia,
                await severidades,
                await rutas,
                await matriz,
                filtros);
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

    [HttpGet]
    public async Task<IActionResult> OpcionesGeograficas(
        string? provincia,
        string? canton,
        string? distrito,
        string? ruta)
    {
        provincia = provincia?.Trim();
        canton = canton?.Trim();
        distrito = distrito?.Trim();
        ruta = ruta?.Trim();

        if (provincia?.Length > 120 || canton?.Length > 120
            || distrito?.Length > 120 || ruta?.Length > 120)
            return BadRequest(new { mensaje = "La ubicación seleccionada no es válida." });

        try
        {
            if (mongoClient is null)
                throw new InvalidOperationException("MongoDB no está configurado.");

            var coleccion = ObtenerColeccion();
            var fb = Builders<BsonDocument>.Filter;
            var ubicacion = new List<FilterDefinition<BsonDocument>>();
            if (!string.IsNullOrWhiteSpace(provincia))
                ubicacion.Add(fb.Eq("ubicacion.provincia", provincia));
            if (!string.IsNullOrWhiteSpace(canton))
                ubicacion.Add(fb.Eq("ubicacion.canton", canton));
            if (!string.IsNullOrWhiteSpace(distrito))
                ubicacion.Add(fb.Eq("ubicacion.distrito", distrito));

            var conRuta = ubicacion.ToList();
            if (!string.IsNullOrWhiteSpace(ruta))
                conRuta.Add(fb.Eq("ubicacion.ruta", ruta));
            var filtroUbicacion = conRuta.Count == 0 ? fb.Empty : fb.And(conRuta);

            var provincias = string.IsNullOrWhiteSpace(ruta)
                ? (string.IsNullOrWhiteSpace(provincia)
                    ? await ObtenerTextosAsync(coleccion, "ubicacion.provincia", fb.Empty)
                    : [provincia])
                : await ObtenerTextosAsync(coleccion, "ubicacion.provincia", filtroUbicacion);

            var filtroCantones = string.IsNullOrWhiteSpace(ruta)
                ? (!string.IsNullOrWhiteSpace(provincia)
                    ? fb.Eq("ubicacion.provincia", provincia)
                    : fb.Empty)
                : filtroUbicacion;
            var cantones = string.IsNullOrWhiteSpace(provincia) && string.IsNullOrWhiteSpace(ruta)
                ? Array.Empty<string>()
                : await ObtenerTextosAsync(coleccion, "ubicacion.canton", filtroCantones);

            var filtroDistritos = new List<FilterDefinition<BsonDocument>>();
            if (!string.IsNullOrWhiteSpace(provincia))
                filtroDistritos.Add(fb.Eq("ubicacion.provincia", provincia));
            if (!string.IsNullOrWhiteSpace(canton))
                filtroDistritos.Add(fb.Eq("ubicacion.canton", canton));
            if (!string.IsNullOrWhiteSpace(ruta))
                filtroDistritos.Add(fb.Eq("ubicacion.ruta", ruta));
            var distritos = string.IsNullOrWhiteSpace(canton)
                ? Array.Empty<string>()
                : await ObtenerTextosAsync(
                    coleccion,
                    "ubicacion.distrito",
                    filtroDistritos.Count == 0 ? fb.Empty : fb.And(filtroDistritos));

            var rutas = await ObtenerTextosAsync(coleccion, "ubicacion.ruta", filtroUbicacion);

            return Ok(new
            {
                provincias,
                cantones,
                distritos,
                rutas = OrdenarRutas(rutas)
            });
        }
        catch (MongoException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "No fue posible consultar las ubicaciones." });
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
        ("tipo_vehiculo", "Vehículo", "Vehiculo"),
        ("genero", "Género", "Genero"),
        ("rango_edad", "Rango de edad", "RangoEdad")
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
        modelo.Vehiculo = Texto("tipo_vehiculo");
        modelo.Genero = Texto("genero");
        modelo.RangoEdad = Texto("rango_edad");
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

    private IMongoCollection<BsonDocument> ObtenerColeccionPersonas() =>
        mongoClient!.GetDatabase(configuracion["MongoDB:Database"])
            .GetCollection<BsonDocument>(
                configuracion["MongoDB:CollectionPersonas"] ?? "PersonasAccidentes");

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

    private static FilterDefinition<BsonDocument> CrearFiltroPersonas(
        DashboardFiltrosViewModel filtros)
    {
        var constructor = Builders<BsonDocument>.Filter;
        var condiciones = new List<FilterDefinition<BsonDocument>>
        {
            constructor.Eq("perfil", "personas_cosevi")
        };

        AgregarTexto(condiciones, constructor, "ubicacion.provincia", filtros.Provincia);
        AgregarTexto(condiciones, constructor, "ubicacion.canton", filtros.Canton);
        AgregarTexto(condiciones, constructor, "ubicacion.distrito", filtros.Distrito);
        AgregarNumero(condiciones, constructor, "tiempo.anio", filtros.Anio);
        AgregarNumero(condiciones, constructor, "tiempo.mes", filtros.Mes);
        AgregarNumero(condiciones, constructor, "tiempo.dia_semana", filtros.DiaSemana);
        AgregarTexto(condiciones, constructor, "vehiculo", filtros.Vehiculo);
        AgregarTexto(condiciones, constructor, "sexo", filtros.Genero);
        AgregarTexto(condiciones, constructor, "edad_quinquenal", filtros.RangoEdad);

        return constructor.And(condiciones);
    }

    private static FilterDefinition<BsonDocument> CrearFiltroVictimas(
        DashboardFiltrosViewModel filtros)
    {
        var constructor = Builders<BsonDocument>.Filter;
        return constructor.And(
            CrearFiltroPersonas(filtros),
            constructor.In("tipo_lesion", TiposLesionVictima));
    }

    // La TSV compara la selección geográfica contra el total nacional bajo
    // las mismas condiciones temporales y del accidente.
    private static FilterDefinition<BsonDocument> CrearFiltroTasaNacional(
        DashboardFiltrosViewModel filtros) =>
        CrearFiltro(new DashboardFiltrosViewModel
        {
            Anio = filtros.Anio,
            Mes = filtros.Mes,
            DiaSemana = filtros.DiaSemana,
            FranjaHoraria = filtros.FranjaHoraria,
            TipoAccidente = filtros.TipoAccidente,
            Clima = filtros.Clima,
            EstadoCalzada = filtros.EstadoCalzada,
            Severidad = filtros.Severidad
        });

    private static readonly string[] TiposLesionVictima =
        ["Muerte en sitio", "Herido grave", "Herido leve"];

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
            Rutas = OrdenarRutas(await rutas),
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

    private static async Task<DashboardOpcionesFiltros> ObtenerOpcionesPersonasAsync(
        IMongoCollection<BsonDocument> coleccion,
        DashboardFiltrosViewModel filtros)
    {
        var vehiculos = ObtenerTextosAsync(
            coleccion, "vehiculo", FiltroOpcionesPersonas(filtros, "Vehiculo"));
        var generos = ObtenerTextosAsync(
            coleccion, "sexo", FiltroOpcionesPersonas(filtros, "Genero"));
        var rangos = ObtenerTextosAsync(
            coleccion, "edad_quinquenal", FiltroOpcionesPersonas(filtros, "RangoEdad"));

        await Task.WhenAll(vehiculos, generos, rangos);

        return new DashboardOpcionesFiltros
        {
            Vehiculos = await vehiculos,
            Generos = await generos,
            RangosEdad = (await rangos)
                .OrderBy(OrdenRangoEdad)
                .ThenBy(valor => valor, StringComparer.Create(
                    new CultureInfo("es-CR"), true))
                .ToList()
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

    private static FilterDefinition<BsonDocument> FiltroOpcionesPersonas(
        DashboardFiltrosViewModel filtros, string excluida)
    {
        var copia = new DashboardFiltrosViewModel();
        foreach (var propiedad in typeof(DashboardFiltrosViewModel).GetProperties())
            if (propiedad.Name != excluida)
                propiedad.SetValue(copia, propiedad.GetValue(filtros));
        if (excluida == "Provincia") { copia.Canton = null; copia.Distrito = null; }
        if (excluida == "Canton") copia.Distrito = null;
        return CrearFiltroPersonas(copia);
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

    private static IReadOnlyList<string> OrdenarRutas(
        IReadOnlyList<string> rutas) =>
        rutas
            .OrderBy(ruta => NumeroRuta(ruta).HasValue ? 0 : 1)
            .ThenBy(ruta => NumeroRuta(ruta) ?? int.MaxValue)
            .ThenBy(ruta => ruta, StringComparer.Create(
                new CultureInfo("es-CR"), true))
            .ToList();

    private static int? NumeroRuta(string ruta) =>
        int.TryParse(
            ruta,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var numero)
                ? numero
                : null;

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

        var consulta = coleccion.Aggregate()
            .Match(constructor.And(
                filtro,
                constructor.Ne(campo, BsonNull.Value),
                constructor.Ne(campo, "")))
            .Group(grupo)
            .Sort(new BsonDocument("cantidad", -1));
        var resultados = limite > 0
            ? await consulta.Limit(limite).ToListAsync()
            : await consulta.ToListAsync();

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

    private static async Task<IReadOnlyList<DashboardDemografiaViewModel>>
        ObtenerDemografiaAsync(
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
                    { "rango", "$edad_quinquenal" },
                    { "sexo", "$sexo" }
                }
            },
            { "cantidad", new BsonDocument("$sum", 1) }
        };

        var resultados = await coleccion.Aggregate()
            .Match(constructor.And(
                filtro,
                constructor.Ne("edad_quinquenal", BsonNull.Value),
                constructor.Ne("edad_quinquenal", "")))
            .Group(grupo)
            .ToListAsync();

        var filas = new Dictionary<string, DashboardDemografiaViewModel>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var resultado in resultados)
        {
            var id = resultado["_id"].AsBsonDocument;
            var rango = id.GetValue("rango", "Desconocida").AsString;
            var sexo = id.GetValue("sexo", "Desconocido").IsString
                ? id["sexo"].AsString
                : "Desconocido";
            var cantidad = resultado["cantidad"].ToInt32();

            if (!filas.TryGetValue(rango, out var fila))
            {
                fila = new DashboardDemografiaViewModel { RangoEdad = rango };
                filas.Add(rango, fila);
            }

            if (sexo.Equals("Hombre", StringComparison.OrdinalIgnoreCase))
                fila.Hombres += cantidad;
            else if (sexo.Equals("Mujer", StringComparison.OrdinalIgnoreCase))
                fila.Mujeres += cantidad;
            else
                fila.SexoNoInformado += cantidad;
        }

        return filas.Values
            .OrderBy(fila => OrdenRangoEdad(fila.RangoEdad))
            .ThenBy(fila => fila.RangoEdad, StringComparer.Create(
                new CultureInfo("es-CR"), true))
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
        int totalNacional,
        int totalPersonas,
        int totalVictimas,
        IReadOnlyList<DashboardDatoViewModel> provincias,
        IReadOnlyList<DashboardDatoViewModel> vehiculos,
        IReadOnlyList<DashboardDemografiaViewModel> demografia,
        IReadOnlyList<DashboardDatoViewModel> severidades,
        IReadOnlyList<DashboardDatoViewModel> rutas,
        IReadOnlyList<DatoTemporal> datosTemporales,
        DashboardFiltrosViewModel filtros)
    {
        modelo.TotalIncidentes = total;
        modelo.TotalPersonas = totalPersonas;
        modelo.TotalVictimas = totalVictimas;
        AsignarPorcentajes(provincias, total);
        var vehiculosPrincipales = vehiculos.Take(5).ToList();
        var vehiculosDonut = vehiculosPrincipales.ToList();
        var coloresVehiculos = new[] { "#479ef5", "#2886de", "#7e57c2", "#f5a623", "#d55b8c" };
        for (var indice = 0; indice < vehiculosDonut.Count; indice++)
            vehiculosDonut[indice].Color = coloresVehiculos[indice % coloresVehiculos.Length];

        var cantidadOtrosVehiculos = Math.Max(
            0,
            vehiculos.Sum(dato => dato.Cantidad) - vehiculosPrincipales.Sum(dato => dato.Cantidad));
        if (cantidadOtrosVehiculos > 0)
        {
            vehiculosDonut.Add(new DashboardDatoViewModel
            {
                Etiqueta = "Otros",
                Cantidad = cantidadOtrosVehiculos,
                Color = "#555555"
            });
        }

        var cantidadSinDatoVehiculo = Math.Max(
            0,
            totalPersonas - vehiculos.Sum(dato => dato.Cantidad));
        if (cantidadSinDatoVehiculo > 0)
        {
            vehiculosDonut.Add(new DashboardDatoViewModel
            {
                Etiqueta = "Sin dato",
                Cantidad = cantidadSinDatoVehiculo,
                Color = "#858585"
            });
        }
        AsignarPorcentajes(vehiculosDonut, totalPersonas);

        AsignarPorcentajes(severidades, total);
        foreach (var severidad in severidades)
            severidad.Color = ColorSeveridad(severidad.Etiqueta);

        var tasa = totalNacional == 0
            ? (decimal?)null
            : Math.Round(total * 100m / totalNacional, 1);
        var ambitoTasa = filtros.Ruta is not null ? $"Ruta {filtros.Ruta}"
            : filtros.Distrito is not null ? filtros.Distrito
            : filtros.Canton is not null ? filtros.Canton
            : filtros.Provincia is not null ? filtros.Provincia
            : "Total nacional";
        var cantidadLeves = severidades
            .Where(dato => dato.Etiqueta.Contains("leve", StringComparison.OrdinalIgnoreCase))
            .Sum(dato => dato.Cantidad);
        var cantidadMuertesOGraves = severidades
            .Where(dato => dato.Etiqueta.Contains("muerto", StringComparison.OrdinalIgnoreCase)
                || dato.Etiqueta.Contains("grave", StringComparison.OrdinalIgnoreCase))
            .Sum(dato => dato.Cantidad);
        var totalPonderable = cantidadLeves + cantidadMuertesOGraves;
        var indiceSeveridad = total == 0 || totalPonderable != total
            ? NoDisponible(
                "Índice de severidad",
                "No todas las categorías de severidad tienen un peso definido.")
            : new DashboardKpiViewModel
            {
                Titulo = "Índice de severidad",
                Valor = ((cantidadLeves + 2m * cantidadMuertesOGraves) / total)
                    .ToString("N2", CultureInfo.GetCultureInfo("es-CR")) + " / 2",
                Detalle = "Escala 1–2: 1 = solo heridos leves; 2 = muertos o heridos graves",
                Formula = "(1 × incidentes leves + 2 × incidentes con muertos o graves) / incidentes filtrados"
            };

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
                Valor = tasa.HasValue ? Porcentaje(tasa.Value) : "—",
                Detalle = totalNacional == 0
                    ? "No hay incidentes nacionales para estas condiciones"
                    : $"{ambitoTasa} respecto al total nacional comparable",
                Formula = "N del ámbito geográfico / N nacional con iguales condiciones × 100"
            },
            indiceSeveridad,
            new DashboardKpiViewModel
            {
                Titulo = "Víctimas",
                Valor = Formatear(totalVictimas),
                Detalle = "Personas heridas o fallecidas en sitio",
                Formula = "Muerte en sitio + heridos graves + heridos leves"
            }
        ];

        modelo.Provincias = provincias;
        modelo.Vehiculos = vehiculosDonut;
        modelo.Demografia = demografia;
        modelo.Severidades = severidades;
        modelo.VehiculoDonutCss = CrearGraficoCircular(
            modelo.Vehiculos,
            modelo.Vehiculos.Select(dato => dato.Color).ToArray());
        modelo.SeveridadPieCss = CrearGraficoCircular(
            modelo.Severidades,
            modelo.Severidades.Select(dato => dato.Color).ToArray());

        var avisos = new List<string>();
        if (TieneFiltrosSoloDeAccidentes(filtros))
        {
            avisos.Add(
                "Ruta, franja, tipo de accidente, clima, calzada y severidad no pueden aplicarse a personas porque la fuente demográfica no contiene esos campos.");
        }
        if (TieneFiltrosSoloDePersonas(filtros))
        {
            avisos.Add(
                "Vehículo, género y rango de edad no pueden aplicarse a incidentes porque las fuentes no incluyen una llave persona–accidente.");
        }
        modelo.AvisosAlcance = avisos;

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

    private static bool TieneFiltrosSoloDeAccidentes(
        DashboardFiltrosViewModel filtros) =>
        filtros.Ruta is not null
        || filtros.FranjaHoraria is not null
        || filtros.TipoAccidente is not null
        || filtros.Clima is not null
        || filtros.EstadoCalzada is not null
        || filtros.Severidad is not null;

    private static bool TieneFiltrosSoloDePersonas(
        DashboardFiltrosViewModel filtros) =>
        filtros.Vehiculo is not null
        || filtros.Genero is not null
        || filtros.RangoEdad is not null;

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
        IReadOnlyList<DashboardDatoViewModel> datos,
        IReadOnlyList<string>? coloresPersonalizados = null)
    {
        if (datos.Count == 0)
            return "#3d3d3d";

        var colores = coloresPersonalizados ?? new[]
        {
            "#479ef5", "#2886de", "#7e57c2", "#f5a623", "#d55b8c"
        };
        var inicio = 0m;
        var segmentos = new List<string>();

        for (var indice = 0; indice < datos.Count; indice++)
        {
            var final = indice == datos.Count - 1
                ? 100m
                : Math.Min(100m, inicio + datos[indice].Porcentaje);
            segmentos.Add(
                $"{colores[indice % colores.Count]} {inicio.ToString(CultureInfo.InvariantCulture)}% {final.ToString(CultureInfo.InvariantCulture)}%");
            inicio = final;
        }

        if (inicio < 100m)
        {
            segmentos.Add(
                $"#3d3d3d {inicio.ToString(CultureInfo.InvariantCulture)}% 100%");
        }

        return $"conic-gradient({string.Join(", ", segmentos)})";
    }

    private static string ColorSeveridad(string etiqueta) =>
        etiqueta.Contains("muerto", StringComparison.OrdinalIgnoreCase)
            || etiqueta.Contains("grave", StringComparison.OrdinalIgnoreCase)
                ? "#f5a623"
                : etiqueta.Contains("leve", StringComparison.OrdinalIgnoreCase)
                    ? "#e34f55"
                    : etiqueta.Contains("daño", StringComparison.OrdinalIgnoreCase)
                        || etiqueta.Contains("material", StringComparison.OrdinalIgnoreCase)
                            ? "#5e9c75"
                            : "#479ef5";

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

    private static int OrdenRangoEdad(string valor)
    {
        var coincidencia = Regex.Match(valor, @"\d+");
        return coincidencia.Success
            && int.TryParse(coincidencia.Value, out var minimo)
                ? minimo
                : int.MaxValue;
    }

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
