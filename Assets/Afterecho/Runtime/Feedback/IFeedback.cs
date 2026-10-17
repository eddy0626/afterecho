namespace Afterecho.Feedback
{
    // A presentation-only reaction. Implementations must never change score, HP or judgement.
    public interface IFeedback
    {
        void Play();

        bool IsPlaying { get; }

        // Returns targets to their rest pose (pause, retry, menu).
        void Stop();
    }
}
