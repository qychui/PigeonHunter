
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace PigeonHunt 
{
    public class UIController : UdonSharpBehaviour
    {
        [Header("GunBar UI")]
        public int weaponType = 0;
        public int bulletMaxCount = 3;
        public int bulletCurrentCount = 0;
        [Tooltip("不同武器类型的图标，索引与weaponType对应。")]
        public GameObject[] weaponIcons;
        [Tooltip("子弹显示用的图标，从左到右依次启用。")]
        public GameObject[] bulletObject;

        [Header("RoundLevel UI")]
        public int roundLevel = 1; //最大99轮
        [Tooltip("轮次两位数显示，每个元素对应一位，对应的材质会被写入_Index。")]
        public Material[] roundLevelDigitMaterials;

        [Header("InfoBar UI")]
        public int difficulty = 0;
        [Tooltip("按顺序配置五个难度的显示对象，当前难度对应的对象会被点亮。")]
        public GameObject[] difficultyLevelObjects;
        [Tooltip("十只鸽子击中显示的对象，true代表该只鸽子被击落。")]
        public GameObject[] pigeonHitIndicators;
        [Min(0f)]
        [Tooltip("回合结算时命中数动画的延迟间隔。")]
        public float pigeonHitCountAnimDelay = 0.3f;
        [Min(0f)]
        [Tooltip("全命中动画的闪烁间隔。")]
        public float fullHitBlinkDelay = 0.25f;
        [Min(1)]
        [Tooltip("全命中动画的闪烁轮数。每轮包含关闭和打开一次。")]
        public int fullHitBlinkCycles = 8;
        [Tooltip("回合结算时命中数动画音效。")]
        public AudioSource hitSfx;
        [Tooltip("全局音效管理器。")]
        public SoundManager soundManager;
        [Tooltip("十只鸽子的遮罩对象，对应出现的鸽子会闪烁。")]
        public GameObject[] pigeonMaskObjects;
        public float maskDelay = 0.4f; //遮罩闪烁的时间间隔，isActivite = true和false的交替间隔

        [Header("Score UI")]
        public int scoreCurrent = 0;
        public int maxScore = 999999;
        [Tooltip("Points awarded for each pigeon hit.")]
        public int scorePerHit = 500;
        [Tooltip("6位数的分数显示，每个元素对应一个材质实例。")]
        public Material[] scoreDigitMaterials;
        [Tooltip("ScoreShader的_Index属性名，驱动具体数字显示。")]
        public string digitShaderIndexProperty = "_Index";

        [Header("Fly Away UI")]
        public GameObject flyAwayBackgroundObject;
        public GameObject flyAwayShootMask;
        public GameObject flyAwayTextObject;

        [Header("GameOver UI")]
        public GameObject gameOverTextObject;

        [Header("Good UI")]
        public GameObject goodTextObject;

        [Header("Perfect UI")]
        public GameObject perfectBackgroundObject;
        public Material[] perfectScoreDigitMaterials;

        [Header("Round UI")]
        public GameObject roundBackgroundObject;
        public Material[] roundLevelTopDigitMaterials;
        [Min(0f)]
        public float roundDisplayTime = 2f;

        private int _pigeonQuota = 10;
        private int _hitCountThisRound;
        private int _primaryMaskIndex = -1;
        private int _secondaryMaskIndex = -1;
        private float _maskTimer;
        private bool _maskVisible;
        private bool _isMaskBlinking;
        private bool[] _pigeonHitStates;
        private bool _hitCountAnimActive;
        private float _hitCountAnimTimer;
        private int _hitCountAnimStep;
        private int _hitCountAnimTotalSteps;
        private int _hitCountAnimCount;
        private int[] _hitCountAnimIndices;
        private bool _fullHitAnimActive;
        private float _fullHitAnimTimer;
        private int _fullHitAnimStep;
        private int _fullHitAnimTotalSteps;
        private bool _roundDisplayActive;
        private float _roundDisplayTimer;
        private bool _perfectDisplayActive;
        private float _perfectDisplayDelay;
        private float _perfectDisplayTimer;
        private bool _perfectAwardPending;
        private int _perfectAwardValue;

        void Start()
        {
            RefreshAll();
        }

        private void Update()
        {
            TickFullHitAnimation();
            TickHitCountAnimation();
            TickMaskBlink();
            TickRoundDisplay();
            TickPerfectDisplay();
        }

        private void OnValidate()
        {
            SetDifficultyLevel(difficulty);   
        }

        public void SetGunState(int type, int currentBullets, int maxBullets)
        {
            weaponType = Mathf.Max(0, type);
            bulletMaxCount = Mathf.Max(0, maxBullets);
            bulletCurrentCount = Mathf.Clamp(currentBullets, 0, bulletMaxCount);
            UpdateWeaponIcons();
            UpdateBulletCount();
        }

        public void ConfigureBulletClip(int clipSize)
        {
            var capacity = bulletObject != null ? bulletObject.Length : clipSize;
            bulletMaxCount = Mathf.Clamp(clipSize, 0, capacity);
            if (bulletCurrentCount > bulletMaxCount)
            {
                bulletCurrentCount = bulletMaxCount;
            }

            UpdateBulletCount();
        }

        public void ResetBulletClipUsage()
        {
            if (bulletCurrentCount == 0)
            {
                UpdateBulletCount();
                return;
            }

            bulletCurrentCount = 0;
            UpdateBulletCount();
        }

        public void SetBulletUsage(int usedShots)
        {
            var clamped = Mathf.Clamp(usedShots, 0, bulletMaxCount);
            if (clamped == bulletCurrentCount)
            {
                return;
            }

            bulletCurrentCount = clamped;
            UpdateBulletCount();
        }

        public void ResetRoundUI(int roundNumber, int quota, int difficultyLevel)
        {
            SetRoundLevel(roundNumber);
            SetDifficultyLevel(difficultyLevel);
            SetPigeonQuota(quota);
            SetHitCount(0);
            ClearActivePigeonMask();
            ClearPigeonHitIndicators();
        }

        public void SetGameOverActive(bool active)
        {
            SetGameObjectActive(gameOverTextObject, active);
        }

        public void SetGoodActive(bool active)
        {
            SetGameObjectActive(goodTextObject, active);
        }

        public void SetPerfectActive(bool active)
        {
            SetGameObjectActive(perfectBackgroundObject, active);
        }

        public void ShowPerfectDisplay(float delaySeconds, float durationSeconds)
        {
            var score = UpdatePerfectScoreDigits();
            _perfectAwardValue = Mathf.Max(0, score);
            _perfectAwardPending = true;

            if (perfectBackgroundObject == null)
            {
                EndPerfectDisplay(false);
                return;
            }

            var duration = Mathf.Max(0f, durationSeconds);
            if (duration <= 0f)
            {
                EndPerfectDisplay(true);
                return;
            }

            _perfectDisplayDelay = Mathf.Max(0f, delaySeconds);
            _perfectDisplayTimer = duration;
            _perfectDisplayActive = true;

            if (_perfectDisplayDelay <= 0f)
            {
                SetPerfectActive(true);
            }
            else
            {
                SetPerfectActive(false);
            }
        }

        public void ClearPerfectDisplay()
        {
            EndPerfectDisplay(false);
        }

        public void ShowRoundDisplay()
        {
            UpdateRoundScoreDigits();
            if (roundBackgroundObject == null)
            {
                _roundDisplayActive = false;
                _roundDisplayTimer = 0f;
                return;
            }

            var duration = Mathf.Max(0f, roundDisplayTime);
            if (duration <= 0f)
            {
                SetGameObjectActive(roundBackgroundObject, false);
                _roundDisplayActive = false;
                _roundDisplayTimer = 0f;
                return;
            }

            SetGameObjectActive(roundBackgroundObject, true);
            _roundDisplayActive = true;
            _roundDisplayTimer = duration;
        }

        public void SetRoundLevel(int roundNumber)
        {
            roundLevel = Mathf.Clamp(roundNumber, 0, 99);
            UpdateRoundDigits();
            UpdateRoundScoreDigits();
        }

        public void SetScoreValue(int newScore)
        {
            scoreCurrent = Mathf.Clamp(newScore, 0, maxScore);
            UpdateScoreDigits();
        }

        public void AddScore(int amount)
        {
            if (amount == 0)
            {
                return;
            }

            var target = scoreCurrent + amount;
            SetScoreValue(target);
        }

        public void AddScoreForHit()
        {
            if (scorePerHit <= 0)
            {
                return;
            }

            AddScore(scorePerHit);
        }

        public void SetDifficultyLevel(int level)
        {
            var maxDifficulty = difficultyLevelObjects != null ? difficultyLevelObjects.Length : 0;
            if (maxDifficulty > 0)
            {
                difficulty = Mathf.Clamp(level, 0, maxDifficulty);
            }
            else
            {
                difficulty = Mathf.Max(0, level);
            }

            UpdateDifficultyObjects();
        }

        public void ChangeDifficulty(int newDifficulty)
        {
            SetDifficultyLevel(newDifficulty);
        }

        public void SetPigeonQuota(int quota)
        {
            if (quota < 0)
            {
                quota = 0;
            }

            var capacity = pigeonHitIndicators != null ? pigeonHitIndicators.Length : 0;
            if (capacity > 0)
            {
                quota = Mathf.Min(quota, capacity);
            }

            _pigeonQuota = quota;
            if (_hitCountThisRound > _pigeonQuota)
            {
                _hitCountThisRound = _pigeonQuota;
            }

            EnsureHitStateBuffer();
            UpdateHitIndicators();
        }

        public void SetHitCount(int hits)
        {
            _hitCountThisRound = Mathf.Clamp(hits, 0, Mathf.Max(_pigeonQuota, 0));
            UpdateHitIndicators();
        }

        public void ShowActivePigeonMask(int spawnIndex)
        {
            SetActiveMaskTargets(spawnIndex, -1);
        }

        public void ShowActivePigeonMaskPair(int firstIndex, int secondIndex)
        {
            SetActiveMaskTargets(firstIndex, secondIndex);
        }

        private void SetActiveMaskTargets(int firstIndex, int secondIndex)
        {
            if (pigeonMaskObjects == null || pigeonMaskObjects.Length == 0)
            {
                return;
            }

            firstIndex = ClampMaskIndex(firstIndex);
            secondIndex = ClampMaskIndex(secondIndex);

            if (firstIndex == _primaryMaskIndex && secondIndex == _secondaryMaskIndex)
            {
                return;
            }

            ClearCurrentMask();

            if (firstIndex < 0 && secondIndex < 0)
            {
                return;
            }

            _primaryMaskIndex = firstIndex;
            _secondaryMaskIndex = secondIndex;
            _maskVisible = true;
            _maskTimer = maskDelay;
            _isMaskBlinking = maskDelay > 0f;
            ApplyMaskVisibility(_maskVisible);
        }

        public void ClearActivePigeonMask()
        {
            ClearCurrentMask();
        }

        public void SetPigeonHitState(int index, bool wasHit)
        {
            EnsureHitStateBuffer();
            if (_pigeonHitStates == null || index < 0 || index >= _pigeonHitStates.Length)
            {
                return;
            }

            if (_pigeonQuota > 0 && index >= _pigeonQuota)
            {
                return;
            }

            _pigeonHitStates[index] = wasHit;
            UpdateHitIndicatorAt(index);
        }

        public void ClearPigeonHitIndicators()
        {
            _hitCountAnimActive = false;
            _fullHitAnimActive = false;
            EnsureHitStateBuffer();
            if (_pigeonHitStates != null)
            {
                for (int i = 0; i < _pigeonHitStates.Length; i++)
                {
                    _pigeonHitStates[i] = false;
                }
            }

            UpdateHitIndicators();
        }

        public bool BeginRoundEndHitAnimation(bool isFullHit)
        {
            if (isFullHit)
            {
                return BeginFullHitAnimation();
            }

            return BeginHitCountAnimation();
        }

        public bool BeginHitCountAnimation()
        {
            if (pigeonHitIndicators == null || pigeonHitIndicators.Length == 0)
            {
                return false;
            }

            _fullHitAnimActive = false;
            EnsureHitCountAnimBuffer();

            _hitCountAnimCount = 0;
            for (int i = 0; i < pigeonHitIndicators.Length; i++)
            {
                var indicator = pigeonHitIndicators[i];
                if (indicator != null && indicator.activeSelf)
                {
                    _hitCountAnimIndices[_hitCountAnimCount] = i;
                    _hitCountAnimCount++;
                }
            }

            if (_hitCountAnimCount <= 0)
            {
                _hitCountAnimActive = false;
                return false;
            }

            _hitCountAnimActive = true;
            _hitCountAnimStep = 0;
            _hitCountAnimTotalSteps = _hitCountAnimCount * 2;
            _hitCountAnimTimer = 0f;

            return true;
        }

        public bool IsHitCountAnimationActive()
        {
            return _hitCountAnimActive;
        }

        private void RefreshAll()
        {
            UpdateRoundDigits();
            UpdateRoundScoreDigits();
            UpdateScoreDigits();
            UpdateDifficultyObjects();
            ClearPigeonHitIndicators();
            SetGoodActive(false);
            ClearPerfectDisplay();
            UpdateWeaponIcons();
            UpdateBulletCount();
            ClearCurrentMask();
            SetGameObjectActive(roundBackgroundObject, false);
            _roundDisplayActive = false;
            _roundDisplayTimer = 0f;
        }

        private void UpdateRoundDigits()
        {
            WriteNumberToDigitMaterials(roundLevelDigitMaterials, Mathf.Clamp(roundLevel, 0, 99));
        }

        private void UpdateRoundScoreDigits()
        {
            WriteNumberToDigitMaterials(roundLevelTopDigitMaterials, Mathf.Clamp(roundLevel, 0, 99));
        }

        private int UpdatePerfectScoreDigits()
        {
            var score = GetPerfectScoreForRound(roundLevel);
            WriteNumberToDigitMaterials(perfectScoreDigitMaterials, score);
            return score;
        }

        private void UpdateScoreDigits()
        {
            var capacityMax = GetDigitCapacityMax(scoreDigitMaterials);
            if (capacityMax > 0)
            {
                scoreCurrent = Mathf.Clamp(scoreCurrent, 0, Mathf.Min(maxScore, capacityMax));
            }
            else
            {
                scoreCurrent = Mathf.Clamp(scoreCurrent, 0, maxScore);
            }

            WriteNumberToDigitMaterials(scoreDigitMaterials, scoreCurrent);
        }

        private void UpdateDifficultyObjects()
        {
            if (difficultyLevelObjects == null || difficultyLevelObjects.Length == 0)
            {
                return;
            }

            var activeCount = Mathf.Clamp(difficulty, 0, difficultyLevelObjects.Length);
            for (int i = 0; i < difficultyLevelObjects.Length; i++)
            {
                SetGameObjectActive(difficultyLevelObjects[i], i < activeCount);
            }
        }

        private void UpdateHitIndicators()
        {
            if (pigeonHitIndicators == null || pigeonHitIndicators.Length == 0)
            {
                return;
            }

            EnsureHitStateBuffer();

            for (int i = 0; i < pigeonHitIndicators.Length; i++)
            {
                UpdateHitIndicatorAt(i);
            }
        }

        private void UpdateHitIndicatorAt(int index)
        {
            if (pigeonHitIndicators == null || index < 0 || index >= pigeonHitIndicators.Length)
            {
                return;
            }

            var indicator = pigeonHitIndicators[index];
            if (indicator == null)
            {
                return;
            }

            if (_pigeonQuota > 0 && index >= _pigeonQuota)
            {
                SetGameObjectActive(indicator, false);
                return;
            }

            var shouldActive = _pigeonHitStates != null && index < _pigeonHitStates.Length && _pigeonHitStates[index];
            SetGameObjectActive(indicator, shouldActive);
        }

        private void TickHitCountAnimation()
        {
            if (_fullHitAnimActive)
            {
                return;
            }

            if (!_hitCountAnimActive)
            {
                return;
            }

            if (_hitCountAnimStep >= _hitCountAnimTotalSteps)
            {
                _hitCountAnimActive = false;
                return;
            }

            if (_hitCountAnimTimer > 0f)
            {
                _hitCountAnimTimer -= Time.deltaTime;
                if (_hitCountAnimTimer > 0f)
                {
                    return;
                }
            }

            var hitIndex = _hitCountAnimStep / 2;
            var disableSource = (_hitCountAnimStep % 2) == 0;

            if (disableSource)
            {
                SetHitIndicatorActiveRaw(_hitCountAnimIndices[hitIndex], false);
            }
            else
            {
                if (soundManager != null)
                {
                    soundManager.PlayHitCount(hitSfx);
                }
                else
                {
                    QychuiUtilities.SafePlay(hitSfx);
                }

                SetHitIndicatorActiveRaw(hitIndex, true);
            }

            _hitCountAnimStep++;

            if (_hitCountAnimStep >= _hitCountAnimTotalSteps)
            {
                _hitCountAnimActive = false;
                return;
            }

            _hitCountAnimTimer = Mathf.Max(0f, pigeonHitCountAnimDelay);
        }

        private bool BeginFullHitAnimation()
        {
            if (pigeonHitIndicators == null || pigeonHitIndicators.Length == 0)
            {
                return false;
            }

            var cycles = Mathf.Max(0, fullHitBlinkCycles);
            if (cycles <= 0)
            {
                _fullHitAnimActive = false;
                return false;
            }

            _fullHitAnimActive = true;
            _hitCountAnimActive = false;
            _fullHitAnimStep = 0;
            _fullHitAnimTotalSteps = cycles * 2;
            _fullHitAnimTimer = 0f;

            return true;
        }

        private void TickFullHitAnimation()
        {
            if (!_fullHitAnimActive)
            {
                return;
            }

            if (_fullHitAnimStep >= _fullHitAnimTotalSteps)
            {
                _fullHitAnimActive = false;
                UpdateHitIndicators();
                return;
            }

            if (_fullHitAnimTimer > 0f)
            {
                _fullHitAnimTimer -= Time.deltaTime;
                if (_fullHitAnimTimer > 0f)
                {
                    return;
                }
            }

            var showIndicators = (_fullHitAnimStep % 2) == 1;
            SetAllHitIndicatorsActive(showIndicators);

            _fullHitAnimStep++;

            if (_fullHitAnimStep >= _fullHitAnimTotalSteps)
            {
                _fullHitAnimActive = false;
                UpdateHitIndicators();
                return;
            }

            _fullHitAnimTimer = Mathf.Max(0f, fullHitBlinkDelay);
        }

        private void TickMaskBlink()
        {
            if (!_isMaskBlinking || maskDelay <= 0f)
            {
                return;
            }

            if (_primaryMaskIndex < 0 && _secondaryMaskIndex < 0)
            {
                return;
            }

            _maskTimer -= Time.deltaTime;
            if (_maskTimer > 0f)
            {
                return;
            }

            _maskTimer = maskDelay;
            _maskVisible = !_maskVisible;
            ApplyMaskVisibility(_maskVisible);
        }

        private void TickRoundDisplay()
        {
            if (!_roundDisplayActive)
            {
                return;
            }

            if (roundBackgroundObject == null)
            {
                _roundDisplayActive = false;
                _roundDisplayTimer = 0f;
                return;
            }

            if (_roundDisplayTimer > 0f)
            {
                _roundDisplayTimer -= Time.deltaTime;
                if (_roundDisplayTimer > 0f)
                {
                    return;
                }
            }

            SetGameObjectActive(roundBackgroundObject, false);
            _roundDisplayActive = false;
            _roundDisplayTimer = 0f;
        }

        private void TickPerfectDisplay()
        {
            if (!_perfectDisplayActive)
            {
                return;
            }

            if (perfectBackgroundObject == null)
            {
                EndPerfectDisplay(false);
                return;
            }

            if (_perfectDisplayDelay > 0f)
            {
                _perfectDisplayDelay -= Time.deltaTime;
                if (_perfectDisplayDelay > 0f)
                {
                    return;
                }

                _perfectDisplayDelay = 0f;
                SetPerfectActive(true);
            }

            if (_perfectDisplayTimer > 0f)
            {
                _perfectDisplayTimer -= Time.deltaTime;
                if (_perfectDisplayTimer > 0f)
                {
                    return;
                }
            }

            EndPerfectDisplay(true);
        }

        private void EndPerfectDisplay(bool awardScore)
        {
            var shouldAward = awardScore && _perfectAwardPending;
            SetPerfectActive(false);
            _perfectDisplayActive = false;
            _perfectDisplayDelay = 0f;
            _perfectDisplayTimer = 0f;
            _perfectAwardPending = false;
            if (shouldAward && _perfectAwardValue != 0)
            {
                AddScore(_perfectAwardValue);
            }
            _perfectAwardValue = 0;
        }

        private void ClearCurrentMask()
        {
            if (pigeonMaskObjects != null && pigeonMaskObjects.Length > 0)
            {
                if (_primaryMaskIndex >= 0 && _primaryMaskIndex < pigeonMaskObjects.Length)
                {
                    SetMaskState(_primaryMaskIndex, false);
                }

                if (_secondaryMaskIndex >= 0 && _secondaryMaskIndex < pigeonMaskObjects.Length && _secondaryMaskIndex != _primaryMaskIndex)
                {
                    SetMaskState(_secondaryMaskIndex, false);
                }

                if (_primaryMaskIndex < 0 && _secondaryMaskIndex < 0)
                {
                    for (int i = 0; i < pigeonMaskObjects.Length; i++)
                    {
                        SetMaskState(i, false);
                    }
                }
            }

            _primaryMaskIndex = -1;
            _secondaryMaskIndex = -1;
            _maskVisible = false;
            _maskTimer = 0f;
            _isMaskBlinking = false;
        }

        private void EnsureHitStateBuffer()
        {
            if (pigeonHitIndicators == null || pigeonHitIndicators.Length == 0)
            {
                _pigeonHitStates = null;
                return;
            }

            var desired = pigeonHitIndicators.Length;
            if (_pigeonHitStates == null || _pigeonHitStates.Length != desired)
            {
                _pigeonHitStates = new bool[desired];
            }
        }

        private void EnsureHitCountAnimBuffer()
        {
            var desired = pigeonHitIndicators != null ? pigeonHitIndicators.Length : 0;
            if (desired <= 0)
            {
                _hitCountAnimIndices = null;
                return;
            }

            if (_hitCountAnimIndices == null || _hitCountAnimIndices.Length != desired)
            {
                _hitCountAnimIndices = new int[desired];
            }
        }

        private void SetHitIndicatorActiveRaw(int index, bool active)
        {
            if (pigeonHitIndicators == null || index < 0 || index >= pigeonHitIndicators.Length)
            {
                return;
            }

            var indicator = pigeonHitIndicators[index];
            if (indicator != null)
            {
                SetGameObjectActive(indicator, active);
            }

            EnsureHitStateBuffer();
            if (_pigeonHitStates != null && index < _pigeonHitStates.Length)
            {
                _pigeonHitStates[index] = active;
            }
        }

        private void SetAllHitIndicatorsActive(bool active)
        {
            if (pigeonHitIndicators == null || pigeonHitIndicators.Length == 0)
            {
                return;
            }

            var count = pigeonHitIndicators.Length;
            if (_pigeonQuota > 0)
            {
                count = Mathf.Min(count, _pigeonQuota);
            }

            for (int i = 0; i < count; i++)
            {
                SetGameObjectActive(pigeonHitIndicators[i], active);
            }
        }

        private int ClampMaskIndex(int index)
        {
            if (pigeonMaskObjects == null || pigeonMaskObjects.Length == 0)
            {
                return -1;
            }

            if (index < 0)
            {
                return -1;
            }

            if (_pigeonQuota > 0)
            {
                index = Mathf.Min(index, _pigeonQuota - 1);
            }

            index = Mathf.Clamp(index, 0, pigeonMaskObjects.Length - 1);
            return index;
        }

        private void ApplyMaskVisibility(bool active)
        {
            if (pigeonMaskObjects == null || pigeonMaskObjects.Length == 0)
            {
                return;
            }

            if (_primaryMaskIndex >= 0)
            {
                SetMaskState(_primaryMaskIndex, active);
            }

            if (_secondaryMaskIndex >= 0 && _secondaryMaskIndex != _primaryMaskIndex)
            {
                SetMaskState(_secondaryMaskIndex, active);
            }
        }

        private void SetMaskState(int index, bool active)
        {
            if (pigeonMaskObjects == null || index < 0 || index >= pigeonMaskObjects.Length)
            {
                return;
            }

            SetGameObjectActive(pigeonMaskObjects[index], active);
        }

        private void WriteNumberToDigitMaterials(Material[] digitMaterials, int value)
        {
            if (digitMaterials == null || digitMaterials.Length == 0)
            {
                return;
            }

            var digits = digitMaterials.Length;
            var maxValue = GetDigitCapacityMax(digitMaterials);
            if (maxValue > 0)
            {
                value = Mathf.Clamp(value, 0, maxValue);
            }

            var formatted = value.ToString("D" + digits);
            if (formatted.Length < digits)
            {
                formatted = formatted.PadLeft(digits, '0');
            }

            for (int i = 0; i < digits && i < formatted.Length; i++)
            {
                var digitValue = formatted[i] - '0';
                if (digitValue < 0 || digitValue > 9)
                {
                    digitValue = 0;
                }

                SetDigitMaterialValue(digitMaterials[i], digitValue);
            }
        }

        private int GetDigitCapacityMax(Material[] digitMaterials)
        {
            if (digitMaterials == null)
            {
                return 0;
            }

            var digits = digitMaterials.Length;
            if (digits <= 0)
            {
                return 0;
            }

            var max = 0;
            for (int i = 0; i < digits; i++)
            {
                max = (max * 10) + 9;
            }

            return max;
        }

        private void SetGameObjectActive(GameObject target, bool shouldBeActive)
        {
            if (target != null && target.activeSelf != shouldBeActive)
            {
                target.SetActive(shouldBeActive);
            }
        }

        private void SetDigitMaterialValue(Material material, int digit)
        {
            if (material == null || string.IsNullOrEmpty(digitShaderIndexProperty))
            {
                return;
            }

            material.SetFloat(digitShaderIndexProperty, digit);
        }

        private int GetPerfectScoreForRound(int roundNumber)
        {
            if (roundNumber <= 10)
            {
                return 10000;
            }

            var steps = ((roundNumber - 11) / 5) + 1;
            return 10000 + Mathf.Max(0, steps) * 5000;
        }

        private void UpdateWeaponIcons()
        {
            if (weaponIcons == null || weaponIcons.Length == 0)
            {
                return;
            }

            var activeIndex = Mathf.Clamp(weaponType, 0, weaponIcons.Length - 1);
            for (int i = 0; i < weaponIcons.Length; i++)
            {
                SetGameObjectActive(weaponIcons[i], i == activeIndex);
            }
        }

        private void UpdateBulletCount()
        {
            if (bulletObject == null || bulletObject.Length == 0)
            {
                return;
            }

            var capacity = bulletObject.Length;
            var allowedMax = Mathf.Clamp(bulletMaxCount, 0, capacity);
            for (int i = 0; i < capacity; i++)
            {
                var mask = bulletObject[i];
                if (mask == null)
                {
                    continue;
                }

                if (i >= allowedMax)
                {
                    SetGameObjectActive(mask, false);
                    continue;
                }

                var shouldActive = i < bulletCurrentCount;
                SetGameObjectActive(mask, shouldActive);
            }
        }
    }
}
