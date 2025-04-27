// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Text.Rewriting.Primitives;

namespace Itexoft.Text.Rewriting.Text.Internal.Matching;

internal sealed class AhoCorasickAutomaton
{
    private readonly Node[] nodes;
    private readonly char[] transitionChars;
    private readonly int[] transitionNext;

    private AhoCorasickAutomaton(Node[] nodes, char[] transitionChars, int[] transitionNext)
    {
        this.nodes = nodes;
        this.transitionChars = transitionChars;
        this.transitionNext = transitionNext;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Step(int state, char c)
    {
        while (true)
        {
            if (this.TryGetTransition(state, c, out var next))
                return next;

            if (state == 0)
                return 0;

            state = this.nodes[state].Failure;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetBestRuleId(int state) => this.nodes[state].BestRuleId;

    public static AhoCorasickAutomaton Build(
        List<TextRewritePlanBuilder.LiteralPattern> patterns,
        TextRewriteRuleEntry[] rules,
        MatchSelection selection)
    {
        var nodes = new List<BuildNode>(Math.Max(1, patterns.Count * 3))
        {
            new(),
        };

        foreach (var p in patterns)
        {
            var state = 0;

            foreach (var ch in p.Pattern)
            {
                if (!nodes[state].Next.TryGetValue(ch, out var next))
                {
                    next = nodes.Count;
                    nodes.Add(new());
                    nodes[state].Next[ch] = next;
                }

                state = next;
            }

            nodes[state].Outputs.Add(p.RuleId);
        }

        var q = new Queue<int>();

        foreach (var kv in nodes[0].Next)
        {
            var s = kv.Value;
            nodes[s].Failure = 0;
            q.Enqueue(s);
        }

        while (q.Count != 0)
        {
            var r = q.Dequeue();

            foreach (var kv in nodes[r].Next)
            {
                var a = kv.Key;
                var s = kv.Value;

                q.Enqueue(s);

                var st = nodes[r].Failure;

                while (st != 0 && !nodes[st].Next.ContainsKey(a))
                    st = nodes[st].Failure;

                nodes[s].Failure = nodes[st].Next.GetValueOrDefault(a, 0);

                var failOutputs = nodes[nodes[s].Failure].Outputs;

                if (failOutputs.Count != 0)
                    nodes[s].Outputs.AddRange(failOutputs);
            }
        }

        foreach (var t in nodes)
        {
            var bestRuleId = -1;
            var bestLength = 0;
            var bestPriority = int.MaxValue;
            var bestOrder = int.MaxValue;

            var outs = t.Outputs;

            foreach (var ruleId in outs)
            {
                var rule = rules[ruleId];
                var len = rule.FixedLength;

                if (bestRuleId < 0)
                {
                    bestRuleId = ruleId;
                    bestLength = len;
                    bestPriority = rule.Priority;
                    bestOrder = rule.Order;

                    continue;
                }

                if (selection == MatchSelection.LongestThenPriority)
                {
                    if (len > bestLength
                        || (len == bestLength && (rule.Priority < bestPriority || (rule.Priority == bestPriority && rule.Order < bestOrder))))
                    {
                        bestRuleId = ruleId;
                        bestLength = len;
                        bestPriority = rule.Priority;
                        bestOrder = rule.Order;
                    }
                }
                else
                {
                    if (rule.Priority < bestPriority
                        || (rule.Priority == bestPriority && (len > bestLength || (len == bestLength && rule.Order < bestOrder))))
                    {
                        bestRuleId = ruleId;
                        bestLength = len;
                        bestPriority = rule.Priority;
                        bestOrder = rule.Order;
                    }
                }
            }

            t.BestRuleId = bestRuleId;
        }

        var compiledNodes = new Node[nodes.Count];

        var totalEdges = nodes.Sum(t => t.Next.Count);

        var transitionChars = new char[totalEdges];
        var transitionNext = new int[totalEdges];

        var edgeCursor = 0;

        for (var i = 0; i < nodes.Count; i++)
        {
            var bn = nodes[i];
            var count = bn.Next.Count;
            var start = edgeCursor;

            if (count != 0)
            {
                var tmp = new List<KeyValuePair<char, int>>(count);
                tmp.AddRange(bn.Next);

                tmp.Sort(static (a, b) => a.Key.CompareTo(b.Key));

                foreach (var t in tmp)
                {
                    transitionChars[edgeCursor] = t.Key;
                    transitionNext[edgeCursor] = t.Value;
                    edgeCursor++;
                }
            }

            compiledNodes[i] = new(bn.Failure, start, count, bn.BestRuleId);
        }

        return new(compiledNodes, transitionChars, transitionNext);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetTransition(int nodeIndex, char c, out int next)
    {
        var node = this.nodes[nodeIndex];
        var start = node.TransitionStart;
        var count = node.TransitionCount;

        if (count == 0)
        {
            next = 0;

            return false;
        }

        if (count <= 8)
        {
            for (var i = 0; i < count; i++)
            {
                if (this.transitionChars[start + i] == c)
                {
                    next = this.transitionNext[start + i];

                    return true;
                }
            }

            next = 0;

            return false;
        }

        var lo = start;
        var hi = start + count - 1;

        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var ch = this.transitionChars[mid];

            if (ch == c)
            {
                next = this.transitionNext[mid];

                return true;
            }

            if (ch < c)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        next = 0;

        return false;
    }

    private sealed class BuildNode
    {
        public readonly Dictionary<char, int> Next = new();
        public readonly List<int> Outputs = [];
        public int BestRuleId = -1;
        public int Failure;
    }

    private readonly struct Node(int failure, int transitionStart, int transitionCount, int bestRuleId)
    {
        public readonly int Failure = failure;
        public readonly int TransitionStart = transitionStart;
        public readonly int TransitionCount = transitionCount;
        public readonly int BestRuleId = bestRuleId;
    }
}
