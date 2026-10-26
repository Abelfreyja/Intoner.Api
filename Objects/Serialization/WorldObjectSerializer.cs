using MessagePack;
using MessagePack.Resolvers;

namespace Intoner.Objects.Api;

/// <summary> reads supported object formats and writes the current format </summary>
/// <remarks> payloads are MessagePack arrays and store their API version separately! </remarks>
public static class WorldObjectSerializer
{
    internal static readonly MessagePackSerializerOptions CurrentOptions = MessagePackSerializerOptions.Standard
        .WithSecurity(MessagePackSecurity.UntrustedData)
        .WithResolver(CompositeResolver.Create([new ObjectLocationFormatter(legacy: false)], [StandardResolver.Instance]));

    internal static readonly MessagePackSerializerOptions LegacyOptions = CurrentOptions
        .WithResolver(CompositeResolver.Create([new ObjectLocationFormatter(legacy: true)], [StandardResolver.Instance]));

    /// <summary> writes objects using <see cref="ObjectApiVersions.Current"/> </summary>
    /// <param name="objects"> complete object array to encode </param>
    /// <returns> uncompressed MessagePack bytes </returns>
    public static byte[] Serialize(IReadOnlyList<WorldObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        if (objects.Any(static item => item is null))
        {
            throw new ArgumentException("Objects cannot contain null entries.", nameof(objects));
        }

        return MessagePackSerializer.Serialize(objects, CurrentOptions);
    }

    /// <summary> reads payloads (using the API version) into current objects </summary>
    /// <param name="payload"> one complete, uncompressed MessagePack object array </param>
    /// <param name="version"> API version recorded with the original payload </param>
    /// <returns> decoded objects with legacy location data migrated </returns>
    /// <exception cref="NotSupportedException"> the declared version is unsupported </exception>
    /// <exception cref="InvalidDataException"> the payload is malformed, contains null objects or has trailing data </exception>
    public static WorldObject[] Deserialize(ReadOnlyMemory<byte> payload, ObjectApiVersion version)
    {
        MessagePackSerializerOptions options = version switch
        {
            { Breaking: 1, Feature: 0 } => LegacyOptions,
            { Breaking: 2, Feature: 0 } => CurrentOptions,
            _ => throw new NotSupportedException($"Unsupported Intoner object API version: {version.Breaking}.{version.Feature}."),
        };

        try
        {
            WorldObject[] objects = MessagePackSerializer.Deserialize<WorldObject[]>(payload, options, out int bytesRead);

            if (objects is null || objects.Any(static item => item is null))
            {
                throw new InvalidDataException("The object payload must be an array without null entries.");
            }

            if (bytesRead != payload.Length)
            {
                throw new InvalidDataException("The object payload contains trailing data.");
            }

            return objects;
        }
        catch (MessagePackSerializationException exception)
        {
            throw new InvalidDataException("The Intoner object payload is invalid.", exception);
        }
    }
}

// the source generator declaration
[GeneratedMessagePackResolver]
internal partial class ObjectApiResolver
{
}