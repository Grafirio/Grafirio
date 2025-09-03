using MongoDB.Bson.Serialization.Attributes;

namespace Grafirio.Discount.Api.Repositories
{
    public class BaseEntity
    {
        public Guid Id { get; set; }
    }
}