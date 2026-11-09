namespace SIVCR.Web.Models;

public sealed class FiltrosGuardadosViewModel
{
    public IReadOnlyList<FiltroGuardadoViewModel> Filtros { get; set; } = [];
    public IReadOnlyList<HistorialConsultaViewModel> Historial { get; set; } = [];
    public string? ErrorMongoDB { get; set; }
    public string? Mensaje { get; set; }

    // Filtros que el usuario tiene seleccionados en el dashboard de la sesión actual.
    public IReadOnlyList<(string Nombre, string Valor)> FiltrosActuales { get; set; } = [];
}

public sealed class FiltroGuardadoViewModel
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Fecha { get; set; } = "";

    public string TotalRegistros { get; set; } = "No disponible";

    public IReadOnlyList<(string Nombre, string Valor)> Etiquetas { get; set; } = [];

    public Dictionary<string, string> Parametros { get; set; } = new();
}

public sealed class HistorialConsultaViewModel
{
    public string Fecha { get; set; } = "";

    public string Filtros { get; set; } = "Sin filtros";

    public string Resultados { get; set; } = "";

    // Parámetros de consulta para repetir la consulta en el dashboard.
    public Dictionary<string, string> Parametros { get; set; } = new();
}