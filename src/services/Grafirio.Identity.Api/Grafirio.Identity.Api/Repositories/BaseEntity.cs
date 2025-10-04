using MongoDB.Bson.Serialization.Attributes;

namespace Grafirio.Identity.Api.Repositories
{
    public class BaseEntity
    {
        [BsonElement("_id")] public Guid Id { get; set; }
    }
}