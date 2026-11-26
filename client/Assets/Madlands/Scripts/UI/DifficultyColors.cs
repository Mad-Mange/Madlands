using Madlands.ClientCore;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// The colors of <see cref="LevelDifficulty"/> (thresholds in <see cref="Difficulty"/>): the target frame's
    /// level, quest levels in the quest log, the tracker and on the world map.
    /// </summary>
    public static class DifficultyColors
    {
        private static readonly Color Trivial = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color Easy = new Color(0.35f, 0.9f, 0.35f);
        private static readonly Color Even = new Color(1f, 0.85f, 0.3f);
        private static readonly Color Hard = new Color(1f, 0.55f, 0.2f);
        private static readonly Color VeryHard = new Color(1f, 0.25f, 0.2f);

        public static Color Of(LevelDifficulty difficulty)
        {
            switch (difficulty)
            {
                case LevelDifficulty.Trivial: return Trivial;
                case LevelDifficulty.Easy: return Easy;
                case LevelDifficulty.Hard: return Hard;
                case LevelDifficulty.VeryHard: return VeryHard;
                default: return Even;
            }
        }
    }
}
