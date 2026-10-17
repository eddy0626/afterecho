using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace Afterecho.Feedback
{
    // Single entry point for runner presentation feedbacks. Views ask for a FeedbackId;
    // the bindings below decide which MMF_Players answer, so effects are tuned in the
    // Inspector instead of in code. An id may have several bindings (e.g. a squash and a
    // shake), each with its own channel so Reduced Motion can filter them separately.
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

        readonly Dictionary<FeedbackId, List<Entry>> entries = new Dictionary<FeedbackId, List<Entry>>();
        bool bindingsLoaded;

        public bool ReducedMotion { get; set; }

        public bool Has(FeedbackId id)
        {
            LoadBindings();
            return entries.TryGetValue(id, out var list) && list.Count > 0;
        }

        // Used while code-built effects are migrated to bound MMF_Players:
        // a fallback is ignored when the scene already binds the id.
        public void RegisterFallback(FeedbackId id, FeedbackChannel channel, IFeedback fallback)
        {
            if (fallback == null)
                throw new ArgumentNullException(nameof(fallback));
            if (!Has(id))
                Add(id, new Entry(fallback, channel));
        }

        // Returns false when nothing played (unknown id, or everything filtered by Reduced Motion).
        public bool Play(FeedbackId id)
        {
            LoadBindings();
            if (!entries.TryGetValue(id, out var list))
                return false;
            bool played = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReducedMotion && IsMotion(list[i].Channel))
                    continue;
                list[i].Feedback.Play();
                played = true;
            }
            return played;
        }

        // Stops one id's effects and returns their targets to rest (e.g. before another effect
        // reuses the same target with a different pivot).
        public void Stop(FeedbackId id)
        {
            LoadBindings();
            if (!entries.TryGetValue(id, out var list))
                return;
            for (int i = 0; i < list.Count; i++)
                list[i].Feedback.Stop();
        }

        public void StopAll()
        {
            foreach (var list in entries.Values)
            {
                for (int i = 0; i < list.Count; i++)
                    list[i].Feedback.Stop();
            }
        }

        static bool IsMotion(FeedbackChannel channel)
        {
            return channel == FeedbackChannel.Motion || channel == FeedbackChannel.Flash;
        }

        void Add(FeedbackId id, Entry entry)
        {
            if (!entries.TryGetValue(id, out var list))
            {
                list = new List<Entry>(2);
                entries.Add(id, list);
            }
            list.Add(entry);
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
                    Add(binding.id, new Entry(new FeelFeedback(binding.player), binding.channel));
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
