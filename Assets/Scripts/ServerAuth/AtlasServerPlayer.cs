using AtlasNet;
using InfimaGames.LowPolyShooterPack;
using UnityEngine;
using UnityEngine.InputSystem;

// Server-simulated movement and server-accepted shots; owner keeps raw mouse look.
public sealed class AtlasServerPlayer : NetworkBehaviour
{
    private const int CharacterLayer = 10;
    private const float MaximumShotDistance = 500f;

    [SerializeField] private GameObject hudPrefab;
    [SerializeField] private float walkingSpeed = 4f;
    [SerializeField] private float runningSpeed = 6.8f;
    [SerializeField] private float jumpSpeed = 5f;

    private readonly NetworkVariable<Quaternion> lookPitch = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> equippedWeapon = new NetworkVariable<int>(0);

    private CharacterBehaviour character;
    private AtlasClientCameraLook cameraLook;
    private PlayerInput playerInput;
    private CharacterController controller;
    private CharacterKinematics kinematics;
    private InventoryBehaviour inventory;
    private Animator characterAnimator;
    private AtlasServerWeapon[] weapons;
    private float[] nextShotTime;
    private float[] reloadUntil;
    private int[] handoffAmmunition;
    private GameObject hud;
    private Vector2 movementInput;
    private float movementYaw;
    private bool running;
    private bool jumpQueued;
    private float verticalSpeed;
    private int lastRequestedWeapon = -1;
    private bool wasInspecting;
    private int actionsLayer;

    public Transform AimTransform => character.GetCameraWorld().transform;

    private void Awake()
    {
        character = GetComponent<CharacterBehaviour>();
        cameraLook = GetComponentInChildren<AtlasClientCameraLook>(true);
        playerInput = GetComponent<PlayerInput>();
        controller = GetComponent<CharacterController>();
        kinematics = GetComponent<CharacterKinematics>();
        inventory = character.GetInventory();
        characterAnimator = GetComponentInChildren<Animator>(true);
        weapons = GetComponentsInChildren<AtlasServerWeapon>(true);
        nextShotTime = new float[weapons.Length];
        reloadUntil = new float[weapons.Length];
        handoffAmmunition = new int[weapons.Length];
        for (int i = 0; i < handoffAmmunition.Length; i++) handoffAmmunition[i] = -1;
        actionsLayer = characterAnimator.GetLayerIndex("Layer Actions");
    }

    public override void OnNetworkSpawn()
    {
        character.enabled = IsOwner;
        cameraLook.enabled = IsOwner;
        playerInput.enabled = IsOwner;
        controller.enabled = IsServer;
        foreach (Camera playerCamera in GetComponentsInChildren<Camera>(true))
        {
            playerCamera.enabled = IsOwner;
            AudioListener listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = IsOwner;
        }
        if (IsOwner)
        {
            ServiceLocator.Current.Unregister<IGameModeService>();
            ServiceLocator.Current.Register<IGameModeService>(new LocalGameModeService(character));
            if (hudPrefab != null) hud = Instantiate(hudPrefab);
        }
        else
        {
            SetLayerRecursively(gameObject, CharacterLayer);
            ApplyEquippedWeapon(equippedWeapon.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        character.enabled = false;
        cameraLook.enabled = false;
        playerInput.enabled = false;
        if (hud != null) Destroy(hud);
        if (IsOwner && ServiceLocator.Current != null)
        {
            ServiceLocator.Current.Unregister<IGameModeService>();
            ServiceLocator.Current.Register<IGameModeService>(new GameModeService());
        }
    }

    // Equip before NetworkAnimator reads the late-join animation snapshot.
    protected override void ReadExtraSnapshot(NetReader reader)
    {
        if (!IsOwner) ApplyEquippedWeapon(equippedWeapon.Value);
    }

    private void Update()
    {
        if (IsSpawned && IsOwner && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpQueued = true;
    }

    public override void OnNetworkTick()
    {
        if (HasAuthority) RestoreActiveWeaponAmmunition();
        if (IsOwner)
        {
            lookPitch.Value = cameraLook.transform.localRotation;
            int index = inventory.GetEquippedIndex();
            if (index >= 0 && index != lastRequestedWeapon)
            {
                lastRequestedWeapon = index;
                EquipIntentRpc(index);
            }
            bool jump = jumpQueued;
            jumpQueued = false;
            MoveIntentRpc(Vector2.ClampMagnitude(character.GetInputMovement(), 1f),
                character.IsRunning(), transform.eulerAngles.y, jump);
        }
        if (HasAuthority) SimulateMovement();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (!IsOwner)
        {
            cameraLook.transform.localRotation = lookPitch.Value;
            ApplyEquippedWeapon(equippedWeapon.Value);
            kinematics?.Compute();
            return;
        }

        bool inspecting = actionsLayer >= 0 &&
            (characterAnimator.GetCurrentAnimatorStateInfo(actionsLayer).IsName("Inspect") ||
             characterAnimator.IsInTransition(actionsLayer) &&
             characterAnimator.GetNextAnimatorStateInfo(actionsLayer).IsName("Inspect"));
        if (inspecting && !wasInspecting) PlayInspectRpc(inventory.GetEquippedIndex());
        wasInspecting = inspecting;
    }

    [Rpc(SendTo.Authority)]
    private void MoveIntentRpc(Vector2 input, bool run, float yaw, bool jump)
    {
        if (!Finite(yaw) || !Finite(input.x) || !Finite(input.y)) return;
        movementInput = Vector2.ClampMagnitude(input, 1f);
        movementYaw = yaw;
        running = run;
        jumpQueued |= jump;
    }

    private void SimulateMovement()
    {
        float delta = 1f / NetworkManager.TickRate;
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        if (controller.isGrounded && jumpQueued) verticalSpeed = jumpSpeed;
        jumpQueued = false;
        verticalSpeed += Physics.gravity.y * delta;
        Vector3 direction = Quaternion.Euler(0f, movementYaw, 0f) *
            new Vector3(movementInput.x, 0f, movementInput.y);
        Vector3 velocity = direction * (running ? runningSpeed : walkingSpeed);
        velocity.y = verticalSpeed;
        controller.Move(velocity * delta);
    }

    [Rpc(SendTo.Authority)]
    private void EquipIntentRpc(int index)
    {
        if (!TryGetWeapon(index, out _)) return;
        ApplyEquippedWeapon(index);
        equippedWeapon.Value = index;
    }

    public void RequestFire(AtlasServerWeapon weapon, Vector3 origin, Vector3 direction)
    {
        int index = System.Array.IndexOf(weapons, weapon);
        if (IsOwner && index >= 0)
            FireIntentRpc(index, origin, direction,
                transform.rotation, cameraLook.transform.localRotation);
    }

    [Rpc(SendTo.Authority)]
    private void FireIntentRpc(int index, Vector3 origin, Vector3 direction,
        Quaternion shotYaw, Quaternion shotPitch)
    {
        if (!TryGetWeapon(index, out AtlasServerWeapon weapon) ||
            index != equippedWeapon.Value ||
            !Finite(origin.x) || !Finite(origin.y) || !Finite(origin.z) ||
            !Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z) ||
            direction.sqrMagnitude < 0.5f ||
            (origin - transform.position).sqrMagnitude > 9f ||
            Time.time < nextShotTime[index] || Time.time < reloadUntil[index]) return;

        weapon.EnsureInitialized();
        if (!weapon.HasAmmunition()) return;
        nextShotTime[index] = Time.time + 60f / Mathf.Max(1f, weapon.GetRateOfFire());
        weapon.SetAuthoritativeAmmunition(weapon.GetAmmunitionCurrent() - 1);

        bool hit = Physics.Raycast(origin, direction.normalized, out RaycastHit result,
            MaximumShotDistance, ~0, QueryTriggerInteraction.Ignore);
        FireAcceptedRpc(index, shotYaw, shotPitch, hit,
            hit ? result.point : Vector3.zero, hit ? result.normal : Vector3.up,
            hit ? result.transform.tag : "", weapon.GetAmmunitionCurrent());
    }

    public void RequestReload(AtlasServerWeapon weapon)
    {
        int index = System.Array.IndexOf(weapons, weapon);
        if (IsOwner && index >= 0) ReloadIntentRpc(index);
    }

    [Rpc(SendTo.Authority)]
    private void ReloadIntentRpc(int index)
    {
        if (!TryGetWeapon(index, out AtlasServerWeapon weapon) ||
            index != equippedWeapon.Value || Time.time < reloadUntil[index]) return;
        weapon.EnsureInitialized();
        if (weapon.IsFull()) return;
        bool hadAmmunition = weapon.HasAmmunition();
        reloadUntil[index] = Time.time + 1.5f;
        weapon.SetAuthoritativeAmmunition(weapon.GetAmmunitionTotal());
        ReloadAcceptedRpc(index, hadAmmunition, weapon.GetAmmunitionCurrent());
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void FireAcceptedRpc(int index, Quaternion shotYaw, Quaternion shotPitch, bool hit,
        Vector3 point, Vector3 normal, string surface, int ammunition)
    {
        if (!IsClient || !TryGetWeapon(index, out AtlasServerWeapon weapon)) return;
        ApplyEquippedWeapon(index);
        weapon.SetAuthoritativeAmmunition(ammunition);
        if (!IsOwner)
        {
            transform.rotation = shotYaw;
            cameraLook.transform.localRotation = shotPitch;
            kinematics?.Compute();
            characterAnimator.CrossFade("Fire", 0.05f,
                characterAnimator.GetLayerIndex("Layer Overlay"), 0f);
            weapon.PlayAcceptedMuzzle();
        }
        if (hit) weapon.PlayImpactAt(point, normal, surface);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ReloadAcceptedRpc(int index, bool hadAmmunition, int ammunition)
    {
        if (!IsClient || !TryGetWeapon(index, out AtlasServerWeapon weapon)) return;
        ApplyEquippedWeapon(index);
        weapon.SetAuthoritativeAmmunition(ammunition);
        if (!IsOwner)
        {
            characterAnimator.Play(hadAmmunition ? "Reload" : "Reload Empty", actionsLayer, 0f);
            weapon.PlayAcceptedReload(hadAmmunition);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayInspectRpc(int index)
    {
        if (!IsClient || IsOwner) return;
        ApplyEquippedWeapon(index);
        characterAnimator.CrossFade("Inspect", 0f, actionsLayer, 0f);
    }

    private bool TryGetWeapon(int index, out AtlasServerWeapon weapon)
    {
        weapon = index >= 0 && index < weapons.Length ? weapons[index] : null;
        return weapon != null;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void ApplyEquippedWeapon(int index)
    {
        if (!TryGetWeapon(index, out _) || inventory.GetEquippedIndex() == index) return;
        WeaponBehaviour weapon = inventory.Equip(index);
        if (weapon != null) characterAnimator.runtimeAnimatorController = weapon.GetAnimatorController();
        if (HasAuthority) RestoreActiveWeaponAmmunition();
    }

    // Private simulation state is separate from replicated variables and must follow authority.
    protected override void WriteHandoffState(NetWriter writer)
    {
        writer.Write(movementInput.x);
        writer.Write(movementInput.y);
        writer.Write(movementYaw);
        writer.Write(running);
        writer.Write(jumpQueued);
        writer.Write(verticalSpeed);
        writer.Write(weapons.Length);
        for (int i = 0; i < weapons.Length; i++)
        {
            writer.Write(weapons[i].CaptureAmmunition());
            writer.Write(Mathf.Max(0f, nextShotTime[i] - Time.time));
            writer.Write(Mathf.Max(0f, reloadUntil[i] - Time.time));
        }
    }

    protected override void ReadHandoffState(NetReader reader)
    {
        movementInput = new Vector2(reader.ReadFloat(), reader.ReadFloat());
        movementYaw = reader.ReadFloat();
        running = reader.ReadBool();
        jumpQueued = reader.ReadBool();
        verticalSpeed = reader.ReadFloat();
        int count = reader.ReadInt();
        if (count != weapons.Length) throw new System.InvalidOperationException("Weapon count differs across workers");
        for (int i = 0; i < count; i++)
        {
            handoffAmmunition[i] = reader.ReadInt();
            nextShotTime[i] = Time.time + reader.ReadFloat();
            reloadUntil[i] = Time.time + reader.ReadFloat();
        }
        RestoreActiveWeaponAmmunition();
    }

    private void RestoreActiveWeaponAmmunition()
    {
        for (int i = 0; i < weapons.Length; i++)
            if (handoffAmmunition[i] >= 0 && weapons[i].RestoreAmmunition(handoffAmmunition[i]))
                handoffAmmunition[i] = -1;
    }

    private static void SetLayerRecursively(GameObject value, int layer)
    {
        value.layer = layer;
        foreach (Transform child in value.transform) SetLayerRecursively(child.gameObject, layer);
    }

    private sealed class LocalGameModeService : IGameModeService
    {
        private readonly CharacterBehaviour localCharacter;
        public LocalGameModeService(CharacterBehaviour value) => localCharacter = value;
        public CharacterBehaviour GetPlayerCharacter() => localCharacter;
    }
}
