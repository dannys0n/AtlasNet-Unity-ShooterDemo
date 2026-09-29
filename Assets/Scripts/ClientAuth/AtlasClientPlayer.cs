using AtlasNet;
using InfimaGames.LowPolyShooterPack;
using UnityEngine;
using UnityEngine.InputSystem;

// Keep the imported Character responsible for its own weapons and animations.
// This component controls who may run that character and shares its presentation.
public sealed class AtlasClientPlayer : NetworkBehaviour
{
    private const int CharacterLayer = 10;

    [SerializeField] private GameObject hudPrefab;
    [SerializeField] private float walkingSpeed = 4f;
    [SerializeField] private float runningSpeed = 6.8f;
    [SerializeField] private float jumpSpeed = 5f;

    private readonly NetworkVariable<Quaternion> lookPitch = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<int> equippedWeapon = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CharacterBehaviour character;
    private AtlasClientCameraLook cameraLook;
    private PlayerInput playerInput;
    private CharacterController controller;
    private CharacterKinematics kinematics;
    private InventoryBehaviour inventory;
    private Animator characterAnimator;
    private AtlasClientWeapon[] weapons;
    private GameObject hud;
    private float verticalVelocity;
    private int actionsLayer;
    private bool wasInspecting;

    private void Awake()
    {
        character = GetComponent<CharacterBehaviour>();
        cameraLook = GetComponentInChildren<AtlasClientCameraLook>(true);
        playerInput = GetComponent<PlayerInput>();
        controller = GetComponent<CharacterController>();
        kinematics = GetComponent<CharacterKinematics>();
        inventory = character.GetInventory();
        characterAnimator = GetComponentInChildren<Animator>(true);
        weapons = GetComponentsInChildren<AtlasClientWeapon>(true);
        actionsLayer = characterAnimator.GetLayerIndex("Layer Actions");
    }

    public override void OnNetworkSpawn()
    {
        SetLocalControl(IsOwner);
        if (IsOwner)
        {
            ServiceLocator.Current.Unregister<IGameModeService>();
            ServiceLocator.Current.Register<IGameModeService>(new LocalGameModeService(character));
            if (hudPrefab != null) hud = Instantiate(hudPrefab);
        }
        else
        {
            // The source first-person arms use an overlay layer that draws through walls.
            SetLayerRecursively(gameObject, CharacterLayer);
            ApplyEquippedWeapon(equippedWeapon.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        SetLocalControl(false);
        if (hud != null) Destroy(hud);
        if (IsOwner && ServiceLocator.Current != null)
        {
            ServiceLocator.Current.Unregister<IGameModeService>();
            ServiceLocator.Current.Register<IGameModeService>(new GameModeService());
        }
    }

    // Apply the weapon before NetworkAnimator reads its state during a late-join spawn.
    protected override void ReadExtraSnapshot(NetReader reader)
    {
        if (!IsOwner) ApplyEquippedWeapon(equippedWeapon.Value);
    }

    public override void OnNetworkTick()
    {
        if (!IsOwner) return;
        lookPitch.Value = cameraLook.transform.localRotation;
        int index = inventory.GetEquippedIndex();
        if (index >= 0) equippedWeapon.Value = index;
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

        MoveCharacter();
        bool inspecting = actionsLayer >= 0 &&
            (characterAnimator.GetCurrentAnimatorStateInfo(actionsLayer).IsName("Inspect") ||
             characterAnimator.IsInTransition(actionsLayer) &&
             characterAnimator.GetNextAnimatorStateInfo(actionsLayer).IsName("Inspect"));
        if (inspecting && !wasInspecting) PlayInspectRpc(inventory.GetEquippedIndex());
        wasInspecting = inspecting;
    }

    private void MoveCharacter()
    {
        Vector2 input = Vector2.ClampMagnitude(character.GetInputMovement(), 1f);
        float speed = character.IsRunning() ? runningSpeed : walkingSpeed;
        Vector3 movement = transform.TransformDirection(new Vector3(input.x, 0f, input.y) * speed);
        if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        if (controller.isGrounded && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            verticalVelocity = jumpSpeed;
        verticalVelocity += Physics.gravity.y * Time.deltaTime;
        movement.y = verticalVelocity;
        controller.Move(movement * Time.deltaTime);
    }

    public void ShareFire(int weapon, Vector3 origin, Vector3 direction)
    {
        if (IsOwner)
            PlayFireRpc(weapon, origin, direction, transform.rotation, cameraLook.transform.localRotation);
    }

    public void ShareReload(int weapon, bool hasAmmunition)
    {
        if (IsOwner) PlayReloadRpc(weapon, hasAmmunition);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayFireRpc(int weaponIndex, Vector3 origin, Vector3 direction,
        Quaternion rootRotation, Quaternion pitch)
    {
        if (!IsClient || IsOwner || !TryGetWeapon(weaponIndex, out var remoteWeapon)) return;
        transform.rotation = rootRotation;
        cameraLook.transform.localRotation = pitch;
        ApplyEquippedWeapon(weaponIndex);
        kinematics?.Compute();
        characterAnimator.CrossFade("Fire", 0.05f, characterAnimator.GetLayerIndex("Layer Overlay"), 0f);
        remoteWeapon.PlayRemoteFire(origin, direction);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayReloadRpc(int weaponIndex, bool hasAmmunition)
    {
        if (!IsClient || IsOwner || !TryGetWeapon(weaponIndex, out var remoteWeapon)) return;
        ApplyEquippedWeapon(weaponIndex);
        characterAnimator.Play(hasAmmunition ? "Reload" : "Reload Empty", actionsLayer, 0f);
        remoteWeapon.PlayRemoteReload(hasAmmunition);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayInspectRpc(int weaponIndex)
    {
        if (!IsClient || IsOwner) return;
        ApplyEquippedWeapon(weaponIndex);
        characterAnimator.CrossFade("Inspect", 0f, actionsLayer, 0f);
    }

    private bool TryGetWeapon(int index, out AtlasClientWeapon weapon)
    {
        weapon = index >= 0 && index < weapons.Length ? weapons[index] : null;
        return weapon != null;
    }

    private void ApplyEquippedWeapon(int index)
    {
        if (!TryGetWeapon(index, out _) || inventory.GetEquippedIndex() == index) return;
        WeaponBehaviour weapon = inventory.Equip(index);
        if (weapon != null) characterAnimator.runtimeAnimatorController = weapon.GetAnimatorController();
    }

    private void SetLocalControl(bool active)
    {
        character.enabled = active;
        cameraLook.enabled = active;
        playerInput.enabled = active;
        foreach (Camera playerCamera in GetComponentsInChildren<Camera>(true))
        {
            playerCamera.enabled = active;
            AudioListener listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }
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
