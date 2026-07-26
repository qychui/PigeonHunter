using PigeonHunt;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SyncController : UdonSharpBehaviour
{
    #region Receiver Configuration And Synced Payloads

    [Header("Gun Shot Effects")]
    public AudioSource gunShotAudio;
    public AudioSource clayShotAudio;
    public ParticleSystem gunMuzzleFlash;
    [Tooltip("Optional receiver called whenever gun shot flash objects should be shown.")]
    public UdonSharpBehaviour gunShotFlashReceiver;
    public string gunShotFlashEventName;

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

    [Header("Mode1 Hit Event")]
    [Tooltip("Optional receiver called whenever a synced mode1 hit event is received.")]
    public UdonSharpBehaviour mode1HitReceiver;
    public string mode1HitEventName;

    [Header("Mode1 Shot Event")]
    [Tooltip("Optional receiver called whenever a synced mode1 non-hit shot event is received.")]
    public UdonSharpBehaviour mode1ShotReceiver;
    public string mode1ShotEventName;

    [Header("Mode1 Round Result")]
    [Tooltip("Optional receiver called whenever a synced mode1 round result is received.")]
    public UdonSharpBehaviour mode1RoundResultReceiver;
    public string mode1RoundResultEventName;

    [Header("Mode3 Wave Start")]
    [Tooltip("Optional receiver called whenever a synced mode3 wave starts.")]
    public UdonSharpBehaviour mode3WaveStartReceiver;
    public string mode3WaveStartEventName;

    [Header("Mode3 Hit Event")]
    [Tooltip("Optional receiver called whenever a synced mode3 clay hit event is received.")]
    public UdonSharpBehaviour mode3HitReceiver;
    public string mode3HitEventName;

    [Header("Mode3 Shot Event")]
    [Tooltip("Optional receiver called whenever a synced mode3 non-hit shot event is received.")]
    public UdonSharpBehaviour mode3ShotReceiver;
    public string mode3ShotEventName;

    [Header("Mode3 Round Snapshot")]
    [Tooltip("Optional receiver called when mode3 should realign at the next round start.")]
    public UdonSharpBehaviour mode3RoundSnapshotReceiver;
    public string mode3RoundSnapshotEventName;

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
    [UdonSynced] private int mode1HitPigeonPoolIndex = -1;
    [UdonSynced] private int mode1HitUsedShots;
    [UdonSynced] private int mode1HitRequestId;
    [UdonSynced] private int mode1ShotUsedShots;
    [UdonSynced] private int mode1ShotRequestId;
    [UdonSynced] private int mode1ResultRoundNumber;
    [UdonSynced] private int mode1ResultScore;
    [UdonSynced] private int mode1ResultHitCount;
    [UdonSynced] private bool mode1ResultPassed;
    [UdonSynced] private int mode1RoundResultRequestId;
    [UdonSynced] private int mode1StartGateRoundNumber;
    [UdonSynced] private int mode1StartGateSeed;
    [UdonSynced] private int mode3SnapshotRoundNumber;
    [UdonSynced] private int mode3SnapshotScore;
    [UdonSynced] private int mode3SnapshotDifficulty;
    [UdonSynced] private int mode3RoundSnapshotRequestId;
    [UdonSynced] private bool mode3RoundSnapshotActive;
    private int handledStartRequestId;
    private int handledMode1RoundPlanRequestId;
    private int handledMode1HitRequestId;
    private int handledMode1ShotRequestId;
    private int handledMode1RoundResultRequestId;
    private int handledMode3RoundSnapshotRequestId;
    private int mode3WaveRoundNumber;
    private int mode3WaveIndex;
    private int mode3WaveSeed;
    private int mode3WaveRequestId;
    private int mode3HitClayPoolIndex = -1;
    private int mode3HitUsedShots;
    private int mode3HitRequestId;
    private int mode3ShotUsedShots;
    private int mode3ShotRequestId;
    private int handledMode3WaveRequestId;
    private int handledMode3HitRequestId;
    private int handledMode3ShotRequestId;

    #endregion

    #region Ownership

    public void TransferOwnershipToLocalPlayer()
    {
        EnsureLocalOwner();
    }

    public bool IsLocalOwner()
    {
        return Networking.LocalPlayer != null && Networking.IsOwner(gameObject);
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        ApplySyncedVisualState();
        ApplyMode1RoundPlan();
        RequestSerialization();
    }

    private void EnsureLocalOwner()
    {
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    #endregion

    #region Visual Menu And Start Sync

    private void ApplySyncedVisualState()
    {
        ApplySyncedObjectStates();
        ApplySyncedIndexObjects();
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

    public void SyncGunShotFlash()
    {
        NetworkShowGunShotFlash();
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkShowGunShotFlash));
    }

    public void NetworkShowGunShotFlash()
    {
        SendReceiverEvent(gunShotFlashReceiver, gunShotFlashEventName);
    }

    public bool HasGunShotEffects()
    {
        return gunMuzzleFlash != null || gunShotAudio != null || clayShotAudio != null || HasGunShotFlash();
    }

    public bool HasGunShotAudio()
    {
        return gunShotAudio != null || clayShotAudio != null;
    }

    public bool HasGunMuzzleFlash()
    {
        return gunMuzzleFlash != null;
    }

    public bool HasGunShotFlash()
    {
        return gunShotFlashReceiver != null && !string.IsNullOrEmpty(gunShotFlashEventName);
    }

    public void SetSyncedObjectActive(int objectIndex, bool active)
    {
        if (!IsValidSyncedObjectIndex(objectIndex))
        {
            return;
        }

        EnsureLocalOwner();

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

        EnsureLocalOwner();

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

        EnsureLocalOwner();

        syncedIndex = index;
        syncedStartIndex = index;
        syncedStartRequestId++;
        ApplySyncedIndexObjects();
        NotifySyncedIndexChanged();
        ApplySyncedStartRequest();
        RequestSerialization();
    }

    #endregion

    #region Mode A And B Sync

    public int GetMode1RoundNumber()
    {
        return mode1RoundNumber;
    }

    public int GetMode1RoundSeed()
    {
        return mode1RoundSeed;
    }

    public int GetMode1StartGateRoundNumber()
    {
        return mode1StartGateRoundNumber;
    }

    public int GetMode1StartGateSeed()
    {
        return mode1StartGateSeed;
    }

    public void SyncMode1RoundPlan(int roundNumber, int seed)
    {
        EnsureLocalOwner();

        mode1RoundNumber = Mathf.Max(1, roundNumber);
        mode1RoundSeed = seed;
        mode1RoundPlanRequestId++;
        ApplyMode1RoundPlan();
        RequestSerialization();
    }

    public void SyncMode1StartGate(int roundNumber, int seed)
    {
        EnsureLocalOwner();

        mode1StartGateRoundNumber = Mathf.Max(1, roundNumber);
        mode1StartGateSeed = seed;
        RequestSerialization();
    }

    public int GetMode1HitPigeonPoolIndex()
    {
        return mode1HitPigeonPoolIndex;
    }

    public int GetMode1HitUsedShots()
    {
        return mode1HitUsedShots;
    }

    public void SyncMode1PigeonHit(int pigeonPoolIndex, int usedShots)
    {
        if (pigeonPoolIndex < 0)
        {
            return;
        }

        EnsureLocalOwner();

        mode1HitPigeonPoolIndex = pigeonPoolIndex;
        mode1HitUsedShots = Mathf.Max(0, usedShots);
        mode1HitRequestId++;
        RequestSerialization();
    }

    public int GetMode1ShotUsedShots()
    {
        return mode1ShotUsedShots;
    }

    public void SyncMode1ShotMiss(int usedShots)
    {
        EnsureLocalOwner();

        mode1ShotUsedShots = Mathf.Max(0, usedShots);
        mode1ShotRequestId++;
        RequestSerialization();
    }

    public int GetMode1ResultRoundNumber()
    {
        return mode1ResultRoundNumber;
    }

    public int GetMode1ResultScore()
    {
        return mode1ResultScore;
    }

    public int GetMode1ResultHitCount()
    {
        return mode1ResultHitCount;
    }

    public bool GetMode1ResultPassed()
    {
        return mode1ResultPassed;
    }

    public void SyncMode1RoundResult(int roundNumber, int score, int hitCount, bool passed)
    {
        EnsureLocalOwner();

        mode1ResultRoundNumber = Mathf.Max(1, roundNumber);
        mode1ResultScore = Mathf.Max(0, score);
        mode1ResultHitCount = Mathf.Max(0, hitCount);
        mode1ResultPassed = passed;
        mode1RoundResultRequestId++;
        RequestSerialization();
    }

    #endregion

    #region Mode C Snapshot Accessors

    public int GetMode3WaveRoundNumber()
    {
        return mode3WaveRoundNumber;
    }

    public int GetMode3WaveIndex()
    {
        return mode3WaveIndex;
    }

    public int GetMode3WaveSeed()
    {
        return mode3WaveSeed;
    }

    public int GetMode3SnapshotRoundNumber()
    {
        return mode3SnapshotRoundNumber;
    }

    public int GetMode3SnapshotScore()
    {
        return mode3SnapshotScore;
    }

    public int GetMode3SnapshotDifficulty()
    {
        return mode3SnapshotDifficulty;
    }

    public bool GetMode3SnapshotActive()
    {
        return mode3RoundSnapshotActive;
    }

    #endregion

    #region Mode C Sync

    public void SyncMode3RoundSnapshot(int roundNumber, int score, int difficulty)
    {
        EnsureLocalOwner();

        mode3SnapshotRoundNumber = Mathf.Max(1, roundNumber);
        mode3SnapshotScore = Mathf.Max(0, score);
        mode3SnapshotDifficulty = Mathf.Max(0, difficulty);
        mode3RoundSnapshotActive = true;
        mode3RoundSnapshotRequestId++;
        RequestSerialization();
        SendCustomNetworkEvent(
            NetworkEventTarget.Others,
            nameof(NetworkApplyMode3RoundSnapshot),
            mode3SnapshotRoundNumber,
            mode3SnapshotScore,
            mode3SnapshotDifficulty,
            mode3RoundSnapshotRequestId);
    }

    [NetworkCallable]
    public void NetworkApplyMode3RoundSnapshot(int roundNumber, int score, int difficulty, int requestId)
    {
        if (requestId == handledMode3RoundSnapshotRequestId)
        {
            return;
        }

        mode3SnapshotRoundNumber = Mathf.Max(1, roundNumber);
        mode3SnapshotScore = Mathf.Max(0, score);
        mode3SnapshotDifficulty = Mathf.Max(0, difficulty);
        mode3RoundSnapshotActive = true;
        mode3RoundSnapshotRequestId = requestId;
        handledMode3RoundSnapshotRequestId = requestId;
        SendReceiverEvent(mode3RoundSnapshotReceiver, mode3RoundSnapshotEventName);
    }

    public void ClearMode3RoundSnapshot()
    {
        EnsureLocalOwner();

        mode3RoundSnapshotActive = false;
        RequestSerialization();
    }

    public void SyncMode3WaveStart(int roundNumber, int waveIndex, int seed)
    {
        EnsureLocalOwner();

        mode3WaveRoundNumber = Mathf.Max(1, roundNumber);
        mode3WaveIndex = Mathf.Max(0, waveIndex);
        mode3WaveSeed = seed;
        mode3WaveRequestId++;
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkApplyMode3WaveStart), mode3WaveRoundNumber, mode3WaveIndex, mode3WaveSeed, mode3WaveRequestId);
    }

    [NetworkCallable]
    public void NetworkApplyMode3WaveStart(int roundNumber, int waveIndex, int seed, int requestId)
    {
        if (requestId == handledMode3WaveRequestId)
        {
            return;
        }

        mode3WaveRoundNumber = Mathf.Max(1, roundNumber);
        mode3WaveIndex = Mathf.Max(0, waveIndex);
        mode3WaveSeed = seed;
        mode3WaveRequestId = requestId;
        handledMode3WaveRequestId = requestId;
        SendReceiverEvent(mode3WaveStartReceiver, mode3WaveStartEventName);
    }

    public int GetMode3HitClayPoolIndex()
    {
        return mode3HitClayPoolIndex;
    }

    public int GetMode3HitUsedShots()
    {
        return mode3HitUsedShots;
    }

    public void SyncMode3ClayHit(int clayPoolIndex, int usedShots)
    {
        if (clayPoolIndex < 0)
        {
            return;
        }

        EnsureLocalOwner();

        mode3HitClayPoolIndex = clayPoolIndex;
        mode3HitUsedShots = Mathf.Max(0, usedShots);
        mode3HitRequestId++;
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkApplyMode3ClayHit), mode3HitClayPoolIndex, mode3HitUsedShots, mode3HitRequestId);
    }

    [NetworkCallable]
    public void NetworkApplyMode3ClayHit(int clayPoolIndex, int usedShots, int requestId)
    {
        if (requestId == handledMode3HitRequestId)
        {
            return;
        }

        mode3HitClayPoolIndex = clayPoolIndex;
        mode3HitUsedShots = Mathf.Max(0, usedShots);
        mode3HitRequestId = requestId;
        handledMode3HitRequestId = requestId;
        SendReceiverEvent(mode3HitReceiver, mode3HitEventName);
    }

    public int GetMode3ShotUsedShots()
    {
        return mode3ShotUsedShots;
    }

    public void SyncMode3ShotMiss(int usedShots)
    {
        EnsureLocalOwner();

        mode3ShotUsedShots = Mathf.Max(0, usedShots);
        mode3ShotRequestId++;
        SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(NetworkApplyMode3ShotMiss), mode3ShotUsedShots, mode3ShotRequestId);
    }

    [NetworkCallable]
    public void NetworkApplyMode3ShotMiss(int usedShots, int requestId)
    {
        if (requestId == handledMode3ShotRequestId)
        {
            return;
        }

        mode3ShotUsedShots = Mathf.Max(0, usedShots);
        mode3ShotRequestId = requestId;
        handledMode3ShotRequestId = requestId;
        SendReceiverEvent(mode3ShotReceiver, mode3ShotEventName);
    }

    #endregion

    #region Deserialization And Flow Events

    public override void OnDeserialization()
    {
        ApplySyncedVisualState();
        NotifySyncedIndexChanged();
        ApplyMode1RoundPlan();
        ApplySyncedStartRequest();
        ApplyMode1ShotEvent();
        ApplyMode1HitEvent();
        ApplyMode1RoundResult();
        ApplyMode3RoundSnapshot();
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

    #endregion

    #region Synced Object And Receiver Helpers

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
        if (syncedIndexObjects == null || syncedIndexObjects.Length == 0)
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
        SendReceiverEvent(syncedIndexChangedReceiver, syncedIndexChangedEventName);
    }

    private void SendReceiverEvent(UdonSharpBehaviour receiver, string eventName)
    {
        if (receiver != null && !string.IsNullOrEmpty(eventName))
        {
            receiver.SendCustomEvent(eventName);
        }
    }

    private void ApplySyncedStartRequest()
    {
        if (syncedStartRequestId == handledStartRequestId || syncedStartIndex < 0)
        {
            return;
        }

        handledStartRequestId = syncedStartRequestId;
        SendReceiverEvent(syncedStartReceiver, syncedStartEventName);
    }

    private void ApplyMode1RoundPlan()
    {
        if (mode1RoundPlanRequestId == handledMode1RoundPlanRequestId || mode1RoundNumber <= 0)
        {
            return;
        }

        handledMode1RoundPlanRequestId = mode1RoundPlanRequestId;
        SendReceiverEvent(mode1RoundPlanReceiver, mode1RoundPlanEventName);
    }

    private void ApplyMode1HitEvent()
    {
        if (mode1HitRequestId == handledMode1HitRequestId || mode1HitPigeonPoolIndex < 0)
        {
            return;
        }

        handledMode1HitRequestId = mode1HitRequestId;
        SendReceiverEvent(mode1HitReceiver, mode1HitEventName);
    }

    private void ApplyMode1ShotEvent()
    {
        if (mode1ShotRequestId == handledMode1ShotRequestId)
        {
            return;
        }

        handledMode1ShotRequestId = mode1ShotRequestId;
        SendReceiverEvent(mode1ShotReceiver, mode1ShotEventName);
    }

    private void ApplyMode1RoundResult()
    {
        if (mode1RoundResultRequestId == handledMode1RoundResultRequestId || mode1ResultRoundNumber <= 0)
        {
            return;
        }

        handledMode1RoundResultRequestId = mode1RoundResultRequestId;
        SendReceiverEvent(mode1RoundResultReceiver, mode1RoundResultEventName);
    }

    private void ApplyMode3RoundSnapshot()
    {
        if (!mode3RoundSnapshotActive || mode3RoundSnapshotRequestId == handledMode3RoundSnapshotRequestId || mode3SnapshotRoundNumber <= 0)
        {
            return;
        }

        handledMode3RoundSnapshotRequestId = mode3RoundSnapshotRequestId;
        SendReceiverEvent(mode3RoundSnapshotReceiver, mode3RoundSnapshotEventName);
    }

    #endregion
}
