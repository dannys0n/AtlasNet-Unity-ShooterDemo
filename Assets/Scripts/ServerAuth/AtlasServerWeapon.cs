using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    // Retain the imported weapon setup; the server owns ammo and hit resolution.
    public sealed class AtlasServerWeapon : AtlasClientWeapon
    {
        private bool initialized;

        protected override void Start()
        {
            if (initialized) return;
            base.Start();
            initialized = true;
        }

        // Equipping and firing can arrive in the same server frame.
        public void EnsureInitialized()
        {
            if (!initialized && gameObject.activeInHierarchy) Start();
        }

        public override void Fire(float spreadMultiplier = 1f)
        {
            AtlasServerPlayer player = GetComponentInParent<AtlasServerPlayer>();
            if (player == null || !player.IsOwner) return;
            EnsureInitialized();

            GetAnimator().Play("Fire", 0, 0f);
            GetAttachmentManager().GetEquippedMuzzle()?.Effect();
            Transform aim = player.AimTransform;
            player.RequestFire(this, aim.position, aim.forward);
        }

        public override void Reload()
        {
            AtlasServerPlayer player = GetComponentInParent<AtlasServerPlayer>();
            if (player == null || !player.IsOwner) return;
            EnsureInitialized();
            GetAnimator().Play(HasAmmunition() ? "Reload" : "Reload Empty", 0, 0f);
            player.RequestReload(this);
        }

        // Animation events on a client cannot grant ammunition.
        public override void FillAmmunition(int amount) { }

        public void SetAuthoritativeAmmunition(int amount)
        {
            EnsureInitialized();
            int difference = Mathf.Clamp(amount, 0, GetAmmunitionTotal()) - GetAmmunitionCurrent();
            if (difference != 0) base.FillAmmunition(difference);
        }

        public int CaptureAmmunition() => initialized ? GetAmmunitionCurrent() : -1;

        public bool RestoreAmmunition(int amount)
        {
            if (!gameObject.activeInHierarchy) return false;
            SetAuthoritativeAmmunition(amount);
            return true;
        }

        public void PlayAcceptedMuzzle()
        {
            GetAnimator().Play("Fire", 0, 0f);
            GetAttachmentManager().GetEquippedMuzzle()?.Effect();
        }

        public void PlayAcceptedReload(bool hadAmmunition)
        {
            GetAnimator().Play(hadAmmunition ? "Reload" : "Reload Empty", 0, 0f);
        }
    }
}
