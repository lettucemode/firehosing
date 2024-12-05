
using System.Text.Json;
using System.Text.Json.Serialization;

namespace firehosing;

public class ShortenByteArrayJsonConverter : JsonConverter<ArraySegment<byte>> {
    public override ArraySegment<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
    public override void Write(Utf8JsonWriter writer, ArraySegment<byte> val, JsonSerializerOptions options) {
        writer.WriteStringValue("[...]");
    }
}
