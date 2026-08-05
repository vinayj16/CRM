using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace CRM.Models
{
    /// <summary>
    /// Custom serializer for RolePermission.Id that gracefully handles both 
    /// ObjectId (from auto-generated MongoDB _id) and Int32 values.
    /// When encountering ObjectId, it converts to the hash code (int).
    /// </summary>
    public class RolePermissionIdSerializer : SerializerBase<int>
    {
        public int TenantId { get; set; } = 0;

        public override int Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var bsonType = context.Reader.GetCurrentBsonType();
            
            switch (bsonType)
            {
                case BsonType.Int32:
                    return context.Reader.ReadInt32();
                    
                case BsonType.Int64:
                    return (int)context.Reader.ReadInt64();
                    
                case BsonType.Double:
                    return (int)context.Reader.ReadDouble();
                    
                case BsonType.String:
                    if (int.TryParse(context.Reader.ReadString(), out int parsed))
                        return parsed;
                    return 0;
                    
                case BsonType.ObjectId:
                    // Convert ObjectId to a deterministic int using timestamp + machine hash
                    var objectId = context.Reader.ReadObjectId();
                    // Use the timestamp portion of ObjectId (first 4 bytes)
                    return (int)(objectId.Timestamp & 0x7FFFFFFF);
                    
                case BsonType.Null:
                    context.Reader.ReadNull();
                    return 0;
                    
                default:
                    // Skip unknown types
                    context.Reader.SkipValue();
                    return 0;
            }
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, int value)
        {
            context.Writer.WriteInt32(value);
        }
    }
}
