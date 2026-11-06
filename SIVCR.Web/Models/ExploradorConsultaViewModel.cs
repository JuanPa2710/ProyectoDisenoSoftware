namespace SIVCR.Web.Models;

public sealed class ExploradorConsultaViewModel {
    public string Coleccion { get; set; } = "";
    public List<ExploradorColumnaViewModel> Columnas { get; set; } = [];
    public List<Dictionary<string, string?>> Filas { get; set; } = [];
}

public sealed class ExploradorColumnaViewModel {
    public string Campo { get; set; } = "";
    public string Etiqueta { get; set; } = "";
}
