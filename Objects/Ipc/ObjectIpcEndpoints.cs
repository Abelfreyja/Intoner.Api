namespace Intoner.Objects.Api.Ipc;

/// <summary> public object ipc endpoint labels </summary>
public static class ObjectIpcEndpoints
{
    public const string Prefix = "Intoner.Objects.";

    /// <summary> object lifecycle event labels </summary>
    public static class Events
    {
        public const string Initialized = $"{Prefix}{nameof(Initialized)}";
        public const string Disposed = $"{Prefix}{nameof(Disposed)}";
        public const string PersistentSceneChanged = $"{Prefix}{nameof(PersistentSceneChanged)}";
    }

    /// <summary> object api state labels </summary>
    public static class State
    {
        public const string ApiVersion = $"{Prefix}{nameof(ApiVersion)}";
        public const string ApiBreakingVersion = $"{Prefix}{nameof(ApiBreakingVersion)}";
    }

    /// <summary> persistent layout labels </summary>
    public static class Layouts
    {
        public const string GetLayouts = $"{Prefix}{nameof(GetLayouts)}";
        public const string GetLoadedLayouts = $"{Prefix}{nameof(GetLoadedLayouts)}";
        public const string GetDefaultLayout = $"{Prefix}{nameof(GetDefaultLayout)}";
        public const string CreateLayout = $"{Prefix}{nameof(CreateLayout)}";
        public const string SaveCurrentLayout = $"{Prefix}{nameof(SaveCurrentLayout)}";
        public const string SetDefaultLayout = $"{Prefix}{nameof(SetDefaultLayout)}";
        public const string ClearDefaultLayout = $"{Prefix}{nameof(ClearDefaultLayout)}";
        public const string DeleteLayout = $"{Prefix}{nameof(DeleteLayout)}";
    }

    /// <summary> temporary layout and collection labels </summary>
    public static class Temporary
    {
        public const string GetTemporaryLayouts = $"{Prefix}{nameof(GetTemporaryLayouts)}";
        public const string ApplyTemporaryLayout = $"{Prefix}{nameof(ApplyTemporaryLayout)}";
        public const string ApplyTemporaryObjectChanges = $"{Prefix}{nameof(ApplyTemporaryObjectChanges)}";
        public const string UpsertTemporaryObject = $"{Prefix}{nameof(UpsertTemporaryObject)}";
        public const string PatchTemporaryObject = $"{Prefix}{nameof(PatchTemporaryObject)}";
        public const string RemoveTemporaryObject = $"{Prefix}{nameof(RemoveTemporaryObject)}";
        public const string RemoveTemporaryLayout = $"{Prefix}{nameof(RemoveTemporaryLayout)}";
        public const string ApplyTemporaryCollections = $"{Prefix}{nameof(ApplyTemporaryCollections)}";
        public const string UpsertTemporaryCollection = $"{Prefix}{nameof(UpsertTemporaryCollection)}";
        public const string RemoveTemporaryCollections = $"{Prefix}{nameof(RemoveTemporaryCollections)}";
        public const string BuildTemporarySource = $"{Prefix}{nameof(BuildTemporarySource)}";
    }

    /// <summary> object query labels </summary>
    public static class Queries
    {
        public const string GetSceneSnapshot = $"{Prefix}{nameof(GetSceneSnapshot)}";
        public const string GetObject = $"{Prefix}{nameof(GetObject)}";
    }

    /// <summary> object mutation labels </summary>
    public static class Mutations
    {
        public const string CreateObject = $"{Prefix}{nameof(CreateObject)}";
        public const string ImportObject = $"{Prefix}{nameof(ImportObject)}";
        public const string UpdateObject = $"{Prefix}{nameof(UpdateObject)}";
        public const string PatchObject = $"{Prefix}{nameof(PatchObject)}";
        public const string RemoveObject = $"{Prefix}{nameof(RemoveObject)}";
        public const string DuplicateObject = $"{Prefix}{nameof(DuplicateObject)}";
    }

    /// <summary> object runtime state labels </summary>
    public static class Runtime
    {
        public const string GetRuntimeStates = $"{Prefix}{nameof(GetRuntimeStates)}";
        public const string GetRuntimeState = $"{Prefix}{nameof(GetRuntimeState)}";
    }
}
