using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace CRM.Helpers
{
    /// <summary>
    /// Custom MongoDB serializer that accepts both string and numeric BSON types
    /// and converts them to string. This fixes the "Cannot deserialize a 'String' from BsonType 'Decimal128'" error
    /// when some documents have numeric values in string fields.
    /// </summary>
    public class FlexibleStringSerializer : SerializerBase<string>, IRepresentationConfigurable<FlexibleStringSerializer>
    {
        // The BSON representation we serialize/deserialize as. We always treat the
        // underlying value as a string, so the representation is effectively String.
        private readonly BsonType _representation = BsonType.String;

        public FlexibleStringSerializer()
        {
        }

        public FlexibleStringSerializer(BsonType representation)
        {
            _representation = representation;
        }

        public BsonType Representation => _representation;

        public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var bsonType = context.Reader.GetCurrentBsonType();

            switch (bsonType)
            {
                case BsonType.String:
                    return context.Reader.ReadString();

                case BsonType.Int32:
                    return context.Reader.ReadInt32().ToString();

                case BsonType.Int64:
                    return context.Reader.ReadInt64().ToString();

                case BsonType.Double:
                    return context.Reader.ReadDouble().ToString("0.##");

                case BsonType.Decimal128:
                    return context.Reader.ReadDecimal128().ToString();

                case BsonType.ObjectId:
                    // string Id properties (e.g. ReferralEarningModel.Id) are stored as ObjectId _id
                    return context.Reader.ReadObjectId().ToString();

                case BsonType.Null:
                    context.Reader.ReadNull();
                    return null!;

                case BsonType.Undefined:
                    context.Reader.ReadUndefined();
                    return null!;

                default:
                    // Fallback: skip unknown types and return null
                    context.Reader.SkipValue();
                    return null!;
            }
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
            }
            else
            {
                context.Writer.WriteString(value);
            }
        }

        public FlexibleStringSerializer WithRepresentation(BsonType representation)
        {
            // We always handle every numeric/string type, so the representation is
            // irrelevant to our logic. Return a copy to satisfy the interface contract
            // (this is what prevents the "not configurable using BsonRepresentationAttribute" error).
            return new FlexibleStringSerializer(representation);
        }

        IBsonSerializer IRepresentationConfigurable.WithRepresentation(BsonType representation)
            => WithRepresentation(representation);
    }
}
