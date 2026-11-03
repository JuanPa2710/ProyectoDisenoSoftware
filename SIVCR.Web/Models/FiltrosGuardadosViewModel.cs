namespace SIVCR.Web.Models;

public sealed class FiltrosGuardadosViewModel
{
    public IReadOnlyList<FiltroGuardadoViewModel> Filtros { get; set; } = [];
    public string? ErrorMongoDB { get; set; }
    public string? Mensaje { get; set; }
}

public sealed class FiltroGuardadoViewModel
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Fecha { get; set; } = "";

    // Aún no se guarda el total de registros por filtro.
    public string TotalRegistros { get; set; } = "No disponible";

    // Solo los filtros que tienen un valor distinto de "Todos"/"Todas".
    public IReadOnlyList<(string Nombre, string Valor)> Etiquetas { get; set; } = [];

    // Parámetros de consulta para abrir el Dashboard con esta combinación
    // (por ejemplo "Filtros.Provincia" = "San José").
    public Dictionary<string, string> Parametros { get; set; } = new();
}