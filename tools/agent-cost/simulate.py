"""What-if model: prefix cut and a lower compaction window, output tokens per turn by model and thinking.
Usage: python -I simulate.py <transcript-root>
(part of tools/agent-cost, see README.md)
"""
import json, os, sys
from collections import defaultdict

ROOT = sys.argv[1]
series = []   # (nested, [ctx...], model)
out_by = defaultdict(lambda: [0, 0])   # key -> [turns, output tokens]

for dp, dn, fn in os.walk(ROOT):
    for f in fn:
        if not f.endswith(".jsonl"):
            continue
        p = os.path.join(dp, f)
        nested = "subagents" in p.replace("\\", "/").split("/")
        idx = {}
        ctxs = []
        outs = []
        think = []
        model = None
        with open(p, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                try:
                    e = json.loads(line)
                except ValueError:
                    continue
                if e.get("type") != "assistant":
                    continue
                m = e.get("message") or {}
                u = m.get("usage") or {}
                rid = e.get("requestId") or e.get("uuid")
                c = u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0) + u.get("cache_creation_input_tokens", 0)
                o = u.get("output_tokens", 0)
                has_think = any(isinstance(b, dict) and b.get("type") == "thinking" for b in (m.get("content") or []))
                if rid not in idx:
                    idx[rid] = len(ctxs)
                    ctxs.append(c)
                    outs.append(o)
                    think.append(has_think)
                    model = m.get("model") or model
                else:
                    i = idx[rid]
                    ctxs[i] = max(ctxs[i], c)
                    outs[i] = max(outs[i], o)
                    think[i] = think[i] or has_think
        if ctxs:
            series.append((nested, ctxs, model))
            for o, t in zip(outs, think):
                k = (model, "thinking" if t else "no-thinking")
                out_by[k][0] += 1
                out_by[k][1] += o

actual = sum(sum(c) for _, c, _ in series)
print(f"actual sum of per-turn context (token-turns): {actual/1e9:.2f}B over {sum(len(c) for _,c,_ in series)} turns")


def simulate(T, summary=14000, prefix_cut=0):
    total = 0
    comps = 0
    comp_cost = 0
    for nested, ctxs, model in series:
        base = max(ctxs[0] - prefix_cut, 1000)
        cur = base
        prev = ctxs[0]
        total += cur
        for c in ctxs[1:]:
            d = c - prev
            prev = c
            if d < -60000:      # natural compaction already in the data
                cur = max(c - prefix_cut, 1000)
            else:
                cur = cur + d
            if T and cur > T:
                comps += 1
                comp_cost += cur + 8000      # the summarising call reads the whole context + writes ~8k
                cur = base + summary
            total += cur
    return total, comps, comp_cost


print("\n-- prefix cut only --")
for cut in (0, 20000, 30000, 40000):
    t, _, _ = simulate(0, prefix_cut=cut)
    print(f"prefix -{cut//1000:>2}k : {t/1e9:.2f}B  ({(1-t/actual)*100:5.1f}% of token-turns saved)")

print("\n-- compaction threshold (prefix unchanged) --")
for T in (150000, 200000, 250000, 300000, 400000, 500000):
    t, n, cc = simulate(T)
    print(f"compact at {T//1000:>3}k : {(t+cc)/1e9:.2f}B incl {n} summarising calls ({cc/1e9:.2f}B)  saved {(1-(t+cc)/actual)*100:5.1f}%")

print("\n-- both: prefix -35k and compaction at 300k --")
t, n, cc = simulate(300000, prefix_cut=35000)
print(f"{(t+cc)/1e9:.2f}B  saved {(1-(t+cc)/actual)*100:5.1f}%  compactions {n}")

print("\n-- output tokens/turn by model and thinking-block presence --")
for k, (n, o) in sorted(out_by.items(), key=lambda kv: -kv[1][1]):
    print(f"{str(k[0]):28} {k[1]:12} turns={n:6} out={o/1e6:6.2f}M  avg={o/n:7.0f}/turn")
