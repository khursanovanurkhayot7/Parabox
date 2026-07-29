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
        const string BestPrefix = "Parabox.Best.";
        const int LevelCount = 50;

        public static LuxoddGameService Instance { get; private set; }
        public static bool IsOnline => Instance != null && Instance.online;
        public static string PlayerName => Instance != null ? Instance.playerName : string.Empty;
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
        bool leaderboardLoaded;
        string playerName;
        object cloudRaw;
        LossTransaction pendingLossTransaction;
        bool transactionPopupOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
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
            if (connected) OnConnected();
        }

        void OnConnected()
        {
            online = true;
            if (onlineInitialized)
            {
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

        void OnConnectionFailed()
        {
            online = false;
            Debug.LogWarning("[Luxodd] Connection failed; the game will keep using local state.");
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

            if (identityChanged) ClearLocalProgress();
            if (containedData && remoteState != null)
                ApplyCloudState(remoteState, replace: identityChanged);

            if (!string.IsNullOrEmpty(playerName)) PlayerPrefs.SetString(ProfileKey, playerName);
            PlayerPrefs.Save();
            cloudResolved = true;
            ProgressLoaded?.Invoke();

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
            var state = new ParaboxCloudState
            {
                formatVersion = 1,
                currentLevel = Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0), 0, LevelCount - 1),
                tutorialSeen = PlayerPrefs.GetInt(TutorialKey, 0) == 1,
                updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                bestMoves = new int[LevelCount],
                bestScores = new int[LevelCount]
            };

            for (int i = 0; i < LevelCount; i++)
            {
                state.bestMoves[i] = PlayerPrefs.GetInt(BestPrefix + i, 0);
                state.bestScores[i] = PlayerPrefs.GetInt(ScoreSystem.ScoreKey(i), 0);
            }
            return state;
        }

        static void ApplyCloudState(ParaboxCloudState state, bool replace)
        {
            if (state == null) return;
            if (replace) ClearLocalProgress();

            int localLevel = PlayerPrefs.GetInt(LevelKey, 0);
            int remoteLevel = Mathf.Clamp(state.currentLevel, 0, LevelCount - 1);
            PlayerPrefs.SetInt(LevelKey, replace ? remoteLevel : Mathf.Max(localLevel, remoteLevel));

            for (int i = 0; i < LevelCount; i++)
            {
                int remoteMoves = state.bestMoves != null && i < state.bestMoves.Length
                    ? Mathf.Max(0, state.bestMoves[i]) : 0;
                if (remoteMoves > 0)
                {
                    int localMoves = PlayerPrefs.GetInt(BestPrefix + i, 0);
                    PlayerPrefs.SetInt(BestPrefix + i,
                        replace || localMoves <= 0 ? remoteMoves : Mathf.Min(localMoves, remoteMoves));
                }

                int remoteScore = state.bestScores != null && i < state.bestScores.Length
                    ? Mathf.Max(0, state.bestScores[i]) : 0;
                if (remoteScore > 0)
                {
                    int localScore = PlayerPrefs.GetInt(ScoreSystem.ScoreKey(i), 0);
                    PlayerPrefs.SetInt(ScoreSystem.ScoreKey(i), replace
                        ? remoteScore : Mathf.Max(localScore, remoteScore));
                }
            }

            if (state.tutorialSeen) PlayerPrefs.SetInt(TutorialKey, 1);
            else if (replace) PlayerPrefs.DeleteKey(TutorialKey);
        }

        static void ClearLocalProgress()
        {
            for (int i = 0; i < LevelCount; i++)
            {
                PlayerPrefs.DeleteKey(BestPrefix + i);
                PlayerPrefs.DeleteKey(ScoreSystem.ScoreKey(i));
            }
            PlayerPrefs.SetInt(LevelKey, 0);
            PlayerPrefs.DeleteKey(TutorialKey);
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
            if (Instance == null) return;
            Instance.savePending = true;
            Instance.UploadCloudState();
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

        // First loss on a level: Continue popup (Continue/End). After a paid Continue has already
        // been used for that level: finalize results, then Restart popup (Restart/End). Luxodd
        // deliberately separates these flows because Continue keeps the session alive while
        // Restart creates a new system-owned session.
        public static void RequestLossTransaction(int zeroBasedLevel, int score, Action onContinue)
        {
            if (Instance == null)
            {
                Debug.LogWarning("[Luxodd] Transaction runtime is unavailable; the loss screen will remain open.");
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

        void TryStartLossTransaction()
        {
            if (!online || socket == null || pendingLossTransaction == null || transactionPopupOpen) return;

            LossTransaction transaction = pendingLossTransaction;
            transactionPopupOpen = true;
            if (continuedLevels.Contains(transaction.level))
            {
                FinalizeLoss(transaction, () => OpenRestartPopup(transaction));
                return;
            }

            Debug.Log($"[Luxodd] Opening Continue transaction for level {transaction.level + 1}.");
            socket.SendSessionOptionContinue(action => HandleContinueChoice(transaction, action));
        }

        void HandleContinueChoice(LossTransaction transaction, SessionOptionAction action)
        {
            transactionPopupOpen = false;
            if (pendingLossTransaction != transaction) return;

            switch (action)
            {
                case SessionOptionAction.Continue:
                    continuedLevels.Add(transaction.level);
                    pendingLossTransaction = null;
                    transaction.onContinue?.Invoke();
                    break;

                case SessionOptionAction.Restart:
                    // Defensive support for hosts that expose Restart from the Continue UI.
                    transactionPopupOpen = true;
                    FinalizeLoss(transaction, () => OpenRestartPopup(transaction));
                    break;

                case SessionOptionAction.End:
                    transactionPopupOpen = true;
                    FinalizeLoss(transaction, () =>
                    {
                        pendingLossTransaction = null;
                        socket.BackToSystem();
                    });
                    break;

                case SessionOptionAction.Cancel:
                    // Keep the loss screen visible. Black/Green/Enter/R can request the popup again.
                    break;
            }
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
                // Keep the leaderboard screen usable if finalization fails. The player can press
                // Black/Green/Enter/R to retry once connectivity returns.
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
                socket.BackToSystem();
            }
            else if (action == SessionOptionAction.Continue)
            {
                // Defensive only; Restart UI normally returns Restart/End.
                pendingLossTransaction = null;
                transaction.onContinue?.Invoke();
            }
            else if (action == SessionOptionAction.Restart)
            {
                // A successful Restart is normally handled by the host without a callback.
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
                        Debug.LogWarning($"[Luxodd] Level-begin report failed ({code}): {message}");
                        item.onFailure?.Invoke();
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
                    Debug.LogWarning($"[Luxodd] Level-end report failed ({code}): {message}");
                    item.onFailure?.Invoke();
                });
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
                var rows = response.Leaderboard != null
                    ? new List<LeaderboardData>(response.Leaderboard)
                    : new List<LeaderboardData>();
                if (response.CurrentUserData != null)
                {
                    bool alreadyPresent = rows.Exists(row => row != null &&
                        ((row.Rank > 0 && row.Rank == response.CurrentUserData.Rank)
                         || (!string.IsNullOrWhiteSpace(row.PlayerName)
                             && string.Equals(row.PlayerName, response.CurrentUserData.PlayerName,
                                 StringComparison.OrdinalIgnoreCase))));
                    if (!alreadyPresent) rows.Add(response.CurrentUserData);
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

        readonly struct PendingLevelEvent
        {
            public readonly bool begin;
            public readonly int level;
            public readonly int score;
            public readonly Action onSuccess;
            public readonly Action onFailure;

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
            public int currentLevel;
            public bool tutorialSeen;
            public long updatedAtUnix;
            public int[] bestMoves;
            public int[] bestScores;
        }
    }
}
