using AtlasNet;
using InfimaGames.LowPolyShooterPack;
using UnityEngine;
using UnityEngine.InputSystem;

// F fires a deliberately small, networked test projectile. Existing gun input stays intact.
public sealed class CrossServerProjectileFire : NetworkBehaviour
{
    [SerializeField] private NetworkObject projectilePrefab;
    [SerializeField] private float cooldown = 0.25f;

    private Transform ownerAim;
    private float nextFireTime;
    private float nextAuthoritativeFireTime;

    public override void OnNetworkSpawn()
    {
        if (IsOwner) ownerAim = GetComponent<CharacterBehaviour>().GetCameraWorld().transform;
    }

    private void Update()
    {
        if (!IsOwner || ownerAim == null || Keyboard.current == null ||
            !Keyboard.current.fKey.wasPressedThisFrame || Time.time < nextFireTime) return;
        nextFireTime = Time.time + cooldown;
        FireIntentRpc(ownerAim.position, ownerAim.forward);
    }

    [Rpc(SendTo.Authority)]
    private void FireIntentRpc(Vector3 origin, Vector3 direction)
    {
        if (projectilePrefab == null || Time.time < nextAuthoritativeFireTime ||
            float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z) ||
            float.IsInfinity(origin.x) || float.IsInfinity(origin.y) || float.IsInfinity(origin.z) ||
            float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z) ||
            float.IsInfinity(direction.x) || float.IsInfinity(direction.y) || float.IsInfinity(direction.z) ||
            direction.sqrMagnitude < 0.9f || direction.sqrMagnitude > 1.1f ||
            (origin - transform.position).sqrMagnitude > 9f) return;
        nextAuthoritativeFireTime = Time.time + cooldown;
        Vector3 start = origin + direction * 0.6f;
        NetworkManager.RequestSpawn(NetworkObject, projectilePrefab, start,
            Quaternion.LookRotation(direction), OwnerSession);
    }

    protected override void WriteHandoffState(NetWriter writer) =>
        writer.Write(Mathf.Max(0f, nextAuthoritativeFireTime - Time.time));

    protected override void ReadHandoffState(NetReader reader) =>
        nextAuthoritativeFireTime = Time.time + reader.ReadFloat();
}
