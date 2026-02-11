
using System;
using System.Collections;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;

[AddComponentMenu("PigeonHunt/Sound Manager")]
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SoundManager : UdonSharpBehaviour
{
    [Header("Round Audio")]
    public AudioSource endAudio;
    public AudioSource endLamoAudio;
    public AudioSource scoreCountAudio;
    [FormerlySerializedAs("nextAudio")]
    public AudioSource fullHitAudio;
    [Min(0f)]
    public float endAudioDuration = 0f;
    [Min(0f)]
    public float endLamoAudioDuration = 0f;
    [Min(0f)]
    public float scoreCountAudioDuration = 0f;
    [FormerlySerializedAs("nextAudioDuration")]
    [Min(0f)]
    public float fullHitAudioDuration = 0f;

    private const int SequenceNone = 0;
    private const int SequenceRoundClear = 1;
    private const int SequenceRoundFail = 2;

    private int activeSequence = SequenceNone;
    private int sequenceStep;
    private float sequenceTimer;
    private AudioSource sequenceSource;
    private bool sequenceFullHit;

    public void PlayGunShot(AudioSource source)
    {
        PlayOneShot(source);
    }

    public void PlayHitCount(AudioSource source)
    {
        PlayOneShot(source);
    }

    public void PlayFall(AudioSource source)
    {
        PlayOneShot(source);
    }

    public void PlayGroundImpact(AudioSource source)
    {
        PlayOneShot(source);
    }

    public void PlayFlight(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        if (!source.isPlaying)
        {
            source.Play();
        }
    }

    public void StopFlight(AudioSource source)
    {
        PigeonHunt.QychuiUtilities.SafeStop(source);
    }

    public void PlayRoundEnd()
    {
        PlayOneShot(endAudio);
    }

    public void PlayRoundEndLamo()
    {
        PlayOneShot(endLamoAudio);
    }

    public void PlayScoreCount()
    {
        PlayOneShot(scoreCountAudio);
    }

    public void PlayRoundNext()
    {
        PlayOneShot(fullHitAudio);
    }

    public void PlayRoundClearSequence()
    {
        StartSequence(SequenceRoundClear, false);
    }

    public void PlayRoundClearSequence(bool isFullHit)
    {
        StartSequence(SequenceRoundClear, isFullHit);
    }

    public void PlayRoundFailSequence()
    {
        StartSequence(SequenceRoundFail, false);
    }

    public void StopAll()
    {
        StopSequence();
        PigeonHunt.QychuiUtilities.SafeStop(endAudio);
        PigeonHunt.QychuiUtilities.SafeStop(endLamoAudio);
        PigeonHunt.QychuiUtilities.SafeStop(scoreCountAudio);
        PigeonHunt.QychuiUtilities.SafeStop(fullHitAudio);
    }

    public bool IsSequenceActive()
    {
        return activeSequence != SequenceNone;
    }

    private void Update()
    {
        TickSequence();
    }

    private void PlayOneShot(AudioSource source)
    {
        PigeonHunt.QychuiUtilities.SafePlay(source);
    }

    private void StartSequence(int sequence, bool fullHit)
    {
        StopSequence();
        activeSequence = sequence;
        sequenceStep = 0;
        sequenceFullHit = fullHit;
        StartSequenceStep();
    }

    private void StartSequenceStep()
    {
        sequenceSource = GetSequenceSource(activeSequence, sequenceStep);
        if (sequenceSource == null)
        {
            StopSequence();
            return;
        }

        sequenceTimer = GetSequenceDuration(activeSequence, sequenceStep, sequenceSource);
        PlayOneShot(sequenceSource);

        if (sequenceTimer <= 0f)
        {
            AdvanceSequence();
        }
    }

    private void TickSequence()
    {
        if (activeSequence == SequenceNone)
        {
            return;
        }

        if (sequenceTimer > 0f)
        {
            sequenceTimer -= Time.deltaTime;
            if (sequenceTimer > 0f)
            {
                return;
            }
        }

        AdvanceSequence();
    }

    private void AdvanceSequence()
    {
        if (sequenceSource != null)
        {
            PigeonHunt.QychuiUtilities.SafeStop(sequenceSource);
            sequenceSource = null;
        }

        sequenceStep++;
        if (IsSequenceComplete(activeSequence, sequenceStep))
        {
            StopSequence();
            return;
        }

        StartSequenceStep();
    }

    private void StopSequence()
    {
        if (sequenceSource != null)
        {
            PigeonHunt.QychuiUtilities.SafeStop(sequenceSource);
        }

        activeSequence = SequenceNone;
        sequenceStep = 0;
        sequenceTimer = 0f;
        sequenceSource = null;
        sequenceFullHit = false;
    }

    private AudioSource GetSequenceSource(int sequence, int step)
    {
        if (sequence == SequenceRoundClear)
        {
            return step == 0 ? scoreCountAudio : fullHitAudio;
        }

        if (sequence == SequenceRoundFail)
        {
            return step == 0 ? endAudio : endLamoAudio;
        }

        return null;
    }

    private float GetSequenceDuration(int sequence, int step, AudioSource source)
    {
        if (sequence == SequenceRoundClear)
        {
            return Mathf.Max(0f, step == 0 ? scoreCountAudioDuration : fullHitAudioDuration);
        }

        if (sequence == SequenceRoundFail)
        {
            return Mathf.Max(0f, step == 0 ? endAudioDuration : endLamoAudioDuration);
        }

        return 0f;
    }

    private bool IsSequenceComplete(int sequence, int step)
    {
        return sequence == SequenceRoundClear ? step >= (sequenceFullHit ? 2 : 1) :
               sequence == SequenceRoundFail ? step >= 2 :
               true;
    }
}
