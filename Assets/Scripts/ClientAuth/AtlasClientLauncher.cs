using AtlasNet;
using UnityEngine;

// Small scene-only launcher so the sample can be tested in two Play Mode windows.
public sealed class AtlasClientLauncher : MonoBehaviour
{
    [SerializeField] private NetworkManager manager;

    private void Awake()
    {
        Application.runInBackground = true;
        manager.PlayerSpawnPosition = session => new Vector3((session.Value % 4) * 2f, 0f, 0f);
    }

    private void OnGUI()
    {
        if (manager.IsRunning) return;
        GUILayout.BeginArea(new Rect(10, 10, 180, 110), GUI.skin.box);
        if (GUILayout.Button("Start Host")) manager.StartHost();
        if (GUILayout.Button("Join Client")) manager.StartClient();
        GUILayout.EndArea();
    }
}
