using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hark.Core
{
    /// <summary>Gives each running agent its own pet; a finished agent's pet is free again.</summary>
    public sealed class PetBook
    {
        public static readonly IReadOnlyList<string> Names = new[] { "Fox", "Owl", "Cat", "Bunny", "Panda", "Frog", "Penguin", "Hedgehog" };

        private readonly Dictionary<string, int> _used = new Dictionary<string, int>();

        // Pets of agents that handed back, oldest first. A new agent leaves them alone while another pet is free,
        // so a resumed agent (same id) gets its own pet back.
        private readonly List<KeyValuePair<string, int>> _resting = new List<KeyValuePair<string, int>>();

        public int Assign(string agentId)
        {
            if (_used.TryGetValue(agentId, out var pet))
            {
                return pet;
            }
            var taken = new HashSet<int>(_used.Values);
            var own = _resting.FindIndex(r => r.Key == agentId);
            if (own >= 0 && !taken.Contains(_resting[own].Value))
            {
                pet = _resting[own].Value;
            }
            else
            {
                var free = Enumerable.Range(0, Names.Count).Where(i => !taken.Contains(i)).ToList();
                var resting = new HashSet<int>(_resting.Select(r => r.Value));
                if (free.Count == 0)
                {
                    pet = _used.Count % Names.Count;
                }
                else if (free.Any(i => !resting.Contains(i)))
                {
                    pet = free.First(i => !resting.Contains(i));
                }
                else
                {
                    pet = _resting.First(r => free.Contains(r.Value)).Value; // every free pet rests: take the one resting longest
                }
            }
            _resting.RemoveAll(r => r.Key == agentId || r.Value == pet);
            _used[agentId] = pet;
            return pet;
        }

        public void Release(string agentId)
        {
            if (!_used.TryGetValue(agentId, out var pet))
            {
                return;
            }
            _used.Remove(agentId);
            _resting.RemoveAll(r => r.Key == agentId || r.Value == pet);
            _resting.Add(new KeyValuePair<string, int>(agentId, pet));
        }
    }

    /// <summary>Learns how many steps each agent type usually takes (median of its last 20 runs, once it has 3).</summary>
    public sealed class StepStats
    {
        public const int DefaultSteps = 12;
        public const int MinRuns = 3;
        private const int Keep = 20;

        private Dictionary<string, List<int>> _runs = new Dictionary<string, List<int>>();

        public bool Changed { get; set; }

        public void Record(string agentType, int steps)
        {
            if (string.IsNullOrEmpty(agentType) || steps <= 0)
            {
                return;
            }
            if (!_runs.TryGetValue(agentType, out var list))
            {
                list = new List<int>();
                _runs[agentType] = list;
            }
            list.Add(steps);
            if (list.Count > Keep)
            {
                list.RemoveRange(0, list.Count - Keep);
            }
            Changed = true;
        }

        public int Typical(string agentType)
        {
            // One or two runs say little (a single 1 step task would make every bar jump to 80%).
            if (agentType == null || !_runs.TryGetValue(agentType, out var list) || list.Count < MinRuns)
            {
                return DefaultSteps;
            }
            var sorted = list.OrderBy(n => n).ToList();
            return Math.Max(1, sorted[sorted.Count / 2]);
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(_runs));
            Changed = false;
        }

        public static StepStats Load(string path)
        {
            var stats = new StepStats();
            try
            {
                if (File.Exists(path))
                {
                    stats._runs = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(File.ReadAllText(path)) ?? new Dictionary<string, List<int>>();
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            return stats;
        }
    }

    public sealed class ProgressInfo
    {
        public double Fraction { get; internal set; }
        public string Label { get; internal set; }
        public bool IsEstimate { get; internal set; }
    }

    /// <summary>Real progress from the agent's to-do list, else an estimate from past runs (half full at the usual length).</summary>
    public static class AgentProgress
    {
        public static ProgressInfo ForTodos(IReadOnlyList<TodoItem> todos)
        {
            var done = todos.Count(t => t.Status == "completed");
            return new ProgressInfo { Fraction = (double)done / todos.Count, Label = $"{done} of {todos.Count} done" };
        }

        public static ProgressInfo For(AgentInfo agent, StepStats stats)
        {
            if (agent.Todos.Count > 0)
            {
                return ForTodos(agent.Todos);
            }
            return Estimate(agent.Steps, stats.Typical(agent.AgentType));
        }

        /// <summary>Steps against the usual step count, at most 95% until the work ends.</summary>
        public static ProgressInfo Estimate(int steps, int typical)
        {
            // Half full at the usual length, then still moving: half of all runs go past the usual length,
            // so a bar that stops there would sit still for the second half of those runs.
            var fraction = steps <= 0 ? 0 : (double)steps / (steps + Math.Max(1, typical));
            return new ProgressInfo
            {
                Fraction = fraction,
                Label = $"~{(int)Math.Round(fraction * 100)}% · step {steps}",
                IsEstimate = true,
            };
        }
    }

    /// <summary>One row of the agent panel.</summary>
    public sealed class AgentRow
    {
        public string AgentId { get; internal set; }
        public string SessionId { get; internal set; }
        public string Project { get; internal set; }
        public int PetIndex { get; internal set; }
        public string PetName { get; internal set; }
        public string AgentType { get; internal set; }
        public string Task { get; internal set; }
        public string Elapsed { get; internal set; }
        public ProgressInfo Progress { get; internal set; }
        public IReadOnlyList<TodoItem> Todos { get; internal set; }
        public IReadOnlyList<string> RecentSteps { get; internal set; }

        /// <summary>True for a session's own running task (top row, no pet).</summary>
        public bool IsMainTask { get; internal set; }
    }
}
