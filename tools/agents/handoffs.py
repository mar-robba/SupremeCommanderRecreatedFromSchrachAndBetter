"""How often work had to pass to a fresh agent, and which attempts died before doing anything (orchestrator tool,
see docs/WORKFLOW.md "Fragmentation: does a small cap hurt the result?").

  python3 -I tools/agents/handoffs.py [--since YYYY-MM-DD] [--tag TEXT] [--agents]

Per agent and sub-agent transcript:
  start    context of the first model call (system prompt + tools + the agent prompt);
  work@    context when the agent first started real work (first Edit / Write / Agent call, or a shell
           command that edits files, builds, tests or commits);
  peak     largest context; ckpt = it stopped with CHECKPOINT (the piece goes on with a fresh agent);
  dead     it stopped at its cap having worked only in the last 15 % of its context (or never): it read until
           the cap and handed over almost nothing. A dead attempt is pure loss and a sign the prompt or the cap was
           wrong for the task.
Per purpose tag ([...] at the start of the description): attempts, handoffs (ckpt), dead attempts. These are only
the countable symptoms; the harm to the result itself (lost reasoning, redone or contradicted work, bugs at the
seams) comes from the "Handoff review" lines of the agents' reports (WORKFLOW).
Reads only usage fields, tool names, shell commands and the word CHECKPOINT of the transcripts, never their prose.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from agent_ctx import project_dir  # noqa: E402

WORK_TOOLS = {"Edit", "Write", "NotebookEdit", "Agent"}
# Bash commands that change files, build or commit: agents often edit through the shell.
WORK_BASH = ("sed -i", "perl -i", "cat >", "cat <<", "tee ", "python3 - <<", "python3 -I - <<", "git commit", "git merge",
             "git apply", "dotnet build", "dotnet test", "dn.sh build", "dn.sh test")


def measure(path):
    seen = set()
    calls = start = work_ctx = peak = 0
    pre_work = total = 0
    worked = False
    last_text = ""
    first_ts = last_ts = None
    with open(path, errors="replace") as f:
        for line in f:
            if '"assistant"' not in line:
                continue
            try:
                d = json.loads(line)
            except ValueError:
                continue
            if d.get("type") != "assistant":
                continue
            m = d.get("message") or {}
            ts = d.get("timestamp")
            first_ts = first_ts or ts
            last_ts = ts or last_ts
            u = m.get("usage") or {}
            ctx = u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0) + u.get("cache_creation_input_tokens", 0)
            for c in m.get("content") or []:
                if c.get("type") == "text":
                    last_text = c.get("text", "")
                elif c.get("type") == "tool_use":
                    name, inp = c.get("name"), c.get("input") or {}
                    if name == "SubagentHandback":
                        last_text = json.dumps(inp)
                    if not worked and (name in WORK_TOOLS or (name == "Bash" and any(
                            w in inp.get("command", "") for w in WORK_BASH))):
                        worked = True
                        work_ctx = ctx
            mid = m.get("id")
            if ctx <= 0 or mid in seen:
                continue
            seen.add(mid)
            calls += 1
            start = start or ctx
            peak = max(peak, ctx)
            total += ctx
            if not worked:
                pre_work += ctx
    dead = ("CHECKPOINT" in last_text) and (not worked or work_ctx > 0.85 * peak)
    return {"dead": dead, "calls": calls, "start": start, "work": work_ctx if worked else None, "pre": pre_work,
            "peak": peak, "total": total, "ckpt": "CHECKPOINT" in last_text, "from": first_ts, "to": last_ts}


def tag_of(desc):
    if desc.startswith("[") and "]" in desc:
        return desc[1:desc.index("]")]
    return "(untagged)"


def main(args):
    since = tag = None
    show_agents = "--agents" in args
    if "--since" in args:
        since = args[args.index("--since") + 1]
    if "--tag" in args:
        tag = args[args.index("--tag") + 1]
    rows = []
    for p in glob.glob(os.path.join(project_dir(), "*", "subagents", "agent-*.jsonl")):
        meta_path = p[:-len(".jsonl")] + ".meta.json"
        desc = ""
        if os.path.exists(meta_path):
            with open(meta_path) as f:
                desc = json.load(f).get("description", "")
        if tag and tag.lower() not in desc.lower():
            continue
        r = measure(p)
        if not r["calls"] or (since and (r["from"] or "") < since):
            continue
        r["id"] = os.path.basename(p)[len("agent-"):-len(".jsonl")]
        r["desc"] = desc
        rows.append(r)
    rows.sort(key=lambda r: r["from"] or "")
    k = lambda v: "-" if v is None else f"{v / 1000:.0f}k"
    M = lambda v: f"{v / 1e6:.1f}M"
    if show_agents:
        print(f"{'agent':17} {'from':16} {'calls':>5} {'start':>6} {'work@':>6} {'pre-work':>8} {'peak':>6} "
              f"{'input':>7} stop  description")
        for r in rows:
            print(f"{r['id']:17} {(r['from'] or '')[:16]:16} {r['calls']:5} {k(r['start']):>6} {k(r['work']):>6} "
                  f"{M(r['pre']):>8} {k(r['peak']):>6} {M(r['total']):>7} {'dead' if r['dead'] else 'ckpt' if r['ckpt'] else '    '}  "
                  f"{r['desc'][:60]}")
        print()
    groups = {}
    for r in rows:
        groups.setdefault(tag_of(r["desc"]), []).append(r)
    print(f"{'purpose':34} {'agents':>6} {'ckpt':>4} {'dead':>4} {'avg work@':>9} {'pre-work':>8} {'input':>7}")
    for g, rs in sorted(groups.items(), key=lambda kv: -sum(r["total"] for r in kv[1])):
        works = [r["work"] for r in rs if r["work"]]
        pre = sum(r["pre"] for r in rs)
        tot = sum(r["total"] for r in rs)
        print(f"{g[:34]:34} {len(rs):6} {sum(r['ckpt'] for r in rs):4} {sum(r['dead'] for r in rs):4} "
              f"{k(sum(works) / len(works) if works else None):>9} {M(pre):>8} {M(tot):>7}")


if __name__ == "__main__":
    main(sys.argv[1:])
