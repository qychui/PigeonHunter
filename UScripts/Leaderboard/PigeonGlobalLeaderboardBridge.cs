using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

namespace PigeonHunt
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PigeonGlobalLeaderboardBridge : UdonSharpBehaviour
    {
        private const string SubmitPath = "/api/v1/duck/runs/submit";
        private const string GameRootName = "PigeonHunter";
        private const string AlternateGameRootName = "RetroTV DuckHunt";
        private const int UploadTimeoutMs = 20000;

        #region Configuration

        [Header("Upload UI")]
        public TMP_InputField generatedUrlField;
        public VRCUrlInputField pastedUrlField;
        public TMP_Text statusText;

        [Header("Record Source")]
        public PigeonLeaderboardPersistence persistence;
        public PigeonRunRecordController runRecordController;
        public PigeonLeaderboardModeController modeController;

        [Header("Endpoint")]
        public string submitBaseUrl = "https://api.nhui.top/api/v1/duck/runs/submit";

        [Header("Leaderboards")]
        public string modeALeaderboardId = "duck_single_v1";
        public string modeBLeaderboardId = "duck_pair_v1";
        public string modeCLeaderboardId = "duck_range_v1";

        [Header("Signature")]
        public UdonHashLib hashLibrary;
        public string signingKey;

        #endregion

        #region Runtime

        private bool uploadInFlight;
        private int uploadStartedAtMs;
        private int uploadingModeId;

        private void Start()
        {
            ResolveRecordSources();
        }

        #endregion

        #region Upload Flow

        public void GenerateUploadUrl()
        {
            ResolveRecordSources();

            if (generatedUrlField == null)
            {
                SetStatus("LINK FIELD MISSING");
                return;
            }

            if (persistence == null || modeController == null)
            {
                SetStatus("SCORE SOURCE MISSING");
                return;
            }

            if (hashLibrary == null || string.IsNullOrEmpty(signingKey))
            {
                SetStatus("SIGNATURE NOT CONFIGURED");
                return;
            }

            if (!persistence.IsLocalDataReady())
            {
                SetStatus("PLAYER DATA LOADING");
                return;
            }

            var modeId = modeController.GetCurrentGameMode();
            var leaderboardId = GetLeaderboardId(modeId);
            if (string.IsNullOrEmpty(leaderboardId))
            {
                SetStatus("MODE NOT SUPPORTED");
                return;
            }

            var hasLiveResult = runRecordController != null
                                && runRecordController.GetRunState() == PigeonRunRecordController.RunStateFinalized
                                && runRecordController.GetInvalidReason() == PigeonRunRecordController.InvalidReasonNone
                                && runRecordController.HasLiveResult()
                                && runRecordController.GetLiveModeId() == modeId;
            if (!hasLiveResult && !persistence.HasLocalBestRecord(modeId))
            {
                SetStatus("NO LOCAL SCORE");
                return;
            }

            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                SetStatus("LOCAL PLAYER MISSING");
                return;
            }

            var runnerId = persistence.GetLocalRunnerId();
            if (string.IsNullOrEmpty(runnerId) || runnerId.Length != 32)
            {
                SetStatus("PLAYER ID NOT READY");
                return;
            }

            var score = hasLiveResult
                ? runRecordController.GetLiveFinalScore()
                : persistence.GetLocalBestScore(modeId);
            var reachedRound = hasLiveResult
                ? runRecordController.GetLiveReachedRound()
                : persistence.GetLocalBestRound(modeId);
            var totalHits = hasLiveResult
                ? runRecordController.GetLiveTotalHits()
                : persistence.GetLocalBestTotalHits(modeId);
            var rulesetVersion = hasLiveResult
                ? runRecordController.GetLiveRulesetVersion()
                : persistence.GetLocalBestRulesetVersion(modeId);
            var buildVersion = hasLiveResult
                ? runRecordController.GetLiveBuildVersion()
                : persistence.GetLocalBestBuildVersion(modeId);

            if (score < 0 || reachedRound < 1 || totalHits < 0
                || rulesetVersion < 1 || buildVersion < 1)
            {
                SetStatus("SCORE DATA INVALID");
                return;
            }

            var submissionSource = hasLiveResult ? "live" : "saved_best";
            var noncePrefix = runnerId + "|" + leaderboardId + "|" + rulesetVersion + "|"
                              + buildVersion + "|";
            var noncePayload = hasLiveResult
                ? noncePrefix + "live|" + runRecordController.GetLiveRunId() + "|"
                  + score + "|" + reachedRound + "|" + totalHits
                : noncePrefix + "saved_best|" + Mathf.Max(0, persistence.GetLocalBestDateYmd(modeId)) + "|"
                  + score + "|" + reachedRound + "|" + totalHits;

            var canonicalQuery = "schemaVersion=1"
                                 + "&leaderboardId=" + leaderboardId
                                 + "&rulesetVersion=" + rulesetVersion
                                 + "&buildVersion=" + buildVersion
                                 + "&runnerId=" + runnerId
                                 + "&displayName=" + PercentEncode(player.displayName)
                                 + "&submissionSource=" + submissionSource
                                 + "&score=" + score
                                 + "&reachedRound=" + reachedRound
                                 + "&totalHits=" + totalHits
                                 + "&runNonce=" + BuildRunNonce(noncePayload)
                                 + "&eligibilityFlags=0";
            var signature = BuildHmacSha256("GET\n" + SubmitPath + "\n" + canonicalQuery);
            generatedUrlField.text = submitBaseUrl + "?" + canonicalQuery + "&sig=" + signature;
            SetStatus(GetModeLabel(modeId) + (hasLiveResult ? " LIVE LINK READY" : " SAVED LINK READY"));
        }

        public void SubmitPastedUrl()
        {
            ReleaseExpiredUpload();
            if (uploadInFlight)
            {
                SetStatus(GetModeLabel(uploadingModeId) + " UPLOAD IN PROGRESS");
                return;
            }

            if (pastedUrlField == null)
            {
                SetStatus("PASTE FIELD MISSING");
                return;
            }

            var url = pastedUrlField.GetUrl();
            if (VRCUrl.IsNullOrEmpty(url))
            {
                SetStatus("PASTE A LINK FIRST");
                return;
            }

            if (modeController == null)
            {
                SetStatus("MODE SOURCE MISSING");
                return;
            }

            var modeId = modeController.GetCurrentGameMode();
            var leaderboardId = GetLeaderboardId(modeId);
            var pastedUrl = url.Get();
            if (string.IsNullOrEmpty(leaderboardId)
                || string.IsNullOrEmpty(pastedUrl)
                || !pastedUrl.Contains("leaderboardId=" + leaderboardId))
            {
                SetStatus("PASTED LINK DOES NOT MATCH " + GetModeLabel(modeId));
                return;
            }

            uploadInFlight = true;
            uploadStartedAtMs = Networking.GetServerTimeInMilliseconds();
            uploadingModeId = modeId;
            SetStatus(GetModeLabel(modeId) + " UPLOADING...");
            SendCustomEventDelayedSeconds(nameof(CheckUploadTimeout), UploadTimeoutMs / 1000f);
            VRCStringDownloader.LoadUrl(url, this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            var modeLabel = GetModeLabel(uploadingModeId);
            ClearUploadState();
            SetStatus(modeLabel + " UPLOAD SUCCESS");

            DataToken root;
            if (!VRCJson.TryDeserializeFromJson(result.Result, out root)
                || root.TokenType != TokenType.DataDictionary)
            {
                SetStatus(modeLabel + " INVALID SERVER RESPONSE");
                return;
            }

            var data = root.DataDictionary;
            DataToken okToken;
            if (!data.TryGetValue("ok", out okToken)
                || okToken.TokenType != TokenType.Boolean
                || !okToken.Boolean)
            {
                SetStatus(modeLabel + " UPLOAD REJECTED");
                return;
            }

            DataToken duplicateToken;
            var duplicate = data.TryGetValue("duplicate", out duplicateToken)
                            && duplicateToken.TokenType == TokenType.Boolean
                            && duplicateToken.Boolean;
            DataToken rankToken;
            var rankSuffix = data.TryGetValue("rank", out rankToken)
                             && rankToken.TokenType == TokenType.Double
                ? " - RANK " + Mathf.RoundToInt((float)rankToken.Double)
                : "";
            SetStatus(modeLabel + " " + (duplicate ? "ALREADY UPLOADED" : "UPLOAD SUCCESS") + rankSuffix);
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            var modeLabel = GetModeLabel(uploadingModeId);
            ClearUploadState();
            SetStatus(modeLabel + " UPLOAD FAILED (" + result.ErrorCode + ")");
        }

        public void CheckUploadTimeout()
        {
            if (!uploadInFlight || !IsUploadExpired())
            {
                return;
            }

            var modeLabel = GetModeLabel(uploadingModeId);
            ClearUploadState();
            SetStatus(GetModeTimeoutStatus(modeLabel));
        }

        #endregion

        #region Record Source And Mode Mapping

        public void RegisterRecordSource(
            PigeonLeaderboardPersistence sourcePersistence,
            PigeonRunRecordController sourceRunRecordController)
        {
            if (sourcePersistence != null)
            {
                persistence = sourcePersistence;
            }

            if (sourceRunRecordController != null)
            {
                runRecordController = sourceRunRecordController;
            }
        }

        private void ResolveRecordSources()
        {
            if (persistence != null && runRecordController != null)
            {
                return;
            }

            ResolveRecordSourcesFromRoot(GameObject.Find(GameRootName));
            if (persistence != null && runRecordController != null)
            {
                return;
            }

            ResolveRecordSourcesFromRoot(GameObject.Find(AlternateGameRootName));
        }

        private void ResolveRecordSourcesFromRoot(GameObject gameRoot)
        {
            if (gameRoot == null)
            {
                return;
            }

            if (persistence == null)
            {
                persistence = gameRoot.GetComponentInChildren<PigeonLeaderboardPersistence>(true);
            }

            if (runRecordController == null)
            {
                runRecordController = gameRoot.GetComponentInChildren<PigeonRunRecordController>(true);
            }
        }

        private string GetLeaderboardId(int modeId)
        {
            if (modeId == PigeonRunRecordController.ModeA)
            {
                return modeALeaderboardId;
            }

            if (modeId == PigeonRunRecordController.ModeB)
            {
                return modeBLeaderboardId;
            }

            return modeId == PigeonRunRecordController.ModeC ? modeCLeaderboardId : "";
        }

        private string GetModeLabel(int modeId)
        {
            if (modeId == PigeonRunRecordController.ModeA)
            {
                return "MODE A";
            }

            if (modeId == PigeonRunRecordController.ModeB)
            {
                return "MODE B";
            }

            return modeId == PigeonRunRecordController.ModeC ? "MODE C" : "MODE ?";
        }

        private string GetModeTimeoutStatus(string modeLabel)
        {
            if (modeLabel == "MODE A")
            {
                return "Mode A Time Out";
            }

            if (modeLabel == "MODE B")
            {
                return "Mode B Time Out";
            }

            return modeLabel == "MODE C" ? "Mode C Time Out" : "Time Out";
        }

        private void ReleaseExpiredUpload()
        {
            if (uploadInFlight && IsUploadExpired())
            {
                ClearUploadState();
            }
        }

        private bool IsUploadExpired()
        {
            return Networking.GetServerTimeInMilliseconds() - uploadStartedAtMs >= UploadTimeoutMs;
        }

        private void ClearUploadState()
        {
            uploadInFlight = false;
            uploadStartedAtMs = 0;
            uploadingModeId = 0;
        }

        #endregion

        #region Signature And Encoding

        private string BuildRunNonce(string value)
        {
            var nonce = "";
            for (var block = 0; block < 8; block++)
            {
                var hash = 5381 + block * 1009;
                for (var i = 0; i < value.Length; i++)
                {
                    hash = ((hash << 5) + hash) ^ ((int)value[i] + block * 17);
                }

                nonce += (hash & int.MaxValue).ToString("x8");
            }

            return nonce;
        }

        private string BuildHmacSha256(string message)
        {
            var keyBytes = AsciiBytes(signingKey);
            if (keyBytes.Length > 64)
            {
                keyBytes = HexBytes(hashLibrary.SHA256_Bytes(keyBytes));
            }

            var inner = new byte[64 + message.Length];
            var outer = new byte[96];
            for (var i = 0; i < 64; i++)
            {
                var keyByte = i < keyBytes.Length ? keyBytes[i] : (byte)0;
                inner[i] = (byte)(keyByte ^ 0x36);
                outer[i] = (byte)(keyByte ^ 0x5C);
            }

            for (var i = 0; i < message.Length; i++)
            {
                inner[64 + i] = (byte)message[i];
            }

            var innerHash = HexBytes(hashLibrary.SHA256_Bytes(inner));
            for (var i = 0; i < innerHash.Length; i++)
            {
                outer[64 + i] = innerHash[i];
            }

            return hashLibrary.SHA256_Bytes(outer);
        }

        private byte[] AsciiBytes(string value)
        {
            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                bytes[i] = (byte)value[i];
            }

            return bytes;
        }

        private byte[] HexBytes(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(HexValue(value[i * 2]) * 16 + HexValue(value[i * 2 + 1]));
            }

            return bytes;
        }

        private int HexValue(char value)
        {
            return value >= '0' && value <= '9' ? value - '0' : value - 'a' + 10;
        }

        private string PercentEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "PLAYER";
            }

            var encoded = "";
            for (var i = 0; i < value.Length; i++)
            {
                var codePoint = (int)value[i];
                if (codePoint >= 0xD800 && codePoint <= 0xDBFF && i + 1 < value.Length)
                {
                    var low = (int)value[i + 1];
                    if (low >= 0xDC00 && low <= 0xDFFF)
                    {
                        codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + low - 0xDC00;
                        i++;
                    }
                }

                if (codePoint < 0x80)
                {
                    if (IsUnreserved(codePoint))
                    {
                        encoded += (char)codePoint;
                    }
                    else
                    {
                        encoded += EncodeByte(codePoint);
                    }
                }
                else if (codePoint < 0x800)
                {
                    encoded += EncodeByte(0xC0 | codePoint >> 6);
                    encoded += EncodeByte(0x80 | codePoint & 0x3F);
                }
                else if (codePoint < 0x10000)
                {
                    encoded += EncodeByte(0xE0 | codePoint >> 12);
                    encoded += EncodeByte(0x80 | codePoint >> 6 & 0x3F);
                    encoded += EncodeByte(0x80 | codePoint & 0x3F);
                }
                else
                {
                    encoded += EncodeByte(0xF0 | codePoint >> 18);
                    encoded += EncodeByte(0x80 | codePoint >> 12 & 0x3F);
                    encoded += EncodeByte(0x80 | codePoint >> 6 & 0x3F);
                    encoded += EncodeByte(0x80 | codePoint & 0x3F);
                }
            }

            return encoded;
        }

        private bool IsUnreserved(int value)
        {
            return value >= 'A' && value <= 'Z'
                   || value >= 'a' && value <= 'z'
                   || value >= '0' && value <= '9'
                   || value == '-' || value == '.' || value == '_' || value == '~';
        }

        private string EncodeByte(int value)
        {
            return "%" + (value & 0xFF).ToString("X2");
        }

        #endregion

        private void SetStatus(string value)
        {
            if (statusText != null && statusText.text != value)
            {
                statusText.text = value;
            }
        }
    }
}
