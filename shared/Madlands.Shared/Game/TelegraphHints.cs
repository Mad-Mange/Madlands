namespace Madlands.Shared.Game
{
    /// <summary>
    /// Client-only presentation hints for content whose effects are not sent over the protocol.
    /// ContentTests checks the radius against the authoritative spell data.
    /// </summary>
    public static class TelegraphHints
    {
        public const uint CinderQuake = 914;
        public const float CinderQuakeRadius = 12f;
    }
}
