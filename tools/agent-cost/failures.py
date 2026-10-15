"""Exact counts of the recurring failure classes (hook refusals, sleep polls, command not found, quoting errors).
Usage: python -I failures.py <transcript-root>
(part of tools/agent-cost, see README.md)
"""
import json, os, re, sys
from collections import Counter

ROOT = sys.argv[1]
c = Counter()
cmd_not_found = Counter()
hook_names = Counter()
for dp, dn, fn in os.walk(ROOT):
    for f in fn:
        if not f.endswith(".jsonl"):
            continue
        uses = {}
        with open(os.path.join(dp, f), encoding="utf-8", errors="replace") as fh:
            for line in fh:
                if '"tool_result"' not in line and '"tool_use"' not in line:
                    continue
                try:
                    e = json.loads(line)
                except ValueError:
                    continue
                m = e.get("message") or {}
                content = m.get("content")
                if not isinstance(content, list):
                    continue
                for b in content:
                    if not isinstance(b, dict):
                        continue
                    if b.get("type") == "tool_use":
                        uses[b.get("id")] = b.get("name")
                    elif b.get("type") == "tool_result" and b.get("is_error"):
                        body = b.get("content")
                        if isinstance(body, list):
                            body = "\n".join(x.get("text", "") for x in body if isinstance(x, dict))
                        body = body or ""
                        tool = uses.get(b.get("tool_use_id"), "?")
                        if re.search(r"Blocked: (sleep|Start-Sleep)", body):
                            c["sleep-poll refused"] += 1
                        elif "hook error" in body or "Blocked by" in body:
                            c["hook refusal (all)"] += 1
                            mm = re.search(r"Blocked by ([\w\- ]+?)( \(|:)", body)
                            hook_names[mm.group(1).strip() if mm else "other"] += 1
                        elif "command not found" in body:
                            c["bash: command not found"] += 1
                            mm = re.search(r"(\S+): command not found", body)
                            cmd_not_found[mm.group(1) if mm else "?"] += 1
                        elif "unexpected EOF" in body or "unexpected token" in body or "syntax error" in body:
                            c["bash: quoting/syntax error"] += 1
                        elif "is blocked. This path is protected" in body:
                            c["Remove-Item protected-path guard"] += 1
                        elif "has been denied" in body:
                            c["permission denied (git rewind etc.)"] += 1
                        elif "exceeds maximum allowed tokens" in body:
                            c["read too large"] += 1
                        elif "String to replace not found" in body:
                            c["edit: no match"] += 1
                        elif "MSB1009" in body:
                            c["dotnet: project file missing"] += 1
print(json.dumps({"counts": c, "command_not_found": cmd_not_found.most_common(10), "hooks": hook_names.most_common(10)}, indent=1))
