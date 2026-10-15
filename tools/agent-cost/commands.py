"""Wall-clock and failure sub-classes of build/test commands, most expensive subagent and main sessions.
Usage: python -I commands.py <transcript-root> <out.json>
(part of tools/agent-cost, see README.md)
"""
import json, os, re, sys
from collections import Counter, defaultdict
from datetime import datetime

ROOT, OUT = sys.argv[1], sys.argv[2]


def ts(s):
    try:
        return datetime.fromisoformat(s.replace("Z", "+00:00")).timestamp()
    except Exception:
        return None


def text_of(content):
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        out = []
        for b in content:
            if isinstance(b, dict):
                if b.get("type") == "text":
                    out.append(b.get("text", ""))
                elif b.get("type") == "tool_result":
                    out.append(text_of(b.get("content")))
        return "\n".join(out)
    return ""


def kind_of(cmd):
    c = cmd.lower()
    if "run-alltests" in c:
        return "Run-AllTests"
    if "dotnet test" in c:
        if "companion.tests" in c:
            return "dotnet test Companion.Tests"
        if "lite.tests" in c:
            return "dotnet test Lite.Tests"
        return "dotnet test (other)"
    if "dotnet build" in c or "dotnet publish" in c:
        return "dotnet build/publish"
    if "msbuild" in c:
        return "msbuild"
    if "build.ps1" in c and "run-" not in c:
        return "build.ps1"
    if "go test" in c or "go vet" in c or "go build" in c:
        return "go"
    if "test-" in c and ".ps1" in c:
        return "Test-*.ps1 gate"
    return None


stats = defaultdict(lambda: {"n": 0, "secs": 0.0, "fail": 0, "bg": 0, "durs": []})
subclass = Counter()
sub_examples = {}
subs = []  # nested sessions detail
sessions = []

for dp, dn, fn in os.walk(ROOT):
    for f in fn:
        if not f.endswith(".jsonl"):
            continue
        path = os.path.join(dp, f)
        nested = "subagents" in path.replace("\\", "/").split("/")
        uses = {}
        seen = set()
        n_req = 0
        cr = 0
        last_ctx = 0
        model = None
        first_prompt = None
        first_ts = last_ts = None
        user_msgs = 0
        tool_calls = 0
        edits = 0
        reads = 0
        with open(path, encoding="utf-8", errors="replace") as fh:
            for line in fh:
                try:
                    e = json.loads(line)
                except ValueError:
                    continue
                t = e.get("type")
                tstamp = ts(e.get("timestamp") or "")
                if tstamp:
                    first_ts = first_ts or tstamp
                    last_ts = tstamp
                if t == "assistant":
                    m = e.get("message") or {}
                    u = m.get("usage") or {}
                    rid = e.get("requestId") or e.get("uuid")
                    if rid not in seen:
                        seen.add(rid)
                        n_req += 1
                    c = (u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0)
                         + u.get("cache_creation_input_tokens", 0))
                    last_ctx = max(last_ctx, c) if False else c or last_ctx
                    model = m.get("model") or model
                    for b in m.get("content") or []:
                        if isinstance(b, dict) and b.get("type") == "tool_use":
                            tool_calls += 1
                            nm = b.get("name")
                            if nm in ("Edit", "Write"):
                                edits += 1
                            if nm == "Read":
                                reads += 1
                            inp = b.get("input") or {}
                            cmd = inp.get("command") or ""
                            uses[b.get("id")] = (nm, cmd, tstamp, bool(inp.get("run_in_background")))
                elif t == "user":
                    m = e.get("message") or {}
                    content = m.get("content")
                    if first_prompt is None:
                        first_prompt = text_of(content)[:220].replace("\n", " ")
                    if isinstance(content, str) or (isinstance(content, list) and any(
                            isinstance(b, dict) and b.get("type") == "text" for b in content)):
                        user_msgs += 1
                    if isinstance(content, list):
                        for b in content:
                            if isinstance(b, dict) and b.get("type") == "tool_result":
                                nm, cmd, t0, bg = uses.get(b.get("tool_use_id"), (None, "", None, False))
                                if nm not in ("Bash", "PowerShell") or not cmd:
                                    continue
                                k = kind_of(cmd)
                                if not k:
                                    continue
                                body = text_of(b.get("content"))
                                s = stats[k]
                                s["n"] += 1
                                if bg:
                                    s["bg"] += 1
                                if tstamp and t0 and not bg:
                                    d = tstamp - t0
                                    if 0 <= d < 3600:
                                        s["secs"] += d
                                        s["durs"].append(d)
                                if b.get("is_error"):
                                    s["fail"] += 1
                                    low = body
                                    if "MSB1009" in low or "does not exist" in low[:300]:
                                        c2 = "wrong project/path (MSB1009 / not found)"
                                    elif re.search(r"error BC\d+", low):
                                        c2 = "compile error (BCxxxx)"
                                    elif re.search(r"error CS\d+", low):
                                        c2 = "compile error (CSxxxx)"
                                    elif "being used by another process" in low or "cannot access the file" in low or "Could not copy" in low:
                                        c2 = "file locked (running exe/other build)"
                                    elif re.search(r"Failed!|Failed:\s+[1-9]|\[FAIL\]|FAIL\s", low):
                                        c2 = "real test failure"
                                    elif "command not found" in low:
                                        c2 = "tool not on PATH (bash)"
                                    elif "unexpected EOF" in low or "syntax error" in low:
                                        c2 = "shell quoting error"
                                    elif "COULD NOT VERIFY" in low:
                                        c2 = "could-not-verify"
                                    else:
                                        c2 = "other"
                                    subclass[(k, c2)] += 1
                                    sub_examples.setdefault((k, c2), body[:260].replace("\n", " | "))
        if n_req == 0:
            continue
        rec = {"file": f, "nested": nested, "turns": n_req, "last_ctx": last_ctx, "model": model,
               "prompt": first_prompt, "dur_min": ((last_ts - first_ts) / 60 if first_ts and last_ts else 0),
               "user_msgs": user_msgs, "tools": tool_calls, "edits": edits, "reads": reads}
        (subs if nested else sessions).append(rec)

out = {
    "cmd_stats": {k: {"n": v["n"], "fail": v["fail"], "bg": v["bg"],
                      "total_min": round(v["secs"] / 60, 1),
                      "avg_s": round(v["secs"] / max(1, len(v["durs"])), 1),
                      "p90_s": round(sorted(v["durs"])[int(len(v["durs"]) * .9)] if v["durs"] else 0, 1),
                      "max_s": round(max(v["durs"]) if v["durs"] else 0, 1)} for k, v in stats.items()},
    "fail_subclass": [[k[0], k[1], n] for k, n in subclass.most_common(30)],
    "fail_sub_examples": {f"{k[0]} / {k[1]}": v for k, v in sub_examples.items()},
    "main_sessions": sorted(sessions, key=lambda r: -r["turns"])[:20],
    "n_main": len(sessions),
    "subs_top": sorted(subs, key=lambda r: -r["turns"] * r["last_ctx"])[:25],
    "subs_by_model_turns": Counter(),
}
bm = Counter()
for s in subs:
    bm[s["model"]] += s["turns"]
out["subs_by_model_turns"] = bm.most_common()
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False)
print("ok", len(sessions), "main", len(subs), "nested")
