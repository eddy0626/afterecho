using System;
using MoreMountains.Feedbacks;

namespace Afterecho.Feedback
{
    // Adapts a Feel MMF_Player to IFeedback.
    public sealed class FeelFeedback : IFeedback
    {
        readonly MMF_Player player;

        public FeelFeedback(MMF_Player player)
        {
            if (player == null)
                throw new ArgumentNullException(nameof(player));
            this.player = player;
        }

        // Always restart from the rest pose so judgements closer together than the effect
        // never start from another play's in-flight scale.
        public void Play()
        {
            if (player == null)
                return;
            player.StopFeedbacks();
            player.RestoreInitialValues();
            player.ResetAllCooldowns();
            player.PlayFeedbacks();
        }

        public void Stop()
        {
            if (player == null)
                return;
            player.StopFeedbacks();
            player.RestoreInitialValues();
        }
    }
}
