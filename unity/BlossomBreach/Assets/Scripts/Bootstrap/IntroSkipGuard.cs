namespace BlossomBreach
{
    /// <summary>Rejects launch-time trigger noise until the player has aimed away from the skip target.</summary>
    public sealed class IntroSkipGuard
    {
        private readonly float minimumPlaybackSeconds;
        private bool releaseObserved;
        private bool pointerSeenOutside;

        public IntroSkipGuard(float minimumPlaybackSeconds)
        {
            this.minimumPlaybackSeconds = minimumPlaybackSeconds;
        }

        public bool TryAccept(
            float playbackSeconds,
            bool triggerHeld,
            bool triggerPressed,
            bool pointerInsideTarget)
        {
            if (!triggerHeld)
            {
                releaseObserved = true;
            }

            if (!pointerInsideTarget)
            {
                pointerSeenOutside = true;
            }

            return playbackSeconds >= minimumPlaybackSeconds &&
                   releaseObserved &&
                   pointerSeenOutside &&
                   triggerPressed &&
                   pointerInsideTarget;
        }
    }
}
