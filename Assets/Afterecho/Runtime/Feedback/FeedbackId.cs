namespace Afterecho.Feedback
{
    // What the game wants to show. Which effect answers is decided by RunnerFeedbackPlayer's bindings.
    // Values are serialized in scenes: append new ids, never renumber existing ones.
    public enum FeedbackId
    {
        HitGood = 0,
        HitPerfect = 1,
        HpLoss = 2,
        Fall = 3,
        BoostStart = 4,
        JudgementText = 5,
        Stumble = 6,
        ResultShow = 7,
        ComboUp = 8,
        ComboBreak = 9,
        Beat = 10,
        JudgementPerfect = 11,
        JudgementMiss = 12,
    }

    // Reduced Motion turns off Motion and Flash but keeps Audio and Text.
    // Keep one channel per MMF_Player so a sound is never muted along with a squash.
    public enum FeedbackChannel
    {
        Motion = 0,
        Flash = 1,
        Audio = 2,
        Text = 3,
    }
}
