using AtlasNet;
using UnityEngine;

// Server-simulated actor: it can cross a shard boundary and keep its remaining life.
public sealed class CrossServerProjectile : NetworkBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifetime = 4f;

    private float remainingLife;
    private bool ending;

    public override void OnNetworkSpawn()
    {
        if (remainingLife == 0f && !ending) remainingLife = lifetime;
    }

    public override void OnNetworkTick()
    {
        if (!HasAuthority || ending) return;
        float delta = 1f / NetworkManager.TickRate;
        remainingLife -= delta;
        if (remainingLife <= 0f)
        {
            ending = true;
            NetworkObject.Despawn();
            return;
        }

        Vector3 origin = transform.position;
        Vector3 direction = transform.forward;
        float distance = speed * delta;
        var hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            NetworkObject other = hit.collider.GetComponentInParent<NetworkObject>();
            if (other == NetworkObject || other != null && other.OwnerSession == OwnerSession) continue;
            ending = true;
            if (other != null) other.GetComponent<CrossServerHitState>()?.RecordProjectileHit(NetworkObject);
            ImpactRpc(hit.point, hit.normal);
            NetworkObject.Despawn();
            return;
        }
        transform.position = origin + direction * distance;
    }

    protected override void WriteHandoffState(NetWriter writer)
    {
        writer.Write(remainingLife);
        writer.Write(ending);
    }

    protected override void ReadHandoffState(NetReader reader)
    {
        remainingLife = reader.ReadFloat();
        ending = reader.ReadBool();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ImpactRpc(Vector3 point, Vector3 normal)
    {
        if (!IsClient) return;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Cross-server projectile impact";
        marker.transform.position = point + normal * 0.05f;
        marker.transform.localScale = Vector3.one * 0.2f;
        Destroy(marker.GetComponent<Collider>());
        Destroy(marker, 0.3f);
    }
}
