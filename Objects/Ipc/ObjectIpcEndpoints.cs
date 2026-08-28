namespace Intoner.Objects.Api.Ipc;

/// <summary> common metadata for endpoint </summary>
public interface IIpcEndpoint
{
    /// <summary> stable label </summary>
    string Name { get; }

    /// <summary> capability required to use this endpoint </summary>
    ObjectApiCapabilities RequiredCapabilities { get; }
}

// generic arguments define the IPC signature at compile time
#pragma warning disable S2326

/// <summary> function endpoint </summary>
/// <param name="Name"> stable label </param>
/// <param name="RequiredCapabilities"> capability required to use the endpoint </param>
public readonly record struct IpcEndpoint<TReturn>(string Name, ObjectApiCapabilities RequiredCapabilities) : IIpcEndpoint;

/// <summary> function endpoint with one argument </summary>
/// <param name="Name"> stable label </param>
/// <param name="RequiredCapabilities"> capability required to use the endpoint </param>
public readonly record struct IpcEndpoint<T, TReturn>(string Name, ObjectApiCapabilities RequiredCapabilities) : IIpcEndpoint;

/// <summary> function endpoint with two arguments </summary>
/// <param name="Name"> stable label </param>
/// <param name="RequiredCapabilities"> capability required to use the endpoint </param>
public readonly record struct IpcEndpoint<T1, T2, TReturn>(string Name, ObjectApiCapabilities RequiredCapabilities) : IIpcEndpoint;

/// <summary> event endpoint </summary>
/// <param name="Name"> stable label </param>
/// <param name="RequiredCapabilities"> capability required to receive the event </param>
public readonly record struct IpcEventEndpoint<T>(string Name, ObjectApiCapabilities RequiredCapabilities) : IIpcEndpoint;

#pragma warning restore S2326

/// <summary> stable typed endpoints </summary>
public static class ObjectIpcEndpoints
{
    /// <summary> it's just a prefix </summary>
    public const string Prefix = "Intoner.Api.";

    /// <summary> plugin lifecycle event labels </summary>
    public static class Events
    {
        /// <summary> host lifecycle event </summary>
        public static readonly IpcEventEndpoint<ObjectApiStateChanged> StateChanged = new(Prefix + "Events.StateChanged", ObjectApiCapabilities.None);
        /// <summary> composed scene revision event </summary>
        public static readonly IpcEventEndpoint<ObjectSceneChanged> SceneChanged = new(Prefix + "Events.SceneChanged", ObjectApiCapabilities.RevisionEvents);
        /// <summary> persistent scene revision event </summary>
        public static readonly IpcEventEndpoint<PersistentObjectSceneChanged> PersistentSceneChanged = new(Prefix + "Events.PersistentSceneChanged", ObjectApiCapabilities.RevisionEvents);
        /// <summary> saved layouts changed event </summary>
        public static readonly IpcEventEndpoint<SavedObjectLayoutsChanged> SavedLayoutsChanged = new(Prefix + "Events.SavedLayoutsChanged", ObjectApiCapabilities.SavedLayoutEvents);
    }

    /// <summary> API state operations </summary>
    public static class State
    {
        /// <summary> gets current host information </summary>
        public static readonly IpcEndpoint<ObjectApiInfo> GetInfo = new(Prefix + "State.GetInfo", ObjectApiCapabilities.None);
    }

    /// <summary> saved layout operations </summary>
    public static class Layouts
    {
        /// <summary> gets identifying information for all saved layouts </summary>
        public static readonly IpcEndpoint<SavedObjectLayoutsSnapshot> GetAll = new(Prefix + "Layouts.GetAll", ObjectApiCapabilities.Layouts);
        /// <summary> gets one saved layout with its complete content </summary>
        public static readonly IpcEndpoint<Guid, SavedObjectLayout?> Get = new(Prefix + "Layouts.Get", ObjectApiCapabilities.Layouts);
        /// <summary> gets the default layout id </summary>
        public static readonly IpcEndpoint<Guid?> GetDefault = new(Prefix + "Layouts.GetDefault", ObjectApiCapabilities.Layouts);
        /// <summary> creates an empty saved layout </summary>
        public static readonly IpcEndpoint<string, SavedObjectLayoutMutationResult> Create = new(Prefix + "Layouts.Create", ObjectApiCapabilities.Layouts);
        /// <summary> saves current persistent objects as a layout </summary>
        public static readonly IpcEndpoint<SavedObjectLayoutSaveRequest, SavedObjectLayoutMutationResult> SaveCurrent = new(Prefix + "Layouts.SaveCurrent", ObjectApiCapabilities.Layouts);
        /// <summary> sets the default layout </summary>
        public static readonly IpcEndpoint<SavedObjectLayoutSetDefaultRequest, SavedObjectLayoutMutationResult> SetDefault = new(Prefix + "Layouts.SetDefault", ObjectApiCapabilities.Layouts);
        /// <summary> clears the default layout </summary>
        public static readonly IpcEndpoint<long, SavedObjectLayoutMutationResult> ClearDefault = new(Prefix + "Layouts.ClearDefault", ObjectApiCapabilities.Layouts);
        /// <summary> deletes a saved layout </summary>
        public static readonly IpcEndpoint<SavedObjectLayoutDeleteRequest, SavedObjectLayoutMutationResult> Delete = new(Prefix + "Layouts.Delete", ObjectApiCapabilities.Layouts);
    }

    /// <summary> temporary source operations </summary>
    public static class TemporarySources
    {
        /// <summary> gets temporary sources owned by the caller </summary>
        public static readonly IpcEndpoint<IReadOnlyList<TemporarySourceInfo>> GetAll = new(Prefix + "TemporarySources.GetAll", ObjectApiCapabilities.TemporarySources);
        /// <summary> replaces one complete temporary source </summary>
        public static readonly IpcEndpoint<TemporarySourceApplyRequest, TemporarySourceMutationResult> Apply = new(Prefix + "TemporarySources.Apply", ObjectApiCapabilities.TemporarySources);
        /// <summary> applies ordered object changes to one temporary source </summary>
        public static readonly IpcEndpoint<TemporaryObjectChangeSet, TemporarySourceMutationResult> ApplyObjectChanges = new(Prefix + "TemporarySources.ApplyObjectChanges", ObjectApiCapabilities.TemporaryObjectChanges);
        /// <summary> removes one temporary source </summary>
        public static readonly IpcEndpoint<TemporarySourceRemoveRequest, TemporarySourceMutationResult> Remove = new(Prefix + "TemporarySources.Remove", ObjectApiCapabilities.TemporarySources);
    }

    /// <summary> sharing and payload construction operations </summary>
    public static class Sharing
    {
        /// <summary> builds a transportable temporary source payload </summary>
        public static readonly IpcEndpoint<TemporarySourceBuildRequest, CancellationToken, Task<byte[]>> BuildSource = new(Prefix + "Sharing.BuildSource", ObjectApiCapabilities.SourceBuilder);
    }

    /// <summary> complete scene queries </summary>
    public static class Scene
    {
        /// <summary> gets the complete composed scene </summary>
        public static readonly IpcEndpoint<ObjectSceneSnapshot> GetSnapshot = new(Prefix + "Scene.GetSnapshot", ObjectApiCapabilities.SceneQueries);
        /// <summary> gets one object from the composed scene </summary>
        public static readonly IpcEndpoint<Guid, WorldObject?> GetObject = new(Prefix + "Scene.GetObject", ObjectApiCapabilities.SceneQueries);
        /// <summary> gets the layouts composed into the current scene </summary>
        public static readonly IpcEndpoint<IReadOnlyList<LoadedObjectLayout>> GetLoadedLayouts = new(Prefix + "Scene.GetLoadedLayouts", ObjectApiCapabilities.SceneQueries);
    }

    /// <summary> persistent scene queries and updates </summary>
    public static class PersistentScene
    {
        /// <summary> gets persistent scene state </summary>
        public static readonly IpcEndpoint<PersistentObjectSceneSnapshot> GetSnapshot = new(Prefix + "PersistentScene.GetSnapshot", ObjectApiCapabilities.SceneQueries);
        /// <summary> gets one object from the current persistent scene </summary>
        public static readonly IpcEndpoint<Guid, PersistentObject?> GetObject = new(Prefix + "PersistentScene.GetObject", ObjectApiCapabilities.SceneQueries);
        /// <summary> replaces persistent scene state when its revision is current </summary>
        public static readonly IpcEndpoint<PersistentObjectSceneApplyRequest, PersistentObjectSceneMutationResult> Apply = new(Prefix + "PersistentScene.Apply", ObjectApiCapabilities.PersistentSceneApply);
    }

    /// <summary> persistent object operations </summary>
    public static class Objects
    {
        /// <summary> creates a persistent object with new identity metadata </summary>
        public static readonly IpcEndpoint<PersistentObjectWriteRequest, ObjectMutationResult> Create = new(Prefix + "Objects.Create", ObjectApiCapabilities.PersistentObjects);
        /// <summary> imports a persistent object while preserving valid metadata </summary>
        public static readonly IpcEndpoint<PersistentObjectWriteRequest, ObjectMutationResult> Import = new(Prefix + "Objects.Import", ObjectApiCapabilities.PersistentObjects);
        /// <summary> replaces one persistent object </summary>
        public static readonly IpcEndpoint<PersistentObjectWriteRequest, ObjectMutationResult> Update = new(Prefix + "Objects.Update", ObjectApiCapabilities.PersistentObjects);
        /// <summary> partially updates one persistent object </summary>
        public static readonly IpcEndpoint<PersistentObjectPatchRequest, ObjectMutationResult> Patch = new(Prefix + "Objects.Patch", ObjectApiCapabilities.PersistentObjects);
        /// <summary> removes one persistent object </summary>
        public static readonly IpcEndpoint<PersistentObjectTargetRequest, ObjectMutationResult> Remove = new(Prefix + "Objects.Remove", ObjectApiCapabilities.PersistentObjects);
        /// <summary> duplicates one persistent object </summary>
        public static readonly IpcEndpoint<PersistentObjectTargetRequest, ObjectMutationResult> Duplicate = new(Prefix + "Objects.Duplicate", ObjectApiCapabilities.PersistentObjects);
    }

    /// <summary> live runtime object state queries </summary>
    public static class Runtime
    {
        /// <summary> gets runtime state for every composed object </summary>
        public static readonly IpcEndpoint<IReadOnlyList<RuntimeObjectState>> GetAll = new(Prefix + "Runtime.GetAll", ObjectApiCapabilities.RuntimeState);
        /// <summary> gets runtime state for one composed object </summary>
        public static readonly IpcEndpoint<Guid, RuntimeObjectState?> Get = new(Prefix + "Runtime.Get", ObjectApiCapabilities.RuntimeState);
    }
}
