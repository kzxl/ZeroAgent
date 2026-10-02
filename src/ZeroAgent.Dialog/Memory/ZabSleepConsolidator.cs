using System;
using System.Diagnostics;
using ZeroAgent.Core.Database;

namespace ZeroAgent.Dialog.Memory
{
    /// <summary>
    /// Configuration options for the cognitive sleep consolidation loop.
    /// </summary>
    public sealed class SleepConsolidationOptions
    {
        public int MinAccessCountForReflex { get; set; } = 2;
        public float AdaptationLearningRate { get; set; } = 0.05f;
        public bool AutoCachePlans { get; set; } = true;
        public float PruneLowRetentionThreshold { get; set; } = 0.05f;
        public bool AutoCompactDatabase { get; set; } = true;
    }

    /// <summary>
    /// Summary report generated after the agent completes a sleep consolidation cycle.
    /// </summary>
    public sealed class SleepConsolidationReport
    {
        public int TotalEpisodesScanned { get; set; }
        public int ReflexesSynthesized { get; set; }
        public int ReflexionsGenerated { get; set; }
        public int PlansCached { get; set; }
        public TimeSpan Elapsed { get; set; }
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Offline / Idle Cognitive Sleep Consolidator modeled after human memory consolidation during sleep.
    /// Sweeps short-term episodic experiences (hippocampus tier), extracts repeated patterns and verified resolutions,
    /// distills them into instant System 1 reflexes (INT8 weights) and semantic plan trajectories,
    /// and prunes decayed memory according to Ebbinghaus forgetting dynamics.
    /// </summary>
    public static class ZabSleepConsolidator
    {
        /// <summary>
        /// Executes a full cognitive sleep consolidation cycle between the AgenticMemoryEngine and ZabDatabase.
        /// </summary>
        public static SleepConsolidationReport Consolidate(
            AgenticMemoryEngine engine,
            ZabDatabase db,
            SleepConsolidationOptions? options = null)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (db == null) throw new ArgumentNullException(nameof(db));

            options ??= new SleepConsolidationOptions();
            var sw = Stopwatch.StartNew();
            var report = new SleepConsolidationReport();

            var episodes = engine.Episodic.GetAllEpisodes();
            report.TotalEpisodesScanned = episodes.Count;

            float[] tempSpan = new float[128];

            for (int i = 0; i < episodes.Count; i++)
            {
                var ep = episodes[i];
                bool hasEmbedding = engine.Episodic.TryGetEmbedding(ep.Id, tempSpan);
                ReadOnlySpan<float> emb = hasEmbedding ? tempSpan.AsSpan() : ReadOnlySpan<float>.Empty;

                if (ep.Success)
                {
                    // If episode was successful and encountered frequently, distill into System 1 instinct
                    if (ep.AccessCount >= options.MinAccessCountForReflex)
                    {
                        if (options.AutoCachePlans && !string.IsNullOrWhiteSpace(ep.Resolution))
                        {
                            db.CachePlan(
                                goal: ep.Issue,
                                solution: ep.Resolution,
                                stepsCount: 1,
                                confidence: 0.95f,
                                goalVector: emb);
                            report.PlansCached++;
                        }

                        if (!emb.IsEmpty && db.NeuralPolicy != null && !string.IsNullOrWhiteSpace(ep.Resolution))
                        {
                            db.AdaptNeuralWeights(emb, ep.Resolution, options.AdaptationLearningRate);
                            report.ReflexesSynthesized++;
                        }
                    }
                }
                else
                {
                    // Failed attempt -> Crystallize into explicit self-reflection lesson
                    db.AddReflexion(
                        goal: ep.Issue,
                        failureReason: "Incident failed in operational history.",
                        lesson: string.IsNullOrWhiteSpace(ep.Resolution)
                            ? "Avoid repeating past failed action paths without verification."
                            : ep.Resolution);
                    report.ReflexionsGenerated++;
                }
            }

            // Continuous decay pruning
            engine.Episodic.PruneExpiredEpisodes(DateTime.UtcNow, options.PruneLowRetentionThreshold);

            // Persist full state to sovereign .zab container
            ZabMemoryStorageBridge.PersistToZab(engine, db);

            if (options.AutoCompactDatabase)
            {
                db.Compact();
            }

            sw.Stop();
            report.Elapsed = sw.Elapsed;
            return report;
        }
    }
}
