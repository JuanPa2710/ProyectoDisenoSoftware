
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class Provincia
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
}

public class Canton
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProvinciaId { get; set; } = "";
}

public class Distrito
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    [BsonRepresentation(BsonType.ObjectId)]
    public string CantonId { get; set; } = "";
}