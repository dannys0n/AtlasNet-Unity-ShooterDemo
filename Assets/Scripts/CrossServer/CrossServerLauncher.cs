using System;
using AtlasNet;
using UnityEngine;

// Used only by the two cross-server copies; the original authority launchers stay untouched.
public sealed class CrossServerLauncher : MonoBehaviour
{
    [SerializeField] private NetworkManager manager;
    [SerializeField] private GameObject debugViewPrefab;
    [SerializeField] private Vector3 firstSpawn = Vector3.zero;
    [SerializeField] private Vector3 otherSpawn = new Vector3(-3f, 0f, 0f);
    private string error;
    private int playerSpawnCount;

    private void Awake()
    {
        Application.runInBackground = true;
        if (debugViewPrefab != null) Instantiate(debugViewPrefab);
        manager.PlayerSpawnPosition = session =>
        {
            Vector3 position = playerSpawnCount++ % 2 == 0 ? firstSpawn : otherSpawn;
            Debug.Log($"AtlasNet spawning player session {session} at {position}");
            return position;
        };
    }

    private void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-atlas-host") >= 0) StartSafely(manager.StartHost);
        else if (Array.IndexOf(args, "-atlas-server") >= 0) StartSafely(manager.StartServer);
        else if (Array.IndexOf(args, "-atlas-worker") >= 0) StartSafely(manager.StartWorker);
        else if (Array.IndexOf(args, "-atlas-client") >= 0) StartSafely(manager.StartClient);
    }

    private void StartSafely(Action action)
    {
        try { playerSpawnCount = 0; action(); error = null; }
        catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 330, 340), GUI.skin.box);
        if (!manager.IsRunning)
        {
            if (GUILayout.Button("Start Host")) StartSafely(manager.StartHost);
            if (GUILayout.Button("Start Server Only")) StartSafely(manager.StartServer);
            if (GUILayout.Button("Join Worker")) StartSafely(manager.StartWorker);
            if (GUILayout.Button("Join Client")) StartSafely(manager.StartClient);
        }
        else
        {
            GUILayout.Label(manager.IsWorker ? "Worker" : manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client");
            GUILayout.Label($"Worker {manager.LocalWorkerId} / {manager.WorkerCount}, tick {manager.Tick}");
            GUILayout.Label($"Local authority {manager.LocalAuthorityCount}, ghosts {manager.GhostCount}");
            GUILayout.Label($"Local replicas {manager.SpawnedCount}, pending handoffs {manager.PendingHandoffCount}");
            foreach (NetworkObject obj in manager.SpawnedObjects)
                if (obj != null && obj.IsOwner)
                {
                    var hits = obj.GetComponent<CrossServerHitState>();
                    GUILayout.Label($"Your entity {obj.EntityId}: worker {obj.SimulationWorker}, epoch {obj.AuthorityEpoch}, hits {hits?.HitCount ?? 0}");
                    var interest = obj.GetComponent<NetworkInterestSource>();
                    if (interest != null) GUILayout.Label($"Cross-worker radius {interest.Radius:0.0} (+{interest.ExitPadding:0.0} exit)");
                    break;
                }
            int shown = 0;
            foreach (NetworkObject obj in manager.SpawnedObjects)
            {
                if (shown >= 3) break;
                if (obj == null || obj.IsOwner) continue;
                var hits = obj.GetComponent<CrossServerHitState>();
                GUILayout.Label($"Entity {obj.EntityId}: worker {obj.SimulationWorker}, epoch {obj.AuthorityEpoch}, hits {hits?.HitCount ?? 0}");
                shown++;
            }
            GUILayout.Label("F: networked test projectile; normal gun controls unchanged");
        }
        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);
        GUILayout.EndArea();
    }
}
