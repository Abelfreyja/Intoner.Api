namespace Intoner.Objects.Api;

/// <summary> public object api version constants </summary>
public static class ObjectApiVersions
{
    public const int Breaking = 1;
    public const int Feature = 0;

    /// <summary> current object api version </summary>
    public static ObjectApiVersion Current
        => new(Breaking, Feature);
}
