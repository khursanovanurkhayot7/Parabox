using System;
using System.Collections;
using System.Collections.Generic;
using Luxodd.Game.Scripts.Game.Leaderboard;
using Luxodd.Game.Scripts.Network;
using Luxodd.Game.Scripts.Network.CommandHandler;
using Luxodd.Game.Scripts.Network.Payloads;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace Parabox
{
    // One persistent integration point for Luxodd networking. The game remains fully playable if
    // the platform is unavailable; reports/state are queued locally until a connection exists.
    [DefaultExecutionOrder(-4000)]
    public class LuxoddGameService : MonoBehaviour
    {
        const string SettingsResource = "LuxoddParaboxSettings";
        const string ProfileKey = "Parabox.Luxodd.Profile";
        const string LevelKey = "Parabox.Level";
        const string TutorialKey = "Parabox.Tutorial.Seen";
        const string OriginFilmKey = "Parabox.OriginFilm.Seen";
        const string BestPrefix = "Parabox.Best.";
        const string SessionFingerprintKey = "Parabox.Luxodd.SessionFingerprint";
        const string SessionUpdatedKey = "Parabox.Luxodd.SessionUpdatedAt";
        const string SessionContinuedPrefix = "Parabox.Luxodd.Continued.";
        const int LevelCount = 50;

        public static LuxoddGameService Instance { get; private set; }
        public static bool IsOnline => Instance != null && Instance.online;
        public static bool IsTransactionPopupOpen => Instance != null && Instance.transactionPopupOpen;
        public static string PlayerName => Instance != null ? Instance.playerName : string.Empty;
        public static bool HasContinuedLevel(int zeroBasedLevel)
            => PlayerPrefs.HasKey(SessionContinuedPrefix + Mathf.Max(0, zeroBasedLevel));
        public static event Action ProgressLoaded;

        static readonly HashSet<LossLeaderboard> RegisteredLeaderboards = new HashSet<LossLeaderboard>();

        WebSocketService socket;
        WebSocketCommandHandler commands;
        HealthStatusCheckService health;
        SessionFlowController sessionFlow;

        readonly List<PendingLevelEvent> pendingLevelEvents = new List<PendingLevelEvent>();
        readonly List<LeaderboardEntry> remoteLeaderboard = new List<LeaderboardEntry>(LossLeaderboard.Capacity);
        readonly HashSet<int> continuedLevels = new HashSet<int>();
        bool online;
        bool onlineInitialized;
        bool profileReady;
        bool cloudReady;
        bool cloudRequestSucceeded;
        bool cloudResolved;
        bool identityChanged;
        bool savePending;
        bool saveInFlight;
        bool levelRetryScheduled;
        bool leaderboardLoaded;
        string playerName;
        object cloudRaw;
        LossTransaction pendingLossTransaction;
        bool transactionPopupOpen;
        string activeSessionFingerprint;
        bool platformSessionChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
#if UNITY_EDITOR
            // The cabinet services are WebGL host services. Starting the complete networking
            // prefab in the Editor creates reconnect/health-check work that can keep the Editor
            // main thread busy enough to make the Game view and Play/Stop controls unresponsive.
            // LuxoddArcadeAdapter has its own bootstrap, so real arcade input remains testable.
            // A 4K Game view on a high-refresh display otherwise renders at the monitor rate; keep
            // local testing light enough that Unity's toolbar remains responsive.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Debug.Log("[Luxodd] Editor local-test mode: server runtime disabled; arcade input remains enabled.");
#else
            if (Instance != null) return;

            var settings = Resources.Load<LuxoddParaboxSettings>(SettingsResource);
            if (settings == null || settings.pluginPrefab == null)
            {
                Debug.LogError("[Luxodd] Missing Resources/LuxoddParaboxSettings or plugin prefab reference.");
                return;
            }

            var root = Instantiate(settings.pluginPrefab);
            root.name = "LuxoddRuntime";
            DontDestroyOnLoad(root);
            if (root.GetComponent<LuxoddArcadeAdapter>() == null)
                root.AddComponent<LuxoddArcadeAdapter>();
            if (root.GetComponent<LuxoddGameService>() == null)
                root.AddComponent<LuxoddGameService>();
#endif
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            socket = GetComponentInChildren<WebSocketService>(true);
            commands = GetComponentInChildren<WebSocketCommandHandler>(true);
            health = GetComponentInChildren<HealthStatusCheckService>(true);
            sessionFlow = GetComponentInChildren<SessionFlowController>(true);
        }

        void Start()
        {
            if (socket == null || commands == null || sessionFlow == null)
            {
                Debug.LogError("[Luxodd] Runtime prefab is missing a required network component; local play remains enabled.");
                return;
            }

            socket.ConnectedToServerEvent.AddListener(OnConnectionChanged);
            sessionFlow.ActivateProcess(OnConnected, OnConnectionFailed);
            StartCoroutine(ConnectionWatchdog());
        }

        void OnDestroy()
        {
            if (socket != null) socket.ConnectedToServerEvent.RemoveListener(OnConnectionChanged);
            if (health != null) health.Deactivate();
            if (Instance == this) Instance = null;
        }

        IEnumerator ConnectionWatchdog()
        {
            float until = Time.realtimeSinceStartup + 14f;
            while (!online && Time.realtimeSinceStartup < until) yield return null;
            if (!online)
                Debug.LogWarning("[Luxodd] Server connection is unavailable; continuing with local progress and placeholder leaderboard.");
        }

        void OnConnectionChanged(bool connected)
        {
            online = connected;
            if (connected)
            {
                OnConnected();
                return;
            }

            // If the host disappears while its Continue popup owns the loss screen, no callback
            // can arrive and the player would be trapped permanently. Preserve the live attempt
            // through the same local recovery used when Luxodd is unavailable at request time.
            ContinuePendingLossLocally("the host connection closed during Continue");
        }

        void OnConnected()
        {
            online = true;
            InitializeSessionIdentity();
            if (onlineInitialized)
            {
                if (platformSessionChanged) RequestIdentityAndCloudState();
                // Reconnects must release work queued during the outage. Identity/state do not
                // need another merge, but analytics, the visible board and any pending save do.
                FlushLevelEvents();
                RequestLeaderboard();
                UploadCloudState();
                TryStartLossTransaction();
                return;
            }
            onlineInitialized = true;
            Debug.Log("[Luxodd] Connected to server.");

            if (health != null) health.Activate();
            RequestIdentityAndCloudState();
            RequestLeaderboard();
            FlushLevelEvents();
            TryStartLossTransaction();
        }

        // One paid Luxodd session owns one run. Unity reloads keep that run; a different token
        // starts clean. Store only a one-way token fingerprint, never the raw credential.
        void InitializeSessionIdentity()
        {
            string token = socket != null ? socket.SessionToken : string.Empty;
            if (string.IsNullOrWhiteSpace(token)) return;

            activeSessionFingerprint = Fingerprint(token);
            string previous = PlayerPrefs.GetString(SessionFingerprintKey, string.Empty);
            platformSessionChanged = !string.IsNullOrEmpty(previous)
                && !string.Equals(previous, activeSessionFingerprint, StringComparison.Ordinal);

            if (platformSessionChanged)
            {
                ClearLocalProgress(preserveTutorial: true);
                MarkLocalProgressChanged();
                continuedLevels.Clear();
            }
            else
            {
                for (int i = 0; i < LevelCount; i++)
                    if (PlayerPrefs.HasKey(SessionContinuedPrefix + i)) continuedLevels.Add(i);
            }

            PlayerPrefs.SetString(SessionFingerprintKey, activeSessionFingerprint);
            PlayerPrefs.Save();
        }

        static string Fingerprint(string value)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }
                return hash.ToString("X16");
            }
        }

        void OnConnectionFailed()
        {
            online = false;
            Debug.LogWarning("[Luxodd] Connection failed; the game will keep using local state.");
            ContinuePendingLossLocally("the host connection failed during Continue");
        }

        void RequestIdentityAndCloudState()
        {
            profileReady = false;
            cloudReady = false;
            cloudRequestSucceeded = false;
            cloudResolved = false;

            commands.SendProfileRequestCommand(
                name =>
                {
                    playerName = string.IsNullOrWhiteSpace(name) ? "PLAYER" : name;
                    string previous = PlayerPrefs.GetString(ProfileKey, string.Empty);
                    identityChanged = !string.IsNullOrEmpty(previous)
                        && !string.Equals(previous, playerName, StringComparison.Ordinal);
                    profileReady = true;
                    TryResolveCloudState();
                },
                (code, message) =>
                {
                    Debug.LogWarning($"[Luxodd] Profile request failed ({code}): {message}");
                    profileReady = true; // do not block state recovery if profile is temporarily unavailable
                    TryResolveCloudState();
                });

            commands.SendGetUserDataRequestCommand(
                response =>
                {
                    cloudRaw = response;
                    cloudRequestSucceeded = true;
                    cloudReady = true;
                    TryResolveCloudState();
                },
                (code, message) =>
                {
                    Debug.LogWarning($"[Luxodd] User-state request failed ({code}): {message}");
                    cloudReady = true;
                    TryResolveCloudState();
                });
        }

        void TryResolveCloudState()
        {
            if (!profileReady || !cloudReady || cloudResolved) return;
            if (!cloudRequestSucceeded)
            {
                // A failed read must never overwrite server data with a speculative local save.
                return;
            }

            if (!TryReadCloudState(cloudRaw, out var remoteState, out bool containedData))
            {
                Debug.LogWarning("[Luxodd] User state was returned but could not be parsed; local state was left untouched.");
                return;
            }

            identityChanged |= platformSessionChanged;
            if (identityChanged && !platformSessionChanged)
                ClearLocalProgress(preserveTutorial: true);
            if (containedData && remoteState != null)
                ApplyCloudState(remoteState, replace: identityChanged);

            if (!string.IsNullOrEmpty(playerName)) PlayerPrefs.SetString(ProfileKey, playerName);
            PlayerPrefs.Save();
            cloudResolved = true;
            ProgressLoaded?.Invoke();
            RefreshRegisteredLeaderboards();

            // Upload once after merge (or after a first-time empty state) so server and client use
            // the same schema from this point forward.
            savePending = true;
            UploadCloudState();
        }

        static bool TryReadCloudState(object response, out ParaboxCloudState state, out bool containedData)
        {
            state = null;
            object data = response is Luxodd.Game.Scripts.Network.Payloads.UserDataPayload payload
                ? payload.Data : response;
            containedData = data != null;
            if (!containedData) return true;

            try
            {
#if NEWTONSOFT_JSON
                JToken token = data as JToken ?? JToken.FromObject(data);
                // Plugin 1.0.x returns stored state as { user_data: <value> } inside the outer
                // UserDataPayload. Accept that wrapper as well as direct objects and JSON strings.
                for (int depth = 0; depth < 4 && token != null; depth++)
                {
                    if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                    {
                        containedData = false;
                        return true;
                    }
                    if (token is JObject wrapper && wrapper.TryGetValue("user_data", out JToken nested))
                    {
                        token = nested;
                        continue;
                    }
                    if (token.Type == JTokenType.String)
                    {
                        string raw = token.Value<string>();
                        if (string.IsNullOrWhiteSpace(raw)) return false;
                        token = JToken.Parse(raw);
                        continue;
                    }
                    break;
                }
                state = token?.ToObject<ParaboxCloudState>();
#else
                string json = data as string;
                if (string.IsNullOrWhiteSpace(json)) return false;
                state = JsonUtility.FromJson<ParaboxCloudState>(json);
#endif
                return state != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Luxodd] Invalid cloud state: " + ex.Message);
                return false;
            }
        }

        static ParaboxCloudState CaptureCloudState()
        {
            ScoreSystem.EnsureCurrentVersion(LevelCount);
            long updatedAt = LocalProgressUpdatedAt();
            if (updatedAt <= 0)
            {
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                PlayerPrefs.SetString(SessionUpdatedKey, updatedAt.ToString());
            }
            var state = new ParaboxCloudState
            {
                formatVersion = 3,
                scoreVersion = ScoreSystem.CurrentVersion,
                currentLevel = Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0), 0, LevelCount - 1),
                tutorialSeen = PlayerPrefs.GetInt(TutorialKey, 0) == 1,
                originFilmSeen = PlayerPrefs.GetInt(OriginFilmKey, 0) == 1,
                sessionFingerprint = PlayerPrefs.GetString(SessionFingerprintKey, string.Empty),
                updatedAtUnix = updatedAt,
                bestMoves = new int[LevelCount],
                bestScores = new int[LevelCount],
                cleared = new bool[LevelCount],
                continued = new bool[LevelCount]
            };

            for (int i = 0; i < LevelCount; i++)
            {
                state.bestMoves[i] = PlayerPrefs.GetInt(BestPrefix + i, 0);
                state.bestScores[i] = PlayerPrefs.GetInt(ScoreSystem.ScoreKey(i), 0);
                state.cleared[i] = PlayerPrefs.HasKey(GameManager.SessionClearKey(i));
                state.continued[i] = PlayerPrefs.HasKey(SessionContinuedPrefix + i);
            }
            return state;
        }

        static void ApplyCloudState(ParaboxCloudState state, bool replace)
        {
            if (state == null) return;
            if (state.tutorialSeen) PlayerPrefs.SetInt(TutorialKey, 1);
            if (state.originFilmSeen) PlayerPrefs.SetInt(OriginFilmKey, 1);

            // Account data can outlive a paid cabinet session. Restore run data only when both
            // sides identify the same Luxodd session; otherwise an old run must not revive.
            string localSession = PlayerPrefs.GetString(SessionFingerprintKey, string.Empty);
            if (string.IsNullOrEmpty(state.sessionFingerprint)
                || string.IsNullOrEmpty(localSession)
                || !string.Equals(state.sessionFingerprint, localSession, StringComparison.Ordinal))
                return;

            ScoreSystem.EnsureCurrentVersion(LevelCount);
            if (replace) ClearLocalProgress(preserveTutorial: true);

            long localUpdated = LocalProgressUpdatedAt();
            if (replace || state.updatedAtUnix >= localUpdated)
            {
                PlayerPrefs.SetInt(LevelKey, Mathf.Clamp(state.currentLevel, 0, LevelCount - 1));
                PlayerPrefs.SetString(SessionUpdatedKey, state.updatedAtUnix.ToString());
            }

            for (int i = 0; i < LevelCount; i++)
            {
                if (state.cleared != null && i < state.cleared.Length && state.cleared[i])
                    PlayerPrefs.SetInt(GameManager.SessionClearKey(i), 1);

                if (state.continued != null && i < state.continued.Length && state.continued[i])
                {
                    PlayerPrefs.SetInt(SessionContinuedPrefix + i, 1);
                    if (Instance != null) Instance.continuedLevels.Add(i);
                }

                if (state.bestMoves != null && i < state.bestMoves.Length && state.bestMoves[i] > 0)
                {
                    int localBest = PlayerPrefs.GetInt(BestPrefix + i, 0);
                    if (localBest <= 0 || state.bestMoves[i] < localBest)
                        PlayerPrefs.SetInt(BestPrefix + i, state.bestMoves[i]);
                }

                if (state.bestScores != null && i < state.bestScores.Length)
                {
                    string scoreKey = ScoreSystem.ScoreKey(i);
                    int localScore = PlayerPrefs.GetInt(scoreKey, 0);
                    int cloudScore = ScoreSystem.ConvertToCurrentVersion(
                        state.bestScores[i], state.scoreVersion);
                    if (cloudScore > localScore)
                        PlayerPrefs.SetInt(scoreKey, cloudScore);
                }
            }
        }

        static void ClearLocalProgress(bool preserveTutorial = false)
        {
            for (int i = 0; i < LevelCount; i++)
            {
                PlayerPrefs.DeleteKey(BestPrefix + i);
                PlayerPrefs.DeleteKey(ScoreSystem.ScoreKey(i));
                PlayerPrefs.DeleteKey(GameManager.SessionClearKey(i));
                PlayerPrefs.DeleteKey(SessionContinuedPrefix + i);
            }
            PlayerPrefs.SetInt(LevelKey, 0);
            PlayerPrefs.DeleteKey(SessionUpdatedKey);
            if (!preserveTutorial)
            {
                PlayerPrefs.DeleteKey(TutorialKey);
                PlayerPrefs.DeleteKey(OriginFilmKey);
            }
        }

        static long LocalProgressUpdatedAt()
        {
            return long.TryParse(PlayerPrefs.GetString(SessionUpdatedKey, "0"), out long value)
                ? value : 0L;
        }

        static void MarkLocalProgressChanged()
        {
            PlayerPrefs.SetString(SessionUpdatedKey,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
        }

        void EndLocalSession()
        {
            ClearLocalProgress(preserveTutorial: true);
            continuedLevels.Clear();
            PlayerPrefs.SetString(SessionFingerprintKey, "ENDED");
            MarkLocalProgressChanged();
            PlayerPrefs.Save();
        }

        void UploadCloudState()
        {
            if (!online || !cloudResolved || saveInFlight || !savePending) return;
            savePending = false;
            saveInFlight = true;
            ParaboxCloudState state = CaptureCloudState();
#if NEWTONSOFT_JSON
            object payload = JsonConvert.SerializeObject(state);
#else
            object payload = JsonUtility.ToJson(state);
#endif
            commands.SendSetUserDataRequestCommand(payload,
                () =>
                {
                    saveInFlight = false;
                    if (savePending) UploadCloudState();
                },
                (code, message) =>
                {
                    saveInFlight = false;
                    savePending = true;
                    Debug.LogWarning($"[Luxodd] User-state save failed ({code}): {message}");
                });
        }

        public static void SyncProgress()
        {
            MarkLocalProgressChanged();
            PlayerPrefs.Save();
            if (Instance == null) return;
            Instance.savePending = true;
            Instance.UploadCloudState();
            RefreshRegisteredLeaderboards();
        }

        // White/Back on the title screen returns ownership to the Luxodd shell. The WebGL bridge
        // performs the real host transition; editor and standalone fallbacks remain convenient for
        // local testing without changing arcade behavior.
        public static void ReturnToSystem()
        {
            // The cabinet may keep the WebGL instance alive after returning to its shell. Clear
            // this player's local route now as well as at application startup, so the next player
            // always receives Level 1 with Levels 2-50 locked.
            MainMenuUI.ResetCampaignSessionProgress();
            if (Instance != null) Instance.EndLocalSession();
            if (Instance != null && Instance.socket != null)
            {
                Instance.socket.BackToSystem();
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public static void ReportLevelBegin(int zeroBasedLevel)
        {
            if (Instance == null) return;
            Instance.QueueOrSendLevelEvent(new PendingLevelEvent(true, zeroBasedLevel + 1, 0));
        }

        public static void ReportLevelEnd(int zeroBasedLevel, int score)
        {
            if (Instance == null) return;
            Instance.QueueOrSendLevelEvent(new PendingLevelEvent(false, zeroBasedLevel + 1, Mathf.Max(0, score)));
            SyncProgress();
        }

        // Every loss opens Luxodd's official Continue transaction. The caller schedules this only
        // after the leaderboard reading beat and owns restoration of the still-live puzzle state.
        public static void RequestLossTransaction(int zeroBasedLevel, int score, Action onContinue)
        {
            if (Instance == null || Instance.socket == null)
            {
                Debug.LogWarning("[Luxodd] Continue transaction is unavailable; using the local Continue fallback.");
                onContinue?.Invoke();
                return;
            }

            // Do not queue this forever. By the time GameManager calls here the leaderboard has
            // already been visible for 3.5 seconds; an offline host cannot display or complete its
            // transaction, so the only safe path is to restore the still-live attempt locally.
            if (!Instance.online)
            {
                Debug.LogWarning("[Luxodd] Host is offline at Continue time; using the local Continue fallback.");
                onContinue?.Invoke();
                return;
            }

            int level = Mathf.Max(0, zeroBasedLevel);
            if (Instance.pendingLossTransaction == null
                || Instance.pendingLossTransaction.level != level)
            {
                Instance.pendingLossTransaction = new LossTransaction(level, Mathf.Max(0, score), onContinue);
            }
            Instance.TryStartLossTransaction();
        }

        void ContinuePendingLossLocally(string reason)
        {
            LossTransaction transaction = pendingLossTransaction;
            if (transaction == null) return;

            pendingLossTransaction = null;
            transactionPopupOpen = false;
            Debug.LogWarning($"[Luxodd] {reason}; using the local Continue fallback.");
            transaction.onContinue?.Invoke();
        }

        // Leaving the loss screen invalidates its callback. A late response from an already-open
        // host popup will then be ignored by the identity check in the transaction handlers.
        public static void AbandonLossTransaction()
        {
            if (Instance == null) return;
            Instance.pendingLossTransaction = null;
            Instance.transactionPopupOpen = false;
        }

        void TryStartLossTransaction()
        {
            if (!online || socket == null || pendingLossTransaction == null || transactionPopupOpen) return;

            LossTransaction transaction = pendingLossTransaction;
            transactionPopupOpen = true;
            Debug.Log($"[Luxodd] Opening Continue transaction for level {transaction.level + 1}.");
            socket.SendSessionOptionContinue(action => HandleContinueChoice(transaction, action));
#if UNITY_EDITOR || !UNITY_WEBGL
            HandleContinueChoice(transaction, SessionOptionAction.Continue);
#endif
        }

        void HandleContinueChoice(LossTransaction transaction, SessionOptionAction action)
        {
            transactionPopupOpen = false;
            if (pendingLossTransaction != transaction) return;

            switch (action)
            {
                case SessionOptionAction.Continue:
                    continuedLevels.Add(transaction.level);
                    PlayerPrefs.SetInt(SessionContinuedPrefix + transaction.level, 1);
                    PlayerPrefs.Save();
                    pendingLossTransaction = null;
                    SyncProgress();
                    transaction.onContinue?.Invoke();
                    break;

                case SessionOptionAction.Restart:
                    transactionPopupOpen = true;
                    FinalizeLoss(transaction, () => OpenRestartPopup(transaction));
                    break;

                case SessionOptionAction.End:
                    transactionPopupOpen = true;
                    FinalizeLoss(transaction, () =>
                    {
                        pendingLossTransaction = null;
                        EndLocalSession();
                        socket.BackToSystem();
                    });
                    break;

                case SessionOptionAction.Cancel:
                    // There are intentionally no local loss-screen buttons. Leave the leaderboard
                    // visible, then reopen the official host choice after another reading beat.
                    StartCoroutine(RetryLossTransaction(transaction));
                    break;
            }
        }

        IEnumerator RetryLossTransaction(LossTransaction transaction)
        {
            float delay = 3.5f;
            while (delay > 0f)
            {
                delay -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (pendingLossTransaction == transaction) TryStartLossTransaction();
        }

        void FinalizeLoss(LossTransaction transaction, Action afterSuccess)
        {
            if (transaction.finalized)
            {
                afterSuccess?.Invoke();
                return;
            }
            if (transaction.finalizing) return;

            transaction.finalizing = true;
            var item = new PendingLevelEvent(false, transaction.level + 1, transaction.score, () =>
            {
                transaction.finalizing = false;
                transaction.finalized = true;
                afterSuccess?.Invoke();
            }, () =>
            {
                transaction.finalizing = false;
                transactionPopupOpen = false;
            });
            QueueOrSendLevelEvent(item);
            SyncProgress();
        }

        void OpenRestartPopup(LossTransaction transaction)
        {
            if (!online || socket == null || pendingLossTransaction != transaction)
            {
                transactionPopupOpen = false;
                return;
            }

            transactionPopupOpen = true;
            Debug.Log($"[Luxodd] Opening Restart transaction for level {transaction.level + 1}.");
            socket.SendSessionOptionRestart(action => HandleRestartChoice(transaction, action));
        }

        void HandleRestartChoice(LossTransaction transaction, SessionOptionAction action)
        {
            transactionPopupOpen = false;
            if (pendingLossTransaction != transaction) return;

            if (action == SessionOptionAction.End)
            {
                pendingLossTransaction = null;
                EndLocalSession();
                socket.BackToSystem();
            }
            else if (action == SessionOptionAction.Continue)
            {
                pendingLossTransaction = null;
                transaction.onContinue?.Invoke();
            }
            else if (action == SessionOptionAction.Restart)
            {
                // Successful Restart is owned by the Luxodd host and normally has no callback.
                pendingLossTransaction = null;
            }
            // Cancel deliberately keeps the finalized loss available for reopening.
        }

        void QueueOrSendLevelEvent(PendingLevelEvent item)
        {
            if (!online)
            {
                pendingLevelEvents.Add(item);
                return;
            }
            SendLevelEvent(item);
        }

        void FlushLevelEvents()
        {
            if (!online || pendingLevelEvents.Count == 0) return;
            var copy = pendingLevelEvents.ToArray();
            pendingLevelEvents.Clear();
            for (int i = 0; i < copy.Length; i++) SendLevelEvent(copy[i]);
        }

        void SendLevelEvent(PendingLevelEvent item)
        {
            if (item.begin)
            {
                commands.SendLevelBeginRequestCommand(item.level,
                    () =>
                    {
                        Debug.Log($"[Luxodd] Level {item.level} begin reported.");
                        item.onSuccess?.Invoke();
                    },
                    (code, message) =>
                    {
                        RequeueFailedLevelEvent(item, "begin", code, message);
                    });
                return;
            }

            commands.SendLevelEndRequestCommand(item.level, item.score,
                () =>
                {
                    Debug.Log($"[Luxodd] Level {item.level} end reported with score {item.score}.");
                    RequestLeaderboard();
                    item.onSuccess?.Invoke();
                },
                (code, message) =>
                {
                    RequeueFailedLevelEvent(item, "end", code, message);
                });
        }

        void RequeueFailedLevelEvent(PendingLevelEvent item, string kind, int code, string message)
        {
            // Never silently lose Luxodd session analytics. FlushLevelEvents removes an item
            // before sending it, so retaining one copy here is safe and prevents retry storms.
            item.retryCount++;
            if (!pendingLevelEvents.Contains(item)) pendingLevelEvents.Add(item);
            Debug.LogWarning($"[Luxodd] Level-{kind} report failed ({code}): {message}; queued for retry.");
            item.onFailure?.Invoke();
            ScheduleLevelEventRetry();
        }

        void ScheduleLevelEventRetry()
        {
            if (!online || levelRetryScheduled || pendingLevelEvents.Count == 0) return;
            levelRetryScheduled = true;
            StartCoroutine(RetryPendingLevelEvents());
        }

        IEnumerator RetryPendingLevelEvents()
        {
            int attempts = 1;
            for (int i = 0; i < pendingLevelEvents.Count; i++)
                attempts = Mathf.Max(attempts, pendingLevelEvents[i].retryCount);
            float delay = Mathf.Min(30f, Mathf.Pow(2f, Mathf.Min(attempts, 4)));
            yield return new WaitForSecondsRealtime(delay);
            levelRetryScheduled = false;
            if (online) FlushLevelEvents();
        }

        void RequestLeaderboard()
        {
            if (!online) return;
            commands.SendLeaderboardRequestCommand(ApplyLeaderboardResponse,
                (code, message) =>
                    Debug.LogWarning($"[Luxodd] Leaderboard request failed ({code}): {message}; placeholders remain visible."));
        }

        void ApplyLeaderboardResponse(LeaderboardDataResponse response)
        {
            remoteLeaderboard.Clear();
            if (response != null)
            {
                LeaderboardData currentUser = response.CurrentUserData;
                var rows = response.Leaderboard != null
                    ? new List<LeaderboardData>(response.Leaderboard)
                    : new List<LeaderboardData>();
                if (currentUser != null)
                {
                    bool alreadyPresent = rows.Exists(row => row != null &&
                        ((row.Rank > 0 && row.Rank == currentUser.Rank)
                         || (!string.IsNullOrWhiteSpace(row.PlayerName)
                             && string.Equals(row.PlayerName, currentUser.PlayerName,
                                 StringComparison.OrdinalIgnoreCase))));
                    if (!alreadyPresent) rows.Add(currentUser);
                }
                rows.Sort((a, b) => (a?.Rank ?? int.MaxValue).CompareTo(b?.Rank ?? int.MaxValue));
                for (int i = 0; i < rows.Count && remoteLeaderboard.Count < LossLeaderboard.Capacity; i++)
                {
                    var row = rows[i];
                    if (row == null) continue;
                    remoteLeaderboard.Add(new LeaderboardEntry(row.Rank, row.PlayerName, row.TotalScore));
                }
            }
            leaderboardLoaded = true;
            RefreshRegisteredLeaderboards();
        }

        static void RefreshRegisteredLeaderboards()
        {
            if (Instance == null) return;
            RegisteredLeaderboards.RemoveWhere(board => board == null);
            foreach (var board in RegisteredLeaderboards) Instance.PopulateLeaderboard(board);
        }

        void PopulateLeaderboard(LossLeaderboard board)
        {
            if (board == null || !leaderboardLoaded) return;
            // Rank and score must come from the same authoritative Luxodd response. Substituting
            // a local run score into a server-ranked row creates impossible rank/score pairs.
            board.SetEntries(remoteLeaderboard);
        }

        public static void RegisterLeaderboard(LossLeaderboard board)
        {
            if (board == null) return;
            RegisteredLeaderboards.Add(board);
            if (Instance != null) Instance.PopulateLeaderboard(board);
        }

        public static void UnregisterLeaderboard(LossLeaderboard board)
        {
            if (board != null) RegisteredLeaderboards.Remove(board);
        }

        sealed class PendingLevelEvent
        {
            public readonly bool begin;
            public readonly int level;
            public readonly int score;
            public readonly Action onSuccess;
            public readonly Action onFailure;
            public int retryCount;

            public PendingLevelEvent(bool begin, int level, int score, Action onSuccess = null,
                Action onFailure = null)
            {
                this.begin = begin;
                this.level = level;
                this.score = score;
                this.onSuccess = onSuccess;
                this.onFailure = onFailure;
            }
        }

        sealed class LossTransaction
        {
            public readonly int level;
            public readonly int score;
            public readonly Action onContinue;
            public bool finalizing;
            public bool finalized;

            public LossTransaction(int level, int score, Action onContinue)
            {
                this.level = level;
                this.score = score;
                this.onContinue = onContinue;
            }
        }

        [Serializable]
        public class ParaboxCloudState
        {
            public int formatVersion;
            public int scoreVersion;
            public int currentLevel;
            public bool tutorialSeen;
            public bool originFilmSeen;
            public string sessionFingerprint;
            public long updatedAtUnix;
            public int[] bestMoves;
            public int[] bestScores;
            public bool[] cleared;
            public bool[] continued;
        }
    }
}
