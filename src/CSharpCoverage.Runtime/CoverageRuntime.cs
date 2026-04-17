using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CSharpCoverage.Runtime
{
    public static class CoverageRuntime
    {
        private const int MaxConditionsPerDecision = 64;

        private static readonly ConcurrentDictionary<long, int> _stmts = new ConcurrentDictionary<long, int>();
        private static readonly ConcurrentDictionary<long, int> _branchTaken = new ConcurrentDictionary<long, int>();
        private static readonly ConcurrentDictionary<long, int> _branchNotTaken = new ConcurrentDictionary<long, int>();
        private static readonly ConcurrentDictionary<long, ConcurrentDictionary<int, int>> _cases = new ConcurrentDictionary<long, ConcurrentDictionary<int, int>>();
        private static readonly ConcurrentDictionary<long, List<Observation>> _observations = new ConcurrentDictionary<long, List<Observation>>();

        [ThreadStatic] private static Builder _current;

        private static int _initialized;
        private static readonly object _flushLock = new object();

        private sealed class Builder
        {
            public long CurrentKey;
            public byte[] Values = new byte[MaxConditionsPerDecision];
            public bool Active;

            public void Reset(long key)
            {
                CurrentKey = key;
                Active = true;
                for (int i = 0; i < Values.Length; i++) Values[i] = 0;
            }
        }

        public sealed class Observation
        {
            public byte[] Values;
            public bool Outcome;
            public Observation(byte[] values, bool outcome) { Values = values; Outcome = outcome; }
        }

        private static long Key(int fileId, int id) => ((long)fileId << 32) | (uint)id;

        private static void EnsureInit()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0) return;
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Flush();
            try { AppDomain.CurrentDomain.DomainUnload += (_, __) => Flush(); } catch { }
        }

        public static void Stmt(int fileId, int stmtId)
        {
            EnsureInit();
            var k = Key(fileId, stmtId);
            _stmts.AddOrUpdate(k, 1, (_, v) => v + 1);
        }

        public static bool Branch(int fileId, int decisionId, bool taken)
        {
            EnsureInit();
            var k = Key(fileId, decisionId);
            if (taken) _branchTaken.AddOrUpdate(k, 1, (_, v) => v + 1);
            else _branchNotTaken.AddOrUpdate(k, 1, (_, v) => v + 1);

            var b = _current;
            if (b != null && b.Active && b.CurrentKey == k)
            {
                var copy = new byte[MaxConditionsPerDecision];
                Buffer.BlockCopy(b.Values, 0, copy, 0, MaxConditionsPerDecision);
                var list = _observations.GetOrAdd(k, _ => new List<Observation>());
                lock (list) list.Add(new Observation(copy, taken));
                b.Active = false;
            }
            return taken;
        }

        public static bool Cond(int fileId, int decisionId, int condIdx, bool value)
        {
            EnsureInit();
            var k = Key(fileId, decisionId);
            var b = _current;
            if (b == null || b.CurrentKey != k || !b.Active)
            {
                b = _current ?? (_current = new Builder());
                b.Reset(k);
            }
            if (condIdx >= 0 && condIdx < MaxConditionsPerDecision)
                b.Values[condIdx] = value ? (byte)1 : (byte)2;
            return value;
        }

        public static void Case(int fileId, int decisionId, int caseIndex)
        {
            EnsureInit();
            var k = Key(fileId, decisionId);
            var d = _cases.GetOrAdd(k, _ => new ConcurrentDictionary<int, int>());
            d.AddOrUpdate(caseIndex, 1, (_, v) => v + 1);
        }

        public static T Mark<T>(int fileId, int decisionId, int caseIndex, T value)
        {
            Case(fileId, decisionId, caseIndex);
            return value;
        }

        public static void Reset()
        {
            _stmts.Clear();
            _branchTaken.Clear();
            _branchNotTaken.Clear();
            _cases.Clear();
            _observations.Clear();
        }

        public static void Flush()
        {
            lock (_flushLock)
            {
                var path = Environment.GetEnvironmentVariable("COVERAGE_OUTPUT");
                if (string.IsNullOrEmpty(path)) path = "coverage.json";
                try
                {
                    var json = SerializeJson();
                    var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(path, json);
                }
                catch
                {
                    // swallow — flush must not throw during shutdown
                }
            }
        }

        private static string SerializeJson()
        {
            var sb = new StringBuilder(16 * 1024);
            sb.Append('{');

            sb.Append("\"statements\":");
            AppendGrouped(sb, _stmts);
            sb.Append(',');

            sb.Append("\"branches\":{");
            var allBranchKeys = new HashSet<long>();
            foreach (var k in _branchTaken.Keys) allBranchKeys.Add(k);
            foreach (var k in _branchNotTaken.Keys) allBranchKeys.Add(k);
            var byFileBr = new SortedDictionary<int, SortedDictionary<int, (int t, int n)>>();
            foreach (var k in allBranchKeys)
            {
                int fid = (int)(k >> 32);
                int did = (int)(k & 0xFFFFFFFF);
                _branchTaken.TryGetValue(k, out var t);
                _branchNotTaken.TryGetValue(k, out var n);
                if (!byFileBr.TryGetValue(fid, out var inner)) byFileBr[fid] = inner = new SortedDictionary<int, (int, int)>();
                inner[did] = (t, n);
            }
            bool firstF = true;
            foreach (var fkv in byFileBr)
            {
                if (!firstF) sb.Append(','); firstF = false;
                sb.Append('"').Append(fkv.Key).Append("\":{");
                bool firstD = true;
                foreach (var dkv in fkv.Value)
                {
                    if (!firstD) sb.Append(','); firstD = false;
                    sb.Append('"').Append(dkv.Key).Append("\":{\"taken\":").Append(dkv.Value.t).Append(",\"notTaken\":").Append(dkv.Value.n).Append('}');
                }
                sb.Append('}');
            }
            sb.Append('}').Append(',');

            sb.Append("\"cases\":{");
            var byFileC = new SortedDictionary<int, SortedDictionary<int, SortedDictionary<int, int>>>();
            foreach (var kv in _cases)
            {
                int fid = (int)(kv.Key >> 32);
                int did = (int)(kv.Key & 0xFFFFFFFF);
                if (!byFileC.TryGetValue(fid, out var inner)) byFileC[fid] = inner = new SortedDictionary<int, SortedDictionary<int, int>>();
                var cs = new SortedDictionary<int, int>();
                foreach (var ck in kv.Value) cs[ck.Key] = ck.Value;
                inner[did] = cs;
            }
            firstF = true;
            foreach (var fkv in byFileC)
            {
                if (!firstF) sb.Append(','); firstF = false;
                sb.Append('"').Append(fkv.Key).Append("\":{");
                bool firstD = true;
                foreach (var dkv in fkv.Value)
                {
                    if (!firstD) sb.Append(','); firstD = false;
                    sb.Append('"').Append(dkv.Key).Append("\":{");
                    bool firstCase = true;
                    foreach (var ck in dkv.Value)
                    {
                        if (!firstCase) sb.Append(','); firstCase = false;
                        sb.Append('"').Append(ck.Key).Append("\":").Append(ck.Value);
                    }
                    sb.Append('}');
                }
                sb.Append('}');
            }
            sb.Append('}').Append(',');

            sb.Append("\"observations\":{");
            var byFileO = new SortedDictionary<int, SortedDictionary<int, List<Observation>>>();
            foreach (var kv in _observations)
            {
                int fid = (int)(kv.Key >> 32);
                int did = (int)(kv.Key & 0xFFFFFFFF);
                if (!byFileO.TryGetValue(fid, out var inner)) byFileO[fid] = inner = new SortedDictionary<int, List<Observation>>();
                inner[did] = kv.Value;
            }
            firstF = true;
            foreach (var fkv in byFileO)
            {
                if (!firstF) sb.Append(','); firstF = false;
                sb.Append('"').Append(fkv.Key).Append("\":{");
                bool firstD = true;
                foreach (var dkv in fkv.Value)
                {
                    if (!firstD) sb.Append(','); firstD = false;
                    sb.Append('"').Append(dkv.Key).Append("\":[");
                    bool firstObs = true;
                    List<Observation> snap;
                    lock (dkv.Value) snap = new List<Observation>(dkv.Value);
                    int maxIdx = 0;
                    foreach (var o in snap)
                    {
                        for (int i = MaxConditionsPerDecision - 1; i >= 0; i--)
                            if (o.Values[i] != 0) { if (i + 1 > maxIdx) maxIdx = i + 1; break; }
                    }
                    foreach (var o in snap)
                    {
                        if (!firstObs) sb.Append(','); firstObs = false;
                        sb.Append("{\"o\":").Append(o.Outcome ? "true" : "false").Append(",\"c\":[");
                        for (int i = 0; i < maxIdx; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(o.Values[i]);
                        }
                        sb.Append("]}");
                    }
                    sb.Append(']');
                }
                sb.Append('}');
            }
            sb.Append('}');

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendGrouped(StringBuilder sb, ConcurrentDictionary<long, int> src)
        {
            var byFile = new SortedDictionary<int, SortedDictionary<int, int>>();
            foreach (var kv in src)
            {
                int fid = (int)(kv.Key >> 32);
                int id = (int)(kv.Key & 0xFFFFFFFF);
                if (!byFile.TryGetValue(fid, out var inner)) byFile[fid] = inner = new SortedDictionary<int, int>();
                inner[id] = kv.Value;
            }
            sb.Append('{');
            bool firstF = true;
            foreach (var fkv in byFile)
            {
                if (!firstF) sb.Append(','); firstF = false;
                sb.Append('"').Append(fkv.Key).Append("\":{");
                bool firstS = true;
                foreach (var skv in fkv.Value)
                {
                    if (!firstS) sb.Append(','); firstS = false;
                    sb.Append('"').Append(skv.Key).Append("\":").Append(skv.Value);
                }
                sb.Append('}');
            }
            sb.Append('}');
        }
    }
}
