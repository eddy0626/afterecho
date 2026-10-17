using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace Afterecho.Feedback
{
    // Single entry point for runner presentation feedbacks. Views ask for a FeedbackId;
    // the bindings below decide which MMF_Player answers, so effects are tuned in the
    // Inspector instead of in code.
    public sealed class RunnerFeedbackPlayer : MonoBehaviour
    {
        [Serializable]
        public struct Binding
        {
            public FeedbackId id;
            public FeedbackChannel channel;
            public MMF_Player player;
        }

        [SerializeField] Binding[] bindings = Array.Empty<Binding>();

        readonly Dictionary<FeedbackId, Entry> entries = new Dictionary<FeedbackId, Entry>();
        bool bindingsLoaded;

        public bool ReducedMotion { get; set; }

        public bool Has(FeedbackId id)
        {
            LoadBindings();
            return entries.ContainsKey(id);
        }

        // Used while code-built effects are migrated to bound MMF_Players:
        // a fallback is ignored when the scene already binds the id.
        public void RegisterFallback(FeedbackId id, FeedbackChannel channel, IFeedback fallback)
        {
            if (fallback == null)
                throw new ArgumentNullException(nameof(fallback));
            LoadBindings();
            if (!entries.ContainsKey(id))
                entries.Add(id, new Entry(fallback, channel));
        }

        // Returns false when nothing played (unknown id, or filtered by Reduced Motion).
        public bool Play(FeedbackId id)
        {
            LoadBindings();
            if (!entries.TryGetValue(id, out var entry))
                return false;
            if (ReducedMotion && IsMotion(entry.Channel))
                return false;
            entry.Feedback.Play();
            return true;
        }

        public void StopAll()
        {
            foreach (var entry in entries.Values)
                entry.Feedback.Stop();
        }

        static bool IsMotion(FeedbackChannel channel)
        {
            return channel == FeedbackChannel.Motion || channel == FeedbackChannel.Flash;
        }

        // Lazy so the result does not depend on Awake order between this and RunnerView.
        void LoadBindings()
        {
            if (bindingsLoaded)
                return;
            bindingsLoaded = true;
            foreach (var binding in bindings)
            {
                if (binding.player != null)
                    entries[binding.id] = new Entry(new FeelFeedback(binding.player), binding.channel);
            }
        }

        readonly struct Entry
        {
            public readonly IFeedback Feedback;
            public readonly FeedbackChannel Channel;

            public Entry(IFeedback feedback, FeedbackChannel channel)
            {
                Feedback = feedback;
                Channel = channel;
            }
        }
    }
}
