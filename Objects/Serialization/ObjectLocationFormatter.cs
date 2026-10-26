using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Intoner.Objects.Api;

[ExcludeFormatterFromSourceGeneratedResolver]
internal sealed class ObjectLocationFormatter(bool legacy) : IMessagePackFormatter<ObjectLocationData?>
{
    private static readonly IMessagePackFormatter<ObjectLocationData?> CurrentFormatter = StandardResolver.Instance.GetFormatterWithVerify<ObjectLocationData?>();

    public ObjectLocationData? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return null;
        }

        // a mismatched version must not turn a missing world field into an unrestricted location otherwise bad
        MessagePackReader fields = reader.CreatePeekReader();
        string worldField = legacy ? nameof(LegacyObjectLocation.WorldId) : nameof(ObjectLocationData.Worlds);
        bool hasWorld = false;
        int count = fields.ReadMapHeader();
        for (int index = 0; index < count; ++index)
        {
            string? name = fields.ReadString();

            if (name is nameof(LegacyObjectLocation.WorldId) or nameof(ObjectLocationData.Worlds))
            {
                if (hasWorld || !string.Equals(name, worldField, StringComparison.Ordinal))
                {
                    throw new MessagePackSerializationException("The object location does not match its declared API version.");
                }

                hasWorld = true;
            }

            fields.Skip();
        }

        if (!hasWorld)
        {
            throw new MessagePackSerializationException("The object location has no world selection field.");
        }

        return legacy
            ? MessagePackSerializer.Deserialize<LegacyObjectLocation>(ref reader, options).ToLocation()
            : CurrentFormatter.Deserialize(ref reader, options);
    }

    public void Serialize(ref MessagePackWriter writer, ObjectLocationData? value, MessagePackSerializerOptions options)
    {
        if (legacy)
        {
            throw new NotSupportedException("Writing the legacy location format is not supported.");
        }

        CurrentFormatter.Serialize(ref writer, value, options);
    }
}
