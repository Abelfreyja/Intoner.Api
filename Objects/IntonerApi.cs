using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Intoner.Objects.Api.Ipc;
using System.Diagnostics;

namespace Intoner.Objects.Api;

/// <summary> current connection state of the intoner API client </summary>
public enum IntonerApiConnectionStatus
{
    /// <summary> intoner is not currently exposing the API </summary>
    Unavailable,
    /// <summary> intoner is connected and ready </summary>
    Ready,
    /// <summary> intoner is available, but its API version is incompatible </summary>
    Incompatible,
    /// <summary> intoner is shutting down its API </summary>
    Disposing,
    /// <summary> this client has been disposed </summary>
    Disposed,
    /// <summary> checking the intoner API failed </summary>
    Faulted,
}

/// <summary> describes intoner's current availability to this client </summary>
/// <param name="Status"> current connection state </param>
/// <param name="Info"> details reported by intoner, when available </param>
/// <param name="Message"> reason the connection is unavailable </param>
public sealed record IntonerApiAvailability(
    IntonerApiConnectionStatus Status,
    ObjectApiInfo? Info = null,
    string Message = "")
{
    /// <summary> whether intoner is connected and ready </summary>
    public bool IsAvailable => Status == IntonerApiConnectionStatus.Ready;
}

/// <summary> thrown when an operation is attempted while intoner is unavailable </summary>
public sealed class IntonerApiUnavailableException : InvalidOperationException
{
    /// <summary> initializes the exception from the current availability state </summary>
    /// <param name="availability"> availability state that prevented the operation </param>
    public IntonerApiUnavailableException(IntonerApiAvailability availability)
        : base(CreateMessage(availability))
    {
        Availability = availability;
    }

    /// <summary> availability state that prevented the operation </summary>
    public IntonerApiAvailability Availability { get; }

    private static string CreateMessage(IntonerApiAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        return string.IsNullOrWhiteSpace(availability.Message)
            ? $"Intoner API is not available ({availability.Status})."
            : availability.Message;
    }
}

/// <summary> thrown when intoner does not support the capabilities required by an operation </summary>
public sealed class IntonerApiCapabilityException : NotSupportedException
{
    /// <summary> initializes the exception with the required and available capabilities </summary>
    /// <param name="required"> capabilities required by the operation </param>
    /// <param name="available"> capabilities supported by intoner </param>
    public IntonerApiCapabilityException(ObjectApiCapabilities required, ObjectApiCapabilities available)
        : base($"Intoner API operation requires {required}; the host advertises {available}.")
    {
        Required = required;
        Available = available;
    }

    /// <summary> capabilities required by the operation </summary>
    public ObjectApiCapabilities Required { get; }

    /// <summary> capabilities supported by intoner </summary>
    public ObjectApiCapabilities Available { get; }
}

/// <summary> client for connecting to intoner through Dalamud's IPC </summary>
public sealed class IntonerApi : IDisposable
{
    private static readonly IntonerApiAvailability Unavailable = new(IntonerApiConnectionStatus.Unavailable);

    private readonly IntonerApiConnection _connection;
    private readonly ICallGateSubscriber<ObjectApiInfo> _getInfo;
    private readonly List<Action> _unsubscribeActions = [];
    private readonly Lock _refreshLock = new();
    private readonly Lock _callbackLock = new();
    private IntonerApiAvailability _availability = Unavailable;
    private Guid _revisionInstanceId;
    private long _lastSceneRevision;
    private long _lastPersistentRevision;
    private long _lastSavedLayoutsRevision;
    private long _availabilityVersion;
    private long _nextRefreshSequence;
    private long _appliedRefreshSequence;
    private int _disposed;

    /// <summary> connects to intoner and begins listening for API events </summary>
    /// <param name="pluginInterface"> Dalamud plugin interface owned by the calling plugin </param>
    public IntonerApi(IDalamudPluginInterface pluginInterface)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);

        _connection = new IntonerApiConnection(pluginInterface, () => Availability, HandleHostUnavailable);
        _getInfo = _connection.GetSubscriber(ObjectIpcEndpoints.State.GetInfo);

        Layouts = new LayoutOperations(_connection);
        TemporarySources = new TemporarySourceOperations(_connection);
        Sharing = new SharingOperations(_connection);
        Scene = new SceneOperations(_connection);
        PersistentScene = new PersistentSceneOperations(_connection);
        Objects = new ObjectOperations(_connection);
        Runtime = new RuntimeOperations(_connection);

        try
        {
            Subscribe(ObjectIpcEndpoints.Events.StateChanged, OnStateChanged);
            Subscribe(ObjectIpcEndpoints.Events.SceneChanged, OnSceneChanged);
            Subscribe(ObjectIpcEndpoints.Events.PersistentSceneChanged, OnPersistentSceneChanged);
            Subscribe(ObjectIpcEndpoints.Events.SavedLayoutsChanged, OnSavedLayoutsChanged);
            _ = RefreshAvailability();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary> operations for saved layouts </summary>
    public LayoutOperations Layouts { get; }

    /// <summary> operations for temporary sources owned by this plugin </summary>
    public TemporarySourceOperations TemporarySources { get; }

    /// <summary> helpers for preparing temporary sources for transport </summary>
    public SharingOperations Sharing { get; }

    /// <summary> queries for the complete scene, including temporary sources </summary>
    public SceneOperations Scene { get; }

    /// <summary> queries and updates for the persistent scene </summary>
    public PersistentSceneOperations PersistentScene { get; }

    /// <summary> operations for persistent objects </summary>
    public ObjectOperations Objects { get; }

    /// <summary> queries for live game object state </summary>
    public RuntimeOperations Runtime { get; }

    /// <summary> current intoner connection state </summary>
    public IntonerApiAvailability Availability => Volatile.Read(ref _availability);

    /// <summary> latest information reported by intoner, even when it is incompatible or shutting down </summary>
    public ObjectApiInfo? Info => Availability.Info;

    /// <summary> whether intoner is connected and ready to handle requests </summary>
    public bool IsAvailable => Availability.IsAvailable;

    /// <summary> raised when this client's connection state changes </summary>
    public event Action<IntonerApiAvailability>? AvailabilityChanged;

    /// <summary> raised when the complete scene changes </summary>
    public event Action<ObjectSceneChanged>? SceneChanged;

    /// <summary> raised when the persistent scene changes </summary>
    public event Action<PersistentObjectSceneChanged>? PersistentSceneChanged;

    /// <summary> raised when the saved layouts change </summary>
    public event Action<SavedObjectLayoutsChanged>? SavedLayoutsChanged;

    /// <summary> checks whether intoner supports all requested capabilities </summary>
    /// <param name="capabilities"> capabilities to check </param>
    /// <returns> true when intoner is ready and supports every requested capability </returns>
    public bool Supports(ObjectApiCapabilities capabilities)
    {
        IntonerApiAvailability availability = Availability;
        return availability.IsAvailable && availability.Info!.Supports(capabilities);
    }

    /// <summary> checks intoner again and updates the current connection state </summary>
    /// <returns> updated availability state </returns>
    public IntonerApiAvailability RefreshAvailability()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        long availabilityVersion;
        long refreshSequence;
        IntonerApiAvailability observed;
        lock (_refreshLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            availabilityVersion = Volatile.Read(ref _availabilityVersion);
            refreshSequence = ++_nextRefreshSequence;

            try
            {
                observed = CreateAvailability(_connection.InvokeProbe(_getInfo));
            }
            catch (IpcNotReadyError)
            {
                observed = Unavailable;
            }
            catch (Exception ex)
            {
                Trace.TraceError("Intoner API availability probe failed: {0}", ex);
                observed = new IntonerApiAvailability(IntonerApiConnectionStatus.Faulted, Message: ex.Message);
            }
        }

        lock (_callbackLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Availability;
            }

            bool changed = false;
            if (Volatile.Read(ref _availabilityVersion) == availabilityVersion
                && refreshSequence > _appliedRefreshSequence)
            {
                // completed checks may reach this callback out of order
                _appliedRefreshSequence = refreshSequence;
                changed = SetAvailability(observed);
            }

            if (changed)
            {
                InvokeHandlers(AvailabilityChanged, observed, nameof(AvailabilityChanged));
            }

            return Availability;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_callbackLock)
        {
            Interlocked.Increment(ref _availabilityVersion);
            SetAvailability(new IntonerApiAvailability(IntonerApiConnectionStatus.Disposed));
            AvailabilityChanged = null;
            SceneChanged = null;
            PersistentSceneChanged = null;
            SavedLayoutsChanged = null;
        }

        for (int i = _unsubscribeActions.Count - 1; i >= 0; --i)
        {
            _unsubscribeActions[i]();
        }

        _unsubscribeActions.Clear();

        lock (_refreshLock)
        {
            _connection.Dispose();
        }
    }

    private void OnStateChanged(ObjectApiStateChanged change)
        => UpdateAvailability(CreateAvailability(change.Info));

    private void OnSceneChanged(ObjectSceneChanged change)
        => PublishRevision(change, change.InstanceId, change.SceneRevision, ref _lastSceneRevision, SceneChanged, nameof(SceneChanged));

    private void OnPersistentSceneChanged(PersistentObjectSceneChanged change)
        => PublishRevision(change, change.InstanceId, change.PersistentRevision, ref _lastPersistentRevision, PersistentSceneChanged, nameof(PersistentSceneChanged));

    private void OnSavedLayoutsChanged(SavedObjectLayoutsChanged change)
        => PublishRevision(change, change.InstanceId, change.SavedLayoutsRevision, ref _lastSavedLayoutsRevision, SavedLayoutsChanged, nameof(SavedLayoutsChanged));

    private void Subscribe<T>(IpcEventEndpoint<T> endpoint, Action<T> handler)
    {
        ICallGateSubscriber<T, object?> subscriber = _connection.GetSubscriber(endpoint);
        Action<T> guardedHandler = value =>
        {
            if (endpoint.RequiredCapabilities != ObjectApiCapabilities.None
                && !Supports(endpoint.RequiredCapabilities))
            {
                return;
            }

            handler(value);
        };
        subscriber.Subscribe(guardedHandler);
        _unsubscribeActions.Add(() => subscriber.Unsubscribe(guardedHandler));
    }

    private void UpdateAvailability(
        IntonerApiAvailability availability,
        IntonerApiAvailability? expectedAvailability = null)
    {
        lock (_callbackLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (expectedAvailability is not null && !Equals(_availability, expectedAvailability))
            {
                return;
            }

            Interlocked.Increment(ref _availabilityVersion);
            bool changed = SetAvailability(availability);

            if (changed)
            {
                InvokeHandlers(AvailabilityChanged, availability, nameof(AvailabilityChanged));
            }
        }
    }

    private void PublishRevision<T>(
        T change,
        Guid instanceId,
        long revision,
        ref long lastRevision,
        Action<T>? handlers,
        string eventName)
    {
        lock (_callbackLock)
        {
            if (TryAcceptRevision(instanceId, revision, ref lastRevision))
            {
                InvokeHandlers(handlers, change, eventName);
            }
        }
    }

    private void InvokeHandlers<T>(Action<T>? handlers, T value, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                ((Action<T>)handler)(value);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Intoner API {0} handler failed: {1}", eventName, ex);
            }
        }
    }

    private bool TryAcceptRevision(Guid instanceId, long revision, ref long lastRevision)
    {
        if (Volatile.Read(ref _disposed) != 0
            || _availability is not { IsAvailable: true, Info: { } info }
            || info.InstanceId != instanceId
            || revision <= lastRevision)
        {
            return false;
        }

        lastRevision = revision;
        return true;
    }

    private void HandleHostUnavailable(IntonerApiAvailability expectedAvailability)
        => UpdateAvailability(Unavailable, expectedAvailability);

    private bool SetAvailability(IntonerApiAvailability availability)
    {
        IntonerApiAvailability current = _availability;
        if (Equals(current, availability))
        {
            return false;
        }

        Guid instanceId = availability.IsAvailable ? availability.Info!.InstanceId : Guid.Empty;
        if (_revisionInstanceId != instanceId)
        {
            _revisionInstanceId = instanceId;
            _lastSceneRevision = 0;
            _lastPersistentRevision = 0;
            _lastSavedLayoutsRevision = 0;
        }

        Volatile.Write(ref _availability, availability);
        return true;
    }

    private static IntonerApiAvailability CreateAvailability(ObjectApiInfo info)
    {
        if (info.Version.Breaking != ObjectApiVersions.Breaking)
        {
            string incompatibility = $"Intoner API v{info.Version.Breaking}.{info.Version.Feature} is incompatible with client v{ObjectApiVersions.Breaking}.{ObjectApiVersions.Feature}.";
            return new IntonerApiAvailability(IntonerApiConnectionStatus.Incompatible, info, incompatibility);
        }

        IntonerApiConnectionStatus status = info.State switch
        {
            ObjectApiHostState.Ready => IntonerApiConnectionStatus.Ready,
            ObjectApiHostState.Disposing => IntonerApiConnectionStatus.Disposing,
            _ => IntonerApiConnectionStatus.Faulted,
        };
        string diagnostic = status == IntonerApiConnectionStatus.Faulted
            ? $"Intoner reported unsupported API host state {(int)info.State}."
            : string.Empty;
        return new IntonerApiAvailability(status, info, diagnostic);
    }

    /// <summary> operations for saved layouts </summary>
    public sealed class LayoutOperations
    {
        private readonly Func<SavedObjectLayoutsSnapshot> _getAll;
        private readonly Func<Guid, SavedObjectLayout?> _get;
        private readonly Func<Guid?> _getDefault;
        private readonly Func<string, SavedObjectLayoutMutationResult> _create;
        private readonly Func<SavedObjectLayoutSaveRequest, SavedObjectLayoutMutationResult> _saveCurrent;
        private readonly Func<SavedObjectLayoutSetDefaultRequest, SavedObjectLayoutMutationResult> _setDefault;
        private readonly Func<long, SavedObjectLayoutMutationResult> _clearDefault;
        private readonly Func<SavedObjectLayoutDeleteRequest, SavedObjectLayoutMutationResult> _delete;

        internal LayoutOperations(IntonerApiConnection connection)
        {
            _getAll = connection.Bind(ObjectIpcEndpoints.Layouts.GetAll);
            _get = connection.Bind(ObjectIpcEndpoints.Layouts.Get);
            _getDefault = connection.Bind(ObjectIpcEndpoints.Layouts.GetDefault);
            _create = connection.Bind(ObjectIpcEndpoints.Layouts.Create);
            _saveCurrent = connection.Bind(ObjectIpcEndpoints.Layouts.SaveCurrent);
            _setDefault = connection.Bind(ObjectIpcEndpoints.Layouts.SetDefault);
            _clearDefault = connection.Bind(ObjectIpcEndpoints.Layouts.ClearDefault);
            _delete = connection.Bind(ObjectIpcEndpoints.Layouts.Delete);
        }

        /// <summary> gets identifying information for all saved layouts and the revision for the complete list </summary>
        public SavedObjectLayoutsSnapshot GetAll() => _getAll();

        /// <summary> gets one saved layout with its complete content </summary>
        /// <param name="layoutId"> saved layout id </param>
        /// <returns> the saved layout, or null when it does not exist </returns>
        public SavedObjectLayout? Get(Guid layoutId) => _get(layoutId);

        /// <summary> gets the id of the default layout, when one is selected </summary>
        public Guid? GetDefault() => _getDefault();

        /// <summary> creates an empty saved layout </summary>
        public SavedObjectLayoutMutationResult Create(string name) => _create(name);

        /// <summary> saves the current persistent objects as a new layout </summary>
        public SavedObjectLayoutMutationResult SaveCurrent(string name, long expectedPersistentRevision)
            => _saveCurrent(new SavedObjectLayoutSaveRequest(expectedPersistentRevision, name));

        /// <summary> sets the default layout </summary>
        public SavedObjectLayoutMutationResult SetDefault(
            Guid layoutId,
            long expectedLayoutRevision,
            long expectedPersistentRevision)
            => _setDefault(new SavedObjectLayoutSetDefaultRequest(
                expectedPersistentRevision,
                expectedLayoutRevision,
                layoutId));

        /// <summary> clears the default layout selection </summary>
        public SavedObjectLayoutMutationResult ClearDefault(long expectedPersistentRevision)
            => _clearDefault(expectedPersistentRevision);

        /// <summary> deletes a saved layout </summary>
        public SavedObjectLayoutMutationResult Delete(
            Guid layoutId,
            long expectedLayoutRevision,
            long expectedPersistentRevision)
            => _delete(new SavedObjectLayoutDeleteRequest(
                expectedPersistentRevision,
                expectedLayoutRevision,
                layoutId));
    }

    /// <summary> operations for temporary sources owned by the calling plugin </summary>
    public sealed class TemporarySourceOperations
    {
        private readonly Func<IReadOnlyList<TemporarySourceInfo>> _getAll;
        private readonly Func<TemporarySourceApplyRequest, TemporarySourceMutationResult> _apply;
        private readonly Func<TemporaryObjectChangeSet, TemporarySourceMutationResult> _applyChanges;
        private readonly Func<TemporarySourceRemoveRequest, TemporarySourceMutationResult> _remove;

        internal TemporarySourceOperations(IntonerApiConnection connection)
        {
            _getAll = connection.Bind(ObjectIpcEndpoints.TemporarySources.GetAll);
            _apply = connection.Bind(ObjectIpcEndpoints.TemporarySources.Apply);
            _applyChanges = connection.Bind(ObjectIpcEndpoints.TemporarySources.ApplyObjectChanges);
            _remove = connection.Bind(ObjectIpcEndpoints.TemporarySources.Remove);
        }

        /// <summary> gets the temporary sources created by this plugin </summary>
        public IReadOnlyList<TemporarySourceInfo> GetAll() => _getAll();

        /// <summary> replaces a temporary source with the supplied state </summary>
        public TemporarySourceMutationResult Apply(TemporarySourceApplyRequest request)
            => _apply(request);

        /// <summary> applies an ordered set of changes to a temporary source </summary>
        public TemporarySourceMutationResult ApplyChanges(TemporaryObjectChangeSet changes)
            => _applyChanges(changes);

        /// <summary> removes a temporary source owned by this plugin </summary>
        public TemporarySourceMutationResult Remove(TemporarySourceRemoveRequest request)
            => _remove(request);
    }

    /// <summary> operations for building temporary source payloads </summary>
    public sealed class SharingOperations
    {
        private readonly Func<TemporarySourceBuildRequest, CancellationToken, Task<byte[]>> _buildTemporarySource;

        internal SharingOperations(IntonerApiConnection connection)
        {
            _buildTemporarySource = connection.Bind(ObjectIpcEndpoints.Sharing.BuildSource);
        }

        /// <summary> builds a temporary source payload from local objects and collection settings </summary>
        /// <param name="request"> objects and collections to include </param>
        /// <param name="cancellationToken"> cancellation token for resource resolution and payload creation </param>
        /// <returns> result containing the payload and any diagnostics </returns>
        public async Task<TemporarySourceBuildResult> BuildTemporarySourceAsync(
            TemporarySourceBuildRequest request,
            CancellationToken cancellationToken = default)
        {
            byte[] payload = await _buildTemporarySource(request, cancellationToken).ConfigureAwait(false);
            return TemporarySourceBuildResultWire.Deserialize(payload);
        }
    }

    /// <summary> queries for the complete scene </summary>
    public sealed class SceneOperations
    {
        private readonly Func<ObjectSceneSnapshot> _getSnapshot;
        private readonly Func<Guid, WorldObject?> _getObject;
        private readonly Func<IReadOnlyList<LoadedObjectLayout>> _getLoadedLayouts;

        internal SceneOperations(IntonerApiConnection connection)
        {
            _getSnapshot = connection.Bind(ObjectIpcEndpoints.Scene.GetSnapshot);
            _getObject = connection.Bind(ObjectIpcEndpoints.Scene.GetObject);
            _getLoadedLayouts = connection.Bind(ObjectIpcEndpoints.Scene.GetLoadedLayouts);
        }

        /// <summary> gets a consistent snapshot of the complete scene </summary>
        public ObjectSceneSnapshot GetSnapshot() => _getSnapshot();

        /// <summary> gets one object from the complete scene </summary>
        public WorldObject? GetObject(Guid objectId) => _getObject(objectId);

        /// <summary> gets the layouts currently loaded into the complete scene </summary>
        public IReadOnlyList<LoadedObjectLayout> GetLoadedLayouts() => _getLoadedLayouts();
    }

    /// <summary> queries and updates for persistent scene state </summary>
    public sealed class PersistentSceneOperations
    {
        private readonly Func<PersistentObjectSceneSnapshot> _getSnapshot;
        private readonly Func<Guid, PersistentObject?> _getObject;
        private readonly Func<PersistentObjectSceneApplyRequest, PersistentObjectSceneMutationResult> _apply;

        internal PersistentSceneOperations(IntonerApiConnection connection)
        {
            _getSnapshot = connection.Bind(ObjectIpcEndpoints.PersistentScene.GetSnapshot);
            _getObject = connection.Bind(ObjectIpcEndpoints.PersistentScene.GetObject);
            _apply = connection.Bind(ObjectIpcEndpoints.PersistentScene.Apply);
        }

        /// <summary> gets the persistent scene without caller owned temporary sources </summary>
        public PersistentObjectSceneSnapshot GetSnapshot() => _getSnapshot();

        /// <summary> gets one object from the persistent scene </summary>
        public PersistentObject? GetObject(Guid objectId) => _getObject(objectId);

        /// <summary> replaces the persistent scene when the expected revision is current </summary>
        public PersistentObjectSceneMutationResult Apply(PersistentObjectSceneApplyRequest request)
            => _apply(request);
    }

    /// <summary> operations for persistent objects </summary>
    public sealed class ObjectOperations
    {
        private readonly Func<PersistentObjectWriteRequest, ObjectMutationResult> _create;
        private readonly Func<PersistentObjectWriteRequest, ObjectMutationResult> _import;
        private readonly Func<PersistentObjectWriteRequest, ObjectMutationResult> _update;
        private readonly Func<PersistentObjectPatchRequest, ObjectMutationResult> _patch;
        private readonly Func<PersistentObjectTargetRequest, ObjectMutationResult> _remove;
        private readonly Func<PersistentObjectTargetRequest, ObjectMutationResult> _duplicate;

        internal ObjectOperations(IntonerApiConnection connection)
        {
            _create = connection.Bind(ObjectIpcEndpoints.Objects.Create);
            _import = connection.Bind(ObjectIpcEndpoints.Objects.Import);
            _update = connection.Bind(ObjectIpcEndpoints.Objects.Update);
            _patch = connection.Bind(ObjectIpcEndpoints.Objects.Patch);
            _remove = connection.Bind(ObjectIpcEndpoints.Objects.Remove);
            _duplicate = connection.Bind(ObjectIpcEndpoints.Objects.Duplicate);
        }

        /// <summary> creates a persistent object using intoner's set creation rules </summary>
        public ObjectMutationResult Create(PersistentObjectWriteRequest request)
            => _create(request);

        /// <summary> imports a persistent object while preserving valid supplied metadata </summary>
        public ObjectMutationResult Import(PersistentObjectWriteRequest request)
            => _import(request);

        /// <summary> replaces one persistent object </summary>
        public ObjectMutationResult Update(PersistentObjectWriteRequest request)
            => _update(request);

        /// <summary> updates selected fields on one persistent object </summary>
        public ObjectMutationResult Patch(PersistentObjectPatchRequest update)
            => _patch(update);

        /// <summary> removes one persistent object </summary>
        public ObjectMutationResult Remove(PersistentObjectTargetRequest request)
            => _remove(request);

        /// <summary> duplicates one persistent object </summary>
        public ObjectMutationResult Duplicate(PersistentObjectTargetRequest request)
            => _duplicate(request);
    }

    /// <summary> queries for live object state </summary>
    public sealed class RuntimeOperations
    {
        private readonly Func<IReadOnlyList<RuntimeObjectState>> _getAll;
        private readonly Func<Guid, RuntimeObjectState?> _get;

        internal RuntimeOperations(IntonerApiConnection connection)
        {
            _getAll = connection.Bind(ObjectIpcEndpoints.Runtime.GetAll);
            _get = connection.Bind(ObjectIpcEndpoints.Runtime.Get);
        }

        /// <summary> gets the live state of every object in the complete scene </summary>
        public IReadOnlyList<RuntimeObjectState> GetAll() => _getAll();

        /// <summary> gets the live state of one object </summary>
        public RuntimeObjectState? Get(Guid objectId) => _get(objectId);
    }
}

internal sealed class IntonerApiConnection : IDisposable
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Func<IntonerApiAvailability> _getAvailability;
    private readonly Action<IntonerApiAvailability> _hostUnavailable;
    private int _disposed;

    public IntonerApiConnection(
        IDalamudPluginInterface pluginInterface,
        Func<IntonerApiAvailability> getAvailability,
        Action<IntonerApiAvailability> hostUnavailable)
    {
        _pluginInterface = pluginInterface;
        _getAvailability = getAvailability;
        _hostUnavailable = hostUnavailable;
    }

    public ICallGateSubscriber<TReturn> GetSubscriber<TReturn>(IpcEndpoint<TReturn> endpoint)
        => _pluginInterface.GetIpcSubscriber<TReturn>(endpoint.Name);

    public ICallGateSubscriber<T, object?> GetSubscriber<T>(IpcEventEndpoint<T> endpoint)
        => _pluginInterface.GetIpcSubscriber<T, object?>(endpoint.Name);

    public Func<TReturn> Bind<TReturn>(IpcEndpoint<TReturn> endpoint)
    {
        ICallGateSubscriber<TReturn> subscriber = _pluginInterface.GetIpcSubscriber<TReturn>(endpoint.Name);
        return () => InvokeCore(subscriber.InvokeFunc, endpoint.RequiredCapabilities);
    }

    public Func<T, TReturn> Bind<T, TReturn>(IpcEndpoint<T, TReturn> endpoint)
    {
        ICallGateSubscriber<T, TReturn> subscriber = _pluginInterface.GetIpcSubscriber<T, TReturn>(endpoint.Name);
        return value => InvokeCore(() => subscriber.InvokeFunc(value), endpoint.RequiredCapabilities);
    }

    public Func<T1, T2, TReturn> Bind<T1, T2, TReturn>(IpcEndpoint<T1, T2, TReturn> endpoint)
    {
        ICallGateSubscriber<T1, T2, TReturn> subscriber = _pluginInterface.GetIpcSubscriber<T1, T2, TReturn>(endpoint.Name);
        return (value1, value2) => InvokeCore(
            () => subscriber.InvokeFunc(value1, value2),
            endpoint.RequiredCapabilities);
    }

    public TReturn InvokeProbe<TReturn>(ICallGateSubscriber<TReturn> subscriber)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return subscriber.InvokeFunc();
    }

    private TReturn InvokeCore<TReturn>(Func<TReturn> invoke, ObjectApiCapabilities requiredCapabilities)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        IntonerApiAvailability availability = _getAvailability();
        if (!availability.IsAvailable)
        {
            throw new IntonerApiUnavailableException(availability);
        }

        ObjectApiInfo info = availability.Info!;
        if (!info.Supports(requiredCapabilities))
        {
            throw new IntonerApiCapabilityException(requiredCapabilities, info.Capabilities);
        }

        try
        {
            return invoke();
        }
        catch (IpcNotReadyError)
        {
            _hostUnavailable(availability);
            throw;
        }
    }

    public void Dispose()
        => Interlocked.Exchange(ref _disposed, 1);
}
