using MessagePack;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intoner.Objects.Api;

// location format used by API 1, superseded by API 2 which allows way more location things overall
[MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
internal sealed record LegacyObjectLocation(
    ushort WorldId, string WorldName, uint TerritoryId, string TerritoryName,
    uint DivisionId, uint WardId, uint HouseId, uint RoomId)
{
    public ObjectLocationData ToLocation()
        => new(WorldId == 0 ? null : new(false, worldIds: [WorldId]),
            TerritoryId, TerritoryName, DivisionId, WardId, HouseId, RoomId);
}

internal sealed class LegacyObjectLocationConverter : JsonConverter<ObjectLocationData>
{
    public override ObjectLocationData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (JsonSerializer.Deserialize<LegacyObjectLocation>(ref reader, options)
            ?? throw new JsonException("Missing legacy location data.")).ToLocation();

    public override void Write(Utf8JsonWriter writer, ObjectLocationData value, JsonSerializerOptions options)
        => throw new NotSupportedException("Writing the legacy location format is not supported.");
}
