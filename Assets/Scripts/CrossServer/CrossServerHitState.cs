using AtlasNet;
using UnityEngine;

// Tiny persistent target state proving that a projectile can affect another worker's player.
public sealed class CrossServerHitState : NetworkBehaviour
{
    private readonly NetworkVariable<int> hitCount = new NetworkVariable<int>(0);
    public int HitCount => hitCount.Value;

    public void RecordProjectileHit(NetworkObject projectile)
    {
        if (projectile == null || !projectile.HasAuthority) return;
        SendAuthorityFrom(projectile, nameof(ApplyHitRpc), 1);
    }

    [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Server)]
    private void ApplyHitRpc(int amount)
    {
        if (!HasAuthority || amount != 1) return;
        hitCount.Value++;
        Debug.Log($"AtlasNet entity {NetworkObject.EntityId} was hit; total {hitCount.Value}");
    }
}
