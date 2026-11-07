using MongoDB.Bson;
using SIVCR.Web.Models;

namespace SIVCR.Web.Services;

// Convierte los filtros del dashboard a la estructura que se guarda en MongoDB
public static class FiltrosDocumento
{
    public static DashboardFiltrosViewModel Limpiar(DashboardFiltrosViewModel? filtros)
    {
        static string? Texto(string? valor)
        {
            valor = valor?.Trim();
            return string.IsNullOrEmpty(valor) ? null
                : valor.Length > 120 ? valor[..120] : valor;
        }

        filtros ??= new DashboardFiltrosViewModel();
        return new DashboardFiltrosViewModel
        {
            Provincia = Texto(filtros.Provincia),
            Canton = Texto(filtros.Canton),
            Distrito = Texto(filtros.Distrito),
            Ruta = Texto(filtros.Ruta),
            Anio = filtros.Anio,
            Mes = filtros.Mes,
            DiaSemana = filtros.DiaSemana,
            FranjaHoraria = Texto(filtros.FranjaHoraria),
            TipoAccidente = Texto(filtros.TipoAccidente),
            Clima = Texto(filtros.Clima),
            EstadoCalzada = Texto(filtros.EstadoCalzada),
            Severidad = Texto(filtros.Severidad),
            Vehiculo = Texto(filtros.Vehiculo),
            Genero = Texto(filtros.Genero),
            RangoEdad = Texto(filtros.RangoEdad)
        };
    }

    //Genera un documento BsonDocument con los filtros del dashboard para guardarlo en MongoDB.
    public static BsonDocument Crear(DashboardFiltrosViewModel filtros) =>
        new()
        {
            { "provincia", TextoOTodos(filtros.Provincia, "Todas") },
            { "canton", TextoOTodos(filtros.Canton) },
            { "distrito", TextoOTodos(filtros.Distrito) },
            { "ruta", TextoOTodos(filtros.Ruta) },
            { "anio", NumeroOTodos(filtros.Anio) },
            { "mes", NumeroOTodos(filtros.Mes) },
            { "dia_semana", NumeroOTodos(filtros.DiaSemana) },
            { "franja_horaria", TextoOTodos(filtros.FranjaHoraria) },
            { "tipo_accidente", TextoOTodos(filtros.TipoAccidente) },
            { "condicion_climatica", TextoOTodos(filtros.Clima) },
            { "estado_calzada", TextoOTodos(filtros.EstadoCalzada) },
            { "severidad", TextoOTodos(filtros.Severidad) },
            { "tipo_vehiculo", TextoOTodos(filtros.Vehiculo) },
            { "genero", TextoOTodos(filtros.Genero) },
            { "rango_edad", TextoOTodos(filtros.RangoEdad) }
        };

    private static BsonValue TextoOTodos(string? valor, string porDefecto = "Todos") =>
        new BsonString(valor ?? porDefecto);


    private static BsonValue NumeroOTodos(int? valor) =>
        valor.HasValue ? new BsonInt32(valor.Value) : new BsonString("Todos");
}
