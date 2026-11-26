namespace Madlands.ClientCore
{
    /// <summary>How hard a creature or quest is for the player by its level: the classic "con" colors.</summary>
    public enum LevelDifficulty
    {
        /// <summary>Gray: so far below the player that a kill gives no experience (a quest: much less).</summary>
        Trivial = 0,
        /// <summary>Green: below the player, still worth experience.</summary>
        Easy = 1,
        /// <summary>Yellow: within two levels of the player.</summary>
        Even = 2,
        /// <summary>Orange: three or four levels above the player.</summary>
        Hard = 3,
        /// <summary>Red: five or more levels above the player.</summary>
        VeryHard = 4,
    }

    /// <summary>
    /// The level thresholds of <see cref="LevelDifficulty"/>, for the target frame's level and quest levels
    /// (quest log, tracker, world map). Display only: the server decides the experience.
    /// </summary>
    public static class Difficulty
    {
        /// <summary>This many levels above the player or more: red.</summary>
        public const int VeryHardFrom = 5;
        /// <summary>This many levels above the player or more: orange.</summary>
        public const int HardFrom = 3;
        /// <summary>Up to this many levels below the player (and up to two above): yellow.</summary>
        public const int EvenWithin = 2;
        /// <summary>Quests more than this many levels below the player are gray: their experience falls off.</summary>
        public const int QuestTrivialBelow = 5;

        /// <summary>A creature (or any unit) by its level; gray when a kill gives no experience.</summary>
        public static LevelDifficulty OfCreature(int creatureLevel, int playerLevel) =>
            Classify(creatureLevel - playerLevel, creatureLevel <= GrayLevel(playerLevel));

        /// <summary>A quest by its level.</summary>
        public static LevelDifficulty OfQuest(int questLevel, int playerLevel) =>
            Classify(questLevel - playerLevel, playerLevel - questLevel > QuestTrivialBelow);

        /// <summary>
        /// Creatures at or below this level give no experience. The server's rule (<c>Experience.GrayLevel</c> in
        /// Madlands.Server): ClientCore cannot reference the server, so a test keeps the two equal.
        /// </summary>
        public static int GrayLevel(int playerLevel) => playerLevel <= 5 ? 0 : playerLevel - 5 - playerLevel / 10;

        private static LevelDifficulty Classify(int levelsAbove, bool trivial)
        {
            if (levelsAbove >= VeryHardFrom) return LevelDifficulty.VeryHard;
            if (levelsAbove >= HardFrom) return LevelDifficulty.Hard;
            if (levelsAbove >= -EvenWithin) return LevelDifficulty.Even;
            return trivial ? LevelDifficulty.Trivial : LevelDifficulty.Easy;
        }
    }
}
