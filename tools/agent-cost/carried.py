"""Context size, carried tokens per tool/file/command, redundant re-reads, hard-failure classes (deduplicated by requestId, nested subagent sessions included).
Usage: python -I carried.py <transcript-root> <out.json>
(part of tools/agent-cost, see README.md)
"""
import json, os, re, sys
from collections import Counter, defaultdict

ROOT, OUT = sys.argv[1], sys.argv[2]


def ctx_of(u):
    return (u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0)
            + u.get("cache_creation_input_tokens", 0))


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


files = []
for dp, dn, fn in os.walk(ROOT):
    for f in fn:
        if f.endswith(".jsonl"):
            files.append(os.path.join(dp, f))

res = {
    "prefix_main": [], "prefix_nested": [],
    "sub_sessions": [],          # nested sessions: turns, last ctx, first prompt chars, model
    "carried_by_tool": Counter(),     # token-turns carried by results, per tool
    "carried_by_file": Counter(),     # token-turns per Read path
    "carried_by_cmd": Counter(),
    "result_tokens_by_tool": Counter(),
    "fail_classes": defaultdict(Counter),   # tool -> signature -> n
    "fail_examples": {},
    "reread": Counter(),               # path -> redundant read count
    "reread_chars": Counter(),
    "ctx_hist": Counter(),
    "cacheread_by_ctx_band": Counter(),
    "turns_total": 0,
}

BAND = [50_000, 100_000, 200_000, 300_000, 500_000, 800_000, 10**9]


def band(c):
    for b in BAND:
        if c <= b:
            return b


def classify_fail(tool, body, cmd):
    b = body.strip()
    first = b.splitlines()[0][:140] if b else "(empty)"
    low = b.lower()
    if "was blocked" in low or "hook" in low and "block" in low:
        return "HOOK-BLOCK: " + first[:100]
    if "permission" in low and ("denied" in low or "deny" in low):
        return "PERMISSION: " + first[:100]
    if "exit code" in low[:40]:
        m = re.search(r"exit code (\d+)", low[:60])
        code = m.group(1) if m else "?"
        what = "other"
        c = cmd.lower()
        if "dotnet test" in c:
            what = "dotnet test"
        elif "dotnet build" in c or "dotnet publish" in c or "msbuild" in c:
            what = "build"
        elif "run-alltests" in c:
            what = "Run-AllTests"
        elif "go test" in c or "go build" in c or "go vet" in c:
            what = "go"
        elif "git " in c:
            what = "git"
        elif ".ps1" in c:
            what = "ps1 script"
        return f"exit {code} :: {what}"
    if "file has not been read" in low or "has been modified since read" in low or "must read" in low:
        return "EDIT-STALE/NOT-READ"
    if "string to replace not found" in low or "no match" in low or "not unique" in low or "multiple matches" in low:
        return "EDIT-NO-MATCH"
    if "file does not exist" in low or "no such file" in low or "cannot find path" in low:
        return "NO-SUCH-FILE"
    if "exceeds maximum" in low or "too large" in low or "tokens" in low and "exceed" in low:
        return "TOO-LARGE-READ"
    return "OTHER: " + first[:90]


for path in files:
    nested = "subagents" in path.replace("\\", "/").split("/")
    seen = {}               # requestId -> best ctx
    reqs = []               # ordered unique requests: (rid, ctx, out)
    # tool_use_id -> (tool, key, cmd)
    uses = {}
    results = []            # (turn_index_at_arrival, tool, key, cmd, tokens, is_err, body)
    reads_state = {}        # path -> last turn index of read, edited flag
    last_edit_turn = {}
    first_user_chars = None
    model = None
    turn_idx = 0
    compact_marks = []
    try:
        fh = open(path, encoding="utf-8", errors="replace")
    except OSError:
        continue
    with fh:
        for line in fh:
            try:
                e = json.loads(line)
            except ValueError:
                continue
            t = e.get("type")
            if t == "system" and e.get("subtype") == "compact_boundary":
                compact_marks.append(turn_idx)
                continue
            if t == "assistant":
                m = e.get("message") or {}
                u = m.get("usage") or {}
                rid = e.get("requestId") or e.get("uuid")
                c = ctx_of(u)
                if rid not in seen:
                    turn_idx += 1
                    seen[rid] = len(reqs)
                    reqs.append([c, u.get("output_tokens", 0), u.get("cache_read_input_tokens", 0)])
                    model = m.get("model") or model
                else:
                    r = reqs[seen[rid]]
                    r[0] = max(r[0], c)
                    r[1] = max(r[1], u.get("output_tokens", 0))
                    r[2] = max(r[2], u.get("cache_read_input_tokens", 0))
                for b in m.get("content") or []:
                    if isinstance(b, dict) and b.get("type") == "tool_use":
                        inp = b.get("input") or {}
                        name = b.get("name")
                        key = inp.get("file_path") or inp.get("path") or ""
                        cmd = inp.get("command") or ""
                        uses[b.get("id")] = (name, key, cmd)
                        if name in ("Edit", "Write", "NotebookEdit") and key:
                            last_edit_turn[key.lower()] = turn_idx
            elif t == "user":
                m = e.get("message") or {}
                content = m.get("content")
                if first_user_chars is None:
                    first_user_chars = len(text_of(content))
                if isinstance(content, list):
                    for b in content:
                        if isinstance(b, dict) and b.get("type") == "tool_result":
                            tid = b.get("tool_use_id")
                            name, key, cmd = uses.get(tid, ("?", "", ""))
                            body = text_of(b.get("content"))
                            results.append((turn_idx, name, key, cmd, len(body) / 3.6,
                                            bool(b.get("is_error")), body))
                            if name == "Read" and key:
                                k = key.lower()
                                prev = reads_state.get(k)
                                if prev is not None and last_edit_turn.get(k, -1) < prev:
                                    res["reread"][key] += 1
                                    res["reread_chars"][key] += len(body)
                                reads_state[k] = turn_idx
    n = len(reqs)
    if n == 0:
        continue
    res["turns_total"] += n
    if nested:
        res["prefix_nested"].append(reqs[0][0])
        res["sub_sessions"].append({"turns": n, "last_ctx": reqs[-1][0], "first_prompt_chars": first_user_chars,
                                    "model": model, "cache_read": sum(r[2] for r in reqs)})
    else:
        res["prefix_main"].append(reqs[0][0])
    for c, o, cr in reqs:
        res["cacheread_by_ctx_band"][band(c)] += cr
        res["ctx_hist"][band(c)] += 1
    bounds = compact_marks + [n]
    for (ti, name, key, cmd, tok, err, body) in results:
        nxt = min([b for b in bounds if b >= ti] or [n])
        remaining = max(0, nxt - ti)
        cost = tok * remaining
        res["carried_by_tool"][name] += cost
        res["result_tokens_by_tool"][name] += tok
        if name == "Read" and key:
            res["carried_by_file"][key.lower()] += cost
        if name in ("Bash", "PowerShell") and cmd:
            sig = re.sub(r"[A-Za-z]:[\\/][^\s'\"]*[\\/]scratchpad[\\/]?", "<SCRATCH>/", cmd)
            sig = re.sub(r"\s+", " ", sig)[:110]
            res["carried_by_cmd"][sig] += cost
        if err and name in ("Bash", "PowerShell", "Edit", "Read", "Write", "Grep"):
            cls = classify_fail(name, body, cmd)
            res["fail_classes"][name][cls] += 1
            res["fail_examples"].setdefault(cls, body[:400])

# summarise
def pct(a, p):
    a = sorted(a)
    return a[int(len(a) * p)] if a else 0

out = {
    "files": len(files),
    "turns": res["turns_total"],
    "prefix_main": {"n": len(res["prefix_main"]), "p10": pct(res["prefix_main"], .1), "p50": pct(res["prefix_main"], .5), "p90": pct(res["prefix_main"], .9)},
    "prefix_nested": {"n": len(res["prefix_nested"]), "p10": pct(res["prefix_nested"], .1), "p50": pct(res["prefix_nested"], .5), "p90": pct(res["prefix_nested"], .9)},
    "ctx_hist": dict(sorted(res["ctx_hist"].items())),
    "cacheread_by_ctx_band": dict(sorted(res["cacheread_by_ctx_band"].items())),
    "carried_by_tool": res["carried_by_tool"].most_common(12),
    "result_tokens_by_tool": res["result_tokens_by_tool"].most_common(12),
    "carried_by_file": res["carried_by_file"].most_common(45),
    "carried_by_cmd": res["carried_by_cmd"].most_common(25),
    "reread": [(p, n, res["reread_chars"][p]) for p, n in res["reread"].most_common(40)],
    "reread_total": sum(res["reread"].values()),
    "reread_chars_total": sum(res["reread_chars"].values()),
    "fail_classes": {k: v.most_common(14) for k, v in res["fail_classes"].items()},
    "fail_examples": res["fail_examples"],
    "sub_sessions": {
        "n": len(res["sub_sessions"]),
        "turns_p50": pct([s["turns"] for s in res["sub_sessions"]], .5),
        "turns_p90": pct([s["turns"] for s in res["sub_sessions"]], .9),
        "last_ctx_p50": pct([s["last_ctx"] for s in res["sub_sessions"]], .5),
        "last_ctx_p90": pct([s["last_ctx"] for s in res["sub_sessions"]], .9),
        "cache_read_total": sum(s["cache_read"] for s in res["sub_sessions"]),
        "by_model": Counter((s["model"] or "?") for s in res["sub_sessions"]).most_common(),
        "prompt_chars_p50": pct([s["first_prompt_chars"] or 0 for s in res["sub_sessions"]], .5),
    },
}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(out, f, indent=1, ensure_ascii=False)
print("ok", len(files), "files,", res["turns_total"], "unique requests")
