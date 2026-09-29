using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    public sealed class AtlasClientCameraLook : MonoBehaviour
    {
        [SerializeField] private Vector2 sensitivity = Vector2.one;
        [SerializeField] private Vector2 yClamp = new(-60f, 60f);
        [SerializeField] private bool smooth;
        [SerializeField] private float interpolationSpeed = 25f;

        private CharacterBehaviour character;
        private Quaternion characterRotation;
        private Quaternion cameraRotation;

        private void Awake()
        {
            character = GetComponentInParent<CharacterBehaviour>();
        }

        private void Start()
        {
            characterRotation = character.transform.localRotation;
            cameraRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            Vector2 input = character.IsCursorLocked()
                ? character.GetInputLook() * sensitivity
                : default;

            Quaternion yaw = Quaternion.Euler(0f, input.x, 0f);
            Quaternion pitch = Quaternion.Euler(-input.y, 0f, 0f);

            cameraRotation *= pitch;
            characterRotation *= yaw;

            if (smooth)
            {
                transform.localRotation = Quaternion.Slerp(
                    transform.localRotation,
                    Clamp(cameraRotation),
                    Time.deltaTime * interpolationSpeed);
                character.transform.rotation = Quaternion.Slerp(
                    character.transform.rotation,
                    characterRotation,
                    Time.deltaTime * interpolationSpeed);
            }
            else
            {
                transform.localRotation = Clamp(transform.localRotation * pitch);
                character.transform.rotation *= yaw;
            }
        }

        private Quaternion Clamp(Quaternion rotation)
        {
            rotation.x /= rotation.w;
            rotation.y /= rotation.w;
            rotation.z /= rotation.w;
            rotation.w = 1f;

            float pitch = 2f * Mathf.Rad2Deg * Mathf.Atan(rotation.x);
            pitch = Mathf.Clamp(pitch, yClamp.x, yClamp.y);
            rotation.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * pitch);
            return rotation;
        }
    }
}
