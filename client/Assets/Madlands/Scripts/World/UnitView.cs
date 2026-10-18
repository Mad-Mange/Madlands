using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// The visible body of a unit (remote or local): its model from the displayId, a trigger collider
    /// for mouse selection, and its animations (movement, attacks, casts, hits, death). Displays without
    /// a model fall back to small procedural reactions (hit punch, melee lunge, falling over). Pure
    /// presentation: all state comes from ClientCore.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float HitPunchSeconds = 0.18f;
        private const float LungeSeconds = 0.25f;
        /// <summary>How far the body turns toward a diagonal run (WoW turns the legs, we turn the model).</summary>
        private const float DiagonalTurn = 40f;

        private Transform _visual;
        private DisplayModel _model;
        private UnitAnimator _animator;
        private Transform _castGlow;
        private Transform _mainHand;
        private Transform _offHand;
        private Transform _sparkle;
        private uint _mainDisplay;
        private uint _offDisplay;
        private float _hitAt = -10f;
        private float _lungeAt = -10f;
        private bool _dead;
        private bool _deadKnown;
        private float _deadBlend;
        private bool _casting;
        private bool _airborne;
        private bool _sitting;
        private float _turn;
        private float _turnTarget;

        public EntityGuid Guid { get; private set; }
        public DisplayShape Shape { get; private set; }
        public bool IsLocal { get; private set; }
        public Vector3 HeadPosition => transform.position + Vector3.up * (Shape.Height + 0.35f);
        public Vector3 ChestPosition => transform.position + Vector3.up * (Shape.Height * (_dead ? 0.2f : 0.6f));

        public static UnitView Attach(GameObject go, EntityGuid guid, uint displayId, bool isLocal)
        {
            var view = go.AddComponent<UnitView>();
            view.Guid = guid;
            view.IsLocal = isLocal;
            view._visual = new GameObject("Visual").transform;
            view._visual.SetParent(go.transform, false);
            view.Shape = EntityVisuals.Create(view._visual, displayId, out view._model);
            if (view._model != null && view._model.Animator != null && view._model.Clips.Count > 0)
                view._animator = new UnitAnimator(view._model.Animator, view._model.Clips, view._model.Humanoid, view._model.WalkSpeed, view._model.RunSpeed);

            // Click target for selection. A trigger: never blocks movement or the camera.
            var collider = go.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            collider.height = view.Shape.Height;
            collider.radius = Mathf.Max(0.4f, view.Shape.Radius);
            collider.center = Vector3.up * (view.Shape.Height * 0.5f);
            return view;
        }

        private void OnDestroy() => _animator?.Destroy();

        public void PlayHit()
        {
            _hitAt = Time.time;
            // A flinch never cuts off a swing or a cast.
            if (_animator != null && !_animator.ActionPlaying && !_dead) _animator.PlayAction("Hit");
        }

        /// <summary>A melee swing: the attack animation for what the unit holds.</summary>
        public void PlayLunge()
        {
            _lungeAt = Time.time;
            _sitting = false;
            if (_animator == null || _dead) return;
            string clip = !_animator.Humanoid ? "Attack"
                : _mainDisplay == 0 ? "AttackUnarmed"
                : ItemVisuals.IsTwoHanded(_mainDisplay) ? "Attack2H"
                : "Attack1H";
            _animator.PlayAction(clip);
        }

        /// <summary>A spell leaves the caster's hands.</summary>
        public void PlayCastRelease()
        {
            _sitting = false;
            if (_animator != null && !_dead) _animator.PlayAction("CastRelease");
        }

        /// <summary>Sits down (eating and drinking) until the unit moves or acts.</summary>
        public void SitDown() => _sitting = true;

        public void SetDead(bool dead)
        {
            if (_deadKnown && dead == _dead) return;
            bool first = !_deadKnown;
            _deadKnown = true;
            _dead = dead;
            if (_animator == null) return;
            if (dead) _animator.PlayDeath(instantly: first);
            else _animator.StopDeath();
            _sitting = false;
        }

        /// <summary>Picks the movement animation from the unit's (interpolated) movement state.</summary>
        public void SetMovement(MovementFlags flags, Vec3 velocity)
        {
            bool airborne = (flags & MovementFlags.Airborne) != 0;
            if (airborne && !_airborne && velocity.Y > 0f) _animator?.PlayJump();
            _airborne = airborne;
            if (_animator == null) return;

            bool forward = (flags & MovementFlags.Forward) != 0;
            bool backward = (flags & MovementFlags.Backward) != 0;
            bool left = (flags & MovementFlags.StrafeLeft) != 0;
            bool right = (flags & MovementFlags.StrafeRight) != 0;
            if (forward && backward) forward = backward = false;
            if (left && right) left = right = false;
            bool walking = (flags & MovementFlags.Walking) != 0;
            float speed = new Vector2(velocity.X, velocity.Z).magnitude;
            if (forward || backward || left || right) _sitting = false;

            Locomotion loco;
            _turnTarget = 0f;
            if (airborne) loco = Locomotion.Fall;
            else if (forward)
            {
                loco = walking ? Locomotion.Walk : Locomotion.Run;
                _turnTarget = left ? -DiagonalTurn : right ? DiagonalTurn : 0f;
            }
            else if (backward)
            {
                loco = Locomotion.RunBack;
                _turnTarget = left ? DiagonalTurn : right ? -DiagonalTurn : 0f;
            }
            else if (left) loco = walking ? Locomotion.Walk : Locomotion.StrafeLeft;
            else if (right) loco = walking ? Locomotion.Walk : Locomotion.StrafeRight;
            else loco = _sitting ? Locomotion.Sit : Locomotion.Idle;
            _animator.SetLocomotion(loco, speed);
        }

        /// <summary>Shows the held weapon and shield (display ids from the unit's values).</summary>
        public void SetEquipment(uint mainHand, uint offHand)
        {
            if (!Shape.Hands) return;
            if (mainHand != _mainDisplay) Hold(ref _mainHand, _mainDisplay = mainHand, right: true);
            if (offHand != _offDisplay) Hold(ref _offHand, _offDisplay = offHand, right: false);
        }

        private void Hold(ref Transform hand, uint displayId, bool right)
        {
            if (hand != null) Destroy(hand.gameObject);
            hand = null;
            if (displayId == 0) return;
            hand = new GameObject(right ? "MainHand" : "OffHand").transform;
            var grip = _model == null ? null : right ? _model.MainHandGrip : _model.OffHandGrip;
            if (grip != null)
            {
                // In the fist: follows every swing of the animation.
                hand.SetParent(grip, false);
                hand.localRotation = right ? _model.MainHandOffset : _model.OffHandOffset;
            }
            else
            {
                // Inside the visual, so the weapon falls over with the body.
                hand.SetParent(_visual, false);
                hand.localPosition = new Vector3(right ? Shape.Radius + 0.08f : -(Shape.Radius + 0.08f), 0.95f, 0.12f);
                hand.localRotation = Quaternion.Euler(right ? 20f : 0f, 0f, 0f);
            }
            ItemVisuals.Create(hand, displayId);
        }

        /// <summary>A slowly turning golden mote over a corpse the local player may loot.</summary>
        public void SetLootable(bool lootable)
        {
            if (lootable == (_sparkle != null)) return;
            if (!lootable)
            {
                Destroy(_sparkle.gameObject);
                _sparkle = null;
                return;
            }
            _sparkle = EntityVisuals.Glow(transform, Vector3.up * (Shape.Height * 0.45f + 0.3f), Vector3.one * 0.22f, new Color(1f, 0.85f, 0.3f));
            _sparkle.localRotation = Quaternion.Euler(45f, 0f, 45f);
            _sparkle.gameObject.AddComponent<Spinner>();
        }

        /// <summary>Shows (school given) or hides a glow above the unit while it casts.</summary>
        public void SetCasting(SpellSchool? school)
        {
            bool casting = school != null;
            if (casting != _casting && _animator != null)
            {
                if (casting) _animator.PlayAction("CastLoop", loop: true);
                else _animator.StopAction("CastLoop");
            }
            if (casting) _sitting = false;
            _casting = casting;
            if (school == null)
            {
                if (_castGlow != null) Destroy(_castGlow.gameObject);
                _castGlow = null;
                return;
            }
            if (_castGlow != null) return;
            _castGlow = EntityVisuals.Glow(transform, Vector3.up * (Shape.Height + 0.15f), Vector3.one * 0.3f, SchoolColors.Get(school.Value), PrimitiveType.Sphere);
        }

        private void LateUpdate()
        {
            if (_castGlow != null)
            {
                float pulse = 0.25f + Mathf.Sin(Time.time * 10f) * 0.06f;
                _castGlow.localScale = Vector3.one * pulse;
            }

            if (_animator != null)
            {
                _animator.Update(Time.deltaTime);
                _turn = Mathf.MoveTowards(_turn, _dead ? 0f : _turnTarget, Time.deltaTime * 240f);
                _visual.localRotation = Quaternion.Euler(0f, _turn, 0f);
                return;
            }

            // No model: squash on hits, lunge on swings, fall over when dead.
            float hit = Mathf.Clamp01((Time.time - _hitAt) / HitPunchSeconds);
            float punch = hit < 1f ? Mathf.Sin(hit * Mathf.PI) * 0.12f : 0f;
            float lunge = Mathf.Clamp01((Time.time - _lungeAt) / LungeSeconds);
            float forward = lunge < 1f ? Mathf.Sin(lunge * Mathf.PI) * 0.35f : 0f;

            _deadBlend = Mathf.MoveTowards(_deadBlend, _dead ? 1f : 0f, Time.deltaTime * 3f);
            _visual.localScale = new Vector3(1f + punch, 1f - punch * 0.5f, 1f + punch);
            _visual.localPosition = new Vector3(0f, -_deadBlend * Shape.Radius * 0.3f, forward);
            _visual.localRotation = Quaternion.Euler(0f, 0f, _deadBlend * 90f);
        }
    }
}
