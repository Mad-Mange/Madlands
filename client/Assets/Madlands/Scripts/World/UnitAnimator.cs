using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Madlands.Client
{
    /// <summary>Base movement animation, picked each frame from the unit's movement.</summary>
    public enum Locomotion
    {
        Idle,
        Walk,
        Run,
        RunBack,
        StrafeLeft,
        StrafeRight,
        Fall,
        Sit,
    }

    /// <summary>
    /// Plays a model's clips (art/README.md names them) with the Playables API, no Animator Controller:
    /// layer 0 cross-fades the locomotion loops, layer 1 plays actions (attacks, casts, hits; upper body
    /// only on humanoids so the legs keep running), layer 2 plays full-body moments (jump, death).
    /// A missing clip simply never plays.
    /// </summary>
    public sealed class UnitAnimator
    {
        private const float FadeSpeed = 1f / 0.15f;

        /// <summary>Ground speed in m/s at which each locomotion clip looks right at normal speed.</summary>
        private static readonly float[] NominalSpeed = { 0f, 1.7f, 6.2f, 3.6f, 5.6f, 5.6f, 0f, 0f };

        private readonly PlayableGraph _graph;
        private readonly AnimationLayerMixerPlayable _layers;
        private readonly AnimationMixerPlayable _locomotion;
        private readonly AnimationClipPlayable[] _locoClips;
        private readonly float[] _locoWeights;
        private readonly Slot _action;
        private readonly Slot _fullBody;
        private Locomotion _current;
        private float _speedScale = 1f;

        /// <summary>One layer that plays at most one clip at a time, fading the layer in and out.</summary>
        private sealed class Slot
        {
            public AnimationMixerPlayable Mixer;
            public readonly Dictionary<string, int> Inputs = new Dictionary<string, int>();
            public readonly List<AnimationClipPlayable> Clips = new List<AnimationClipPlayable>();
            public int LayerIndex;
            public int Playing = -1;
            public bool Loop;
            public bool Hold;
            public float Weight;
        }

        public bool Humanoid { get; }

        public UnitAnimator(Animator animator, IReadOnlyDictionary<string, AnimationClip> clips, bool humanoid)
        {
            Humanoid = humanoid;
            _graph = PlayableGraph.Create("Unit " + animator.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
            _layers = AnimationLayerMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_layers);

            var names = System.Enum.GetNames(typeof(Locomotion));
            _locomotion = AnimationMixerPlayable.Create(_graph, names.Length);
            _locoClips = new AnimationClipPlayable[names.Length];
            _locoWeights = new float[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                if (!clips.TryGetValue(names[i], out var clip)) continue;
                _locoClips[i] = AnimationClipPlayable.Create(_graph, clip);
                _graph.Connect(_locoClips[i], 0, _locomotion, i);
            }
            _locoWeights[0] = 1f;
            _locomotion.SetInputWeight(0, 1f);
            _graph.Connect(_locomotion, 0, _layers, 0);
            _layers.SetInputWeight(0, 1f);

            _action = CreateSlot(clips, 1, "Attack1H", "Attack2H", "AttackUnarmed", "Attack", "CastLoop", "CastRelease", "Hit");
            _fullBody = CreateSlot(clips, 2, "Jump", "Death");
            if (humanoid) _layers.SetLayerMaskFromAvatarMask(1, UpperBody);
            _graph.Play();
        }

        private static AvatarMask _upperBody;

        private static AvatarMask UpperBody
        {
            get
            {
                if (_upperBody != null) return _upperBody;
                _upperBody = new AvatarMask();
                for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                    _upperBody.SetHumanoidBodyPartActive(part,
                        part != AvatarMaskBodyPart.Root && part != AvatarMaskBodyPart.LeftLeg && part != AvatarMaskBodyPart.RightLeg &&
                        part != AvatarMaskBodyPart.LeftFootIK && part != AvatarMaskBodyPart.RightFootIK);
                return _upperBody;
            }
        }

        private Slot CreateSlot(IReadOnlyDictionary<string, AnimationClip> clips, int layer, params string[] names)
        {
            var slot = new Slot { LayerIndex = layer };
            slot.Mixer = AnimationMixerPlayable.Create(_graph, names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                if (!clips.TryGetValue(names[i], out var clip)) continue;
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.Pause();
                _graph.Connect(playable, 0, slot.Mixer, i);
                slot.Inputs[names[i]] = i;
                while (slot.Clips.Count < i) slot.Clips.Add(default);
                slot.Clips.Add(playable);
            }
            _graph.Connect(slot.Mixer, 0, _layers, layer);
            _layers.SetInputWeight(layer, 0f);
            return slot;
        }

        public bool Has(string clip) => _action.Inputs.ContainsKey(clip) || _fullBody.Inputs.ContainsKey(clip);

        /// <summary>An attack, cast or hit is playing on the action layer.</summary>
        public bool ActionPlaying => _action.Playing >= 0;

        /// <summary>Sets the base movement; <paramref name="groundSpeed"/> (m/s) scales the playback.</summary>
        public void SetLocomotion(Locomotion state, float groundSpeed)
        {
            if (_locoClips[(int)state].IsValid()) _current = state;
            else if (state == Locomotion.StrafeLeft || state == Locomotion.StrafeRight || state == Locomotion.RunBack) _current = Locomotion.Run;
            else _current = Locomotion.Idle;
            float nominal = NominalSpeed[(int)_current];
            _speedScale = nominal > 0f ? Mathf.Clamp(groundSpeed / nominal, 0.5f, 2f) : 1f;
        }

        /// <summary>Plays an action clip once (or loops it until <see cref="StopAction"/>).</summary>
        public bool PlayAction(string clip, bool loop = false) => Play(_action, clip, loop, hold: false);

        public void StopAction(string clip)
        {
            if (_action.Playing >= 0 && _action.Inputs.TryGetValue(clip, out int input) && input == _action.Playing) _action.Playing = -1;
        }

        public bool PlayJump() => Play(_fullBody, "Jump", loop: false, hold: false);

        /// <summary>Falls over and stays down; <paramref name="instantly"/> skips to the end (already dead on spawn).</summary>
        public bool PlayDeath(bool instantly)
        {
            if (!Play(_fullBody, "Death", loop: false, hold: true)) return false;
            if (instantly)
            {
                var playable = _fullBody.Clips[_fullBody.Playing];
                playable.SetTime(playable.GetAnimationClip().length);
                _fullBody.Weight = 1f;
            }
            _action.Playing = -1;
            return true;
        }

        public void StopDeath()
        {
            if (_fullBody.Playing >= 0 && _fullBody.Hold) _fullBody.Playing = -1;
        }

        private static bool Play(Slot slot, string clip, bool loop, bool hold)
        {
            if (!slot.Inputs.TryGetValue(clip, out int input)) return false;
            if (slot.Playing >= 0 && slot.Playing != input) slot.Clips[slot.Playing].Pause();
            slot.Playing = input;
            slot.Loop = loop;
            slot.Hold = hold;
            var playable = slot.Clips[input];
            playable.SetTime(0);
            playable.SetDone(false);
            playable.Play();
            for (int i = 0; i < slot.Clips.Count; i++)
                if (slot.Clips[i].IsValid()) slot.Mixer.SetInputWeight(i, i == input ? 1f : 0f);
            return true;
        }

        public void Update(float dt)
        {
            float step = dt * FadeSpeed;
            for (int i = 0; i < _locoClips.Length; i++)
            {
                if (!_locoClips[i].IsValid()) continue;
                _locoWeights[i] = Mathf.MoveTowards(_locoWeights[i], i == (int)_current ? 1f : 0f, step);
                _locomotion.SetInputWeight(i, _locoWeights[i]);
                _locoClips[i].SetSpeed(i == (int)_current ? _speedScale : 1f);
            }
            UpdateSlot(_action, step);
            UpdateSlot(_fullBody, step);
        }

        private void UpdateSlot(Slot slot, float step)
        {
            if (slot.Playing >= 0)
            {
                var playable = slot.Clips[slot.Playing];
                float length = playable.GetAnimationClip().length;
                double time = playable.GetTime();
                if (slot.Loop)
                {
                    if (time >= length) playable.SetTime(time % length);
                }
                else if (time >= length)
                {
                    if (slot.Hold) playable.SetTime(length);
                    else slot.Playing = -1;
                }
            }
            // Fade out a little before a one-shot ends, so it blends back into the locomotion.
            bool active = slot.Playing >= 0 && (slot.Loop || slot.Hold ||
                slot.Clips[slot.Playing].GetTime() < slot.Clips[slot.Playing].GetAnimationClip().length - 0.1f);
            slot.Weight = Mathf.MoveTowards(slot.Weight, active ? 1f : 0f, step);
            _layers.SetInputWeight(slot.LayerIndex, slot.Weight);
        }

        public void Destroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
