using PigeonHunt;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SyncController : UdonSharpBehaviour
{
    [Header("Gun Shot Effects")]
    public AudioSource gunShotAudio;
    public AudioSource clayShotAudio;
    public ParticleSystem gunMuzzleFlash;

    [Header("Synced Objects")]
    [Tooltip("Simple synced active states. Supports the first 31 objects.")]
    public GameObject[] syncedObjects;

    [Header("Synced Index")]
    [Tooltip("Optional exclusive object group. The synced index enables one object and disables the others.")]
    public GameObject[] syncedIndexObjects;
    [Tooltip("Optional receiver called whenever the synced index changes.")]
    public UdonSharpBehaviour syncedIndexChangedReceiver;
    public string syncedIndexChangedEventName;

    [Header("Synced Start Request")]
    [Tooltip("Optional receiver called whenever a synced start request is received.")]
    public UdonSharpBehaviour syncedStartReceiver;
    public string syncedStartEventName;

    [Header("Mode1 Round Plan")]
    [Tooltip("Optional receiver called whenever a synced mode1 round plan is received.")]
    public UdonSharpBehaviour mode1RoundPlanReceiver;
    public string mode1RoundPlanEventName;

    [Header("Flow Events")]
    [Tooltip("Receivers for simple indexed flow events.")]
    public UdonSharpBehaviour[] flowEventReceivers;
    [Tooltip("Method names called on matching flow event receivers.")]
    public string[] flowEventNames;

    [UdonSynced] private int syncedObjectActiveMask;
    [UdonSynced] private int syncedIndex = -1;
    [UdonSynced] private int syncedStartIndex = -1;
    [UdonSynced] private int syncedStartRequestId;
    [UdonSynced] private int mode1RoundNumber;
    [UdonSynced] private int mode1RoundSeed;
    [UdonSynced] private int mode1RoundPlanRequestId;
    private int handledStartRequestId;
    private int handledMode1RoundPlanRequestId;

    void Start()
    {
        //if (Networking.IsOwner(gameObject))
        //{
        //    InitializeObjectMaskFromScene();
        //    ApplySyncedObjectStates();
        //}
    }

    public override void OnPlayerTriggerStay(VRCPlayerApi player)
    {
        //TODO:

        base.OnPlayerTriggerStay(player);
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        base.OnPlayerJoined(player);
    }

    [NetworkCallable]
    public void NetworkPlayGunShotAudio(bool useClayShotAudio)
    {
        QychuiUtilities.SafePlay(useClayShotAudio ? clayShotAudio : gunShotAudio);
    }

    public void SyncGunShotAudio(bool useClayShotAudio)
    {
        NetworkPlayGunShotAudio(useClayShotAudio);
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkPlayGunShotAudio), useClayShotAudio);
    }

    public void SyncGunMuzzleFlash()
    {
        NetworkPlayGunMuzzleFlash();
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkPlayGunMuzzleFlash));
    }

    public void NetworkPlayGunMuzzleFlash()
    {
        QychuiUtilities.SafePlay(gunMuzzleFlash);
    }

    public bool HasGunShotEffects()
    {
        return gunMuzzleFlash != null || gunShotAudio != null || clayShotAudio != null;
    }

    public bool HasGunShotAudio()
    {
        return gunShotAudio != null || clayShotAudio != null;
    }

    public bool HasGunMuzzleFlash()
    {
        return gunMuzzleFlash != null;
    }

    public void SetSyncedObjectActive(int objectIndex, bool active)
    {
        if (!IsValidSyncedObjectIndex(objectIndex))
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        var bit = 1 << objectIndex;
        if (active)
        {
            syncedObjectActiveMask |= bit;
        }
        else
        {
            syncedObjectActiveMask &= ~bit;
        }

        ApplySyncedObjectState(objectIndex, active);
        RequestSerialization();
    }

    public bool GetSyncedObjectActive(int objectIndex)
    {
        if (!IsValidSyncedObjectIndex(objectIndex))
        {
            return false;
        }

        return (syncedObjectActiveMask & (1 << objectIndex)) != 0;
    }

    public int GetSyncedIndex()
    {
        return syncedIndex;
    }

    public void SetSyncedIndex(int index)
    {
        if (!IsValidSyncedIndex(index))
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        syncedIndex = index;
        ApplySyncedIndexObjects();
        NotifySyncedIndexChanged();
        RequestSerialization();
    }

    public int GetSyncedStartIndex()
    {
        return syncedStartIndex;
    }

    public void SyncStartIndex(int index)
    {
        if (!IsValidSyncedIndex(index))
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        syncedIndex = index;
        syncedStartIndex = index;
        syncedStartRequestId++;
        ApplySyncedIndexObjects();
        NotifySyncedIndexChanged();
        ApplySyncedStartRequest();
        RequestSerialization();
    }

    public int GetMode1RoundNumber()
    {
        return mode1RoundNumber;
    }

    public int GetMode1RoundSeed()
    {
        return mode1RoundSeed;
    }

    public void SyncMode1RoundPlan(int roundNumber, int seed)
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        mode1RoundNumber = Mathf.Max(1, roundNumber);
        mode1RoundSeed = seed;
        mode1RoundPlanRequestId++;
        ApplyMode1RoundPlan();
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        ApplySyncedObjectStates();
        ApplySyncedIndexObjects();
        NotifySyncedIndexChanged();
        ApplyMode1RoundPlan();
        ApplySyncedStartRequest();
    }

    public void SyncFlowEvent(int eventIndex)
    {
        RunFlowEvent(eventIndex);
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkRunFlowEvent), eventIndex);
    }

    [NetworkCallable]
    public void NetworkRunFlowEvent(int eventIndex)
    {
        RunFlowEvent(eventIndex);
    }

    private void RunFlowEvent(int eventIndex)
    {
        if (flowEventReceivers == null ||
            flowEventNames == null ||
            eventIndex < 0 ||
            eventIndex >= flowEventReceivers.Length ||
            eventIndex >= flowEventNames.Length)
        {
            return;
        }

        var receiver = flowEventReceivers[eventIndex];
        var eventName = flowEventNames[eventIndex];
        if (receiver == null || string.IsNullOrEmpty(eventName))
        {
            return;
        }

        receiver.SendCustomEvent(eventName);
    }

    private void InitializeObjectMaskFromScene()
    {
        if (!Networking.IsOwner(gameObject) || syncedObjects == null)
        {
            return;
        }

        syncedObjectActiveMask = 0;
        var count = Mathf.Min(syncedObjects.Length, 31);
        for (int i = 0; i < count; i++)
        {
            if (syncedObjects[i] != null && syncedObjects[i].activeSelf)
            {
                syncedObjectActiveMask |= 1 << i;
            }
        }

        RequestSerialization();
    }

    private void ApplySyncedObjectStates()
    {
        if (syncedObjects == null)
        {
            return;
        }

        var count = Mathf.Min(syncedObjects.Length, 31);
        for (int i = 0; i < count; i++)
        {
            ApplySyncedObjectState(i, (syncedObjectActiveMask & (1 << i)) != 0);
        }
    }

    private void ApplySyncedIndexObjects()
    {
        if (syncedIndexObjects == null || syncedIndexObjects.Length == 0 || syncedIndex < 0)
        {
            return;
        }

        for (int i = 0; i < syncedIndexObjects.Length; i++)
        {
            var target = syncedIndexObjects[i];
            if (target == null)
            {
                continue;
            }

            var shouldActive = i == syncedIndex;
            if (target.activeSelf != shouldActive)
            {
                target.SetActive(shouldActive);
            }
        }
    }

    private void ApplySyncedObjectState(int objectIndex, bool active)
    {
        if (!IsValidSyncedObjectIndex(objectIndex))
        {
            return;
        }

        var target = syncedObjects[objectIndex];
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private bool IsValidSyncedObjectIndex(int objectIndex)
    {
        return syncedObjects != null &&
               objectIndex >= 0 &&
               objectIndex < syncedObjects.Length &&
               objectIndex < 31;
    }

    private bool IsValidSyncedIndex(int index)
    {
        if (index < 0)
        {
            return false;
        }

        return syncedIndexObjects == null ||
               syncedIndexObjects.Length == 0 ||
               index < syncedIndexObjects.Length;
    }

    private void NotifySyncedIndexChanged()
    {
        if (syncedIndexChangedReceiver == null || string.IsNullOrEmpty(syncedIndexChangedEventName))
        {
            return;
        }

        syncedIndexChangedReceiver.SendCustomEvent(syncedIndexChangedEventName);
    }

    private void ApplySyncedStartRequest()
    {
        if (syncedStartRequestId == handledStartRequestId || syncedStartIndex < 0)
        {
            return;
        }

        handledStartRequestId = syncedStartRequestId;
        if (syncedStartReceiver == null || string.IsNullOrEmpty(syncedStartEventName))
        {
            return;
        }

        syncedStartReceiver.SendCustomEvent(syncedStartEventName);
    }

    private void ApplyMode1RoundPlan()
    {
        if (mode1RoundPlanRequestId == handledMode1RoundPlanRequestId || mode1RoundNumber <= 0)
        {
            return;
        }

        handledMode1RoundPlanRequestId = mode1RoundPlanRequestId;
        if (mode1RoundPlanReceiver == null || string.IsNullOrEmpty(mode1RoundPlanEventName))
        {
            return;
        }

        mode1RoundPlanReceiver.SendCustomEvent(mode1RoundPlanEventName);
    }
}
