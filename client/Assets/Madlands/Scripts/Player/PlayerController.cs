using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Madlands.Client
{
    /// <summary>
    /// Local player, WoW-style controls. The client moves itself immediately (prediction); the
    /// server validates and may send a correction. Movement rules come from GameConstants so they
    /// match what the server and other clients expect.
    ///
    ///   W/S forward/back, A/D turn (strafe while right mouse is held), Q/E strafe,
    ///   both mouse buttons = forward, Space jump, Num Lock or R = autorun.
    ///   No air control: horizontal velocity is locked at take-off, like WoW.
    /// Speed follows the unit's SpeedPercent from the server (slows); rooted units can only turn,
    /// stunned and dead ones cannot act at all.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        private const float GroundSnapDistance = 0.7f;

        private CharacterController _controller;
        private NetworkController _network;
        private CameraController _camera;
        private readonly MovementSender _sender = new MovementSender();

        private MovementState _state;
        private float _yaw;
        private bool _airborne;
        private float _verticalVelocity;
        private Vector3 _airHorizontalVelocity;
        private bool _autorun;

        public MovementState State => _state;
        public UnitView View { get; private set; }
        public bool IsMovingForward => (_state.Flags & MovementFlags.Forward) != 0;

        public static PlayerController Create(MovementState spawn, NetworkController network, CameraController camera)
        {
            var go = new GameObject("LocalPlayer");
            go.transform.SetPositionAndRotation(spawn.Position.ToUnity(), Conversions.YawToRotation(spawn.Yaw));

            var controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);
            controller.stepOffset = 0.45f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.04f;

            var view = UnitView.Attach(go, network.Client.LocalGuid, EntityVisuals.PlayerDisplay, EntityVisuals.LocalPlayerColor, isLocal: true);

            var player = go.AddComponent<PlayerController>();
            player.View = view;
            player._network = network;
            player._camera = camera;
            player._state = spawn;
            player._yaw = spawn.Yaw;
            return player;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            // While a menu is open the character keeps simulating (falling, finishing a jump) but takes no input.
            var self = _network.Client.Self.Values;
            bool stunned = self.IsDead || self.Has(UnitFlags.Stunned);
            bool rooted = stunned || self.Has(UnitFlags.Rooted);
            var keyboard = InputGate.KeyboardBlocked || stunned ? null : Keyboard.current;
            var mouse = InputGate.Blocked || stunned ? null : Mouse.current;
            // Mouse buttons count only when the press started in the world (the camera tracks that).
            bool rmb = mouse != null && _camera != null && _camera.RightHeld;
            bool lmb = mouse != null && _camera != null && _camera.LeftHeld;
            bool Held(Key k) => keyboard != null && keyboard[k].isPressed;

            if (keyboard != null)
            {
                if (keyboard.numLockKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame) _autorun = !_autorun;
                else if (keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) _autorun = false;
            }

            bool forward = Held(Key.W) || (rmb && lmb) || _autorun;
            bool backward = Held(Key.S);
            bool strafeLeft = Held(Key.Q) || (rmb && Held(Key.A));
            bool strafeRight = Held(Key.E) || (rmb && Held(Key.D));
            bool turnLeft = !rmb && Held(Key.A);
            bool turnRight = !rmb && Held(Key.D);
            bool jump = keyboard != null && keyboard.spaceKey.wasPressedThisFrame && !rooted;
            if (rooted)
            {
                forward = backward = strafeLeft = strafeRight = false;
                _autorun = false;
            }

            // Facing: mouse look (right button) steers the character; keyboard turning turns
            // character and camera together.
            if (_camera != null && _camera.IsMouseSteering && !stunned)
            {
                _yaw = _camera.YawDegrees * Mathf.Deg2Rad;
            }
            else
            {
                float turn = ((turnRight ? 1f : 0f) - (turnLeft ? 1f : 0f)) * GameConstants.TurnRate * dt;
                if (turn != 0f)
                {
                    _yaw = MathUtil.WrapAngle(_yaw + turn);
                    if (_camera != null) _camera.AddYaw(turn * Mathf.Rad2Deg);
                }
            }

            var flags = MovementFlags.None;
            if (forward && !backward) flags |= MovementFlags.Forward;
            if (backward && !forward) flags |= MovementFlags.Backward;
            if (strafeLeft && !strafeRight) flags |= MovementFlags.StrafeLeft;
            if (strafeRight && !strafeLeft) flags |= MovementFlags.StrafeRight;
            if (turnLeft && !turnRight) flags |= MovementFlags.TurnLeft;
            if (turnRight && !turnLeft) flags |= MovementFlags.TurnRight;

            Vector3 horizontal;
            if (!_airborne)
            {
                horizontal = DesiredVelocity(flags);
                if (jump)
                {
                    _airborne = true;
                    _verticalVelocity = GameConstants.JumpVelocity;
                    _airHorizontalVelocity = horizontal;
                }
            }
            else
            {
                horizontal = _airHorizontalVelocity;
            }

            if (_airborne)
            {
                _verticalVelocity -= GameConstants.Gravity * dt;
                var collision = _controller.Move((horizontal + Vector3.up * _verticalVelocity) * dt);
                if ((collision & CollisionFlags.Below) != 0 && _verticalVelocity <= 0f)
                {
                    _airborne = false;
                    _verticalVelocity = 0f;
                }
            }
            else
            {
                _controller.Move(horizontal * dt);
                SnapToGround(horizontal);
            }

            transform.rotation = Conversions.YawToRotation(_yaw);
            if (_airborne) flags |= MovementFlags.Airborne;

            _state.Position = transform.position.ToShared();
            _state.Yaw = _yaw;
            _state.Flags = flags;
            _state.Velocity = _airborne
                ? new Vec3(horizontal.x, _verticalVelocity, horizontal.z)
                : new Vec3(horizontal.x, 0f, horizontal.z);

            var client = _network.Client;
            long now = client.LocalTimeMs;
            client.SetLocalMovement(_state);
            if (_sender.ShouldSend(_state, now))
            {
                client.SendMovement(_state);
                _sender.MarkSent(_state, now);
            }
        }

        private Vector3 DesiredVelocity(MovementFlags flags)
        {
            float z = ((flags & MovementFlags.Forward) != 0 ? 1f : 0f) - ((flags & MovementFlags.Backward) != 0 ? 1f : 0f);
            float x = ((flags & MovementFlags.StrafeRight) != 0 ? 1f : 0f) - ((flags & MovementFlags.StrafeLeft) != 0 ? 1f : 0f);
            if (x == 0f && z == 0f) return Vector3.zero;
            float speed = (z < 0f ? GameConstants.BackwardSpeed : GameConstants.RunSpeed) * _network.Client.Self.Values.SpeedPercent / 100f;
            var local = new Vector3(x, 0f, z).normalized * speed;
            return Conversions.YawToRotation(_yaw) * local;
        }

        /// <summary>Keeps the character glued to slopes; walking off a ledge starts a fall.</summary>
        private void SnapToGround(Vector3 horizontal)
        {
            var origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, 0.5f + GroundSnapDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                float drop = transform.position.y - hit.point.y;
                if (drop > 0.001f) _controller.Move(Vector3.down * (drop + 0.01f));
            }
            else
            {
                _airborne = true;
                _verticalVelocity = 0f;
                _airHorizontalVelocity = horizontal;
            }
        }

        /// <summary>Turns the character (and the camera with it) to face a point, e.g. the target.</summary>
        public void FaceTowards(Vector3 point)
        {
            var to = point - transform.position;
            if (to.x * to.x + to.z * to.z < 0.01f) return;
            float yaw = Mathf.Atan2(to.x, to.z);
            float delta = MathUtil.WrapAngle(yaw - _yaw);
            _yaw = yaw;
            if (_camera != null) _camera.AddYaw(delta * Mathf.Rad2Deg);
        }

        public void ApplyCorrection(MovementState state)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(state.Position.ToUnity(), Conversions.YawToRotation(state.Yaw));
            _controller.enabled = true;
            _state = state;
            // Keep the camera where it was relative to the character (teleports can turn us).
            if (_camera != null) _camera.AddYaw(MathUtil.WrapAngle(state.Yaw - _yaw) * Mathf.Rad2Deg);
            _yaw = state.Yaw;
            _airborne = false;
            _verticalVelocity = 0f;
            _sender.Reset();
            Debug.Log("[Net] Moved by the server to " + state.Position);
        }
    }
}
