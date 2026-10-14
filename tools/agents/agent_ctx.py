"""Context size of Claude Code sub-agents (orchestrator tool, see docs/WORKFLOW.md "Context limits and handoffs").

  python3 -I tools/agents/agent_ctx.py <agentId>...
      prints the last context size (tokens) of each agent
  python3 -I tools/agents/agent_ctx.py --watch <tokens> [ignoredAgentId...]
      polls every 60 s and exits, printing, when an ACTIVE agent (transcript written in the last 5 min) is above
      <tokens>. It also covers sub-agents spawned by agents. Run it in the background.

It reads only the API `usage` fields of the transcripts (input + cache read + cache creation tokens), never the
text. The file size is useless: screenshots inflate the bytes.
Transcripts: ~/.claude/projects/<main checkout path, non-alphanumerics as '-'>/<session>/subagents/agent-<id>.jsonl
(override the project folder with RTS_CLAUDE_PROJECT). Old sessions are searched too.
"""
import glob
import json
import os
import subprocess
import sys
import time


def project_dir():
    env = os.environ.get("RTS_CLAUDE_PROJECT")
    if env:
        return env
    here = os.path.dirname(os.path.abspath(__file__))
    common = subprocess.run(["git", "-C", here, "rev-parse", "--path-format=absolute", "--git-common-dir"],
                            capture_output=True, text=True, check=True).stdout.strip()
    main_checkout = os.path.dirname(common)
    return os.path.expanduser("~/.claude/projects/" + "".join(c if c.isalnum() else "-" for c in main_checkout))


def transcripts(agent_id="*"):
    return glob.glob(os.path.join(project_dir(), "*", "subagents", "agent-" + agent_id + ".jsonl"))


def context(path):
    last = None
    with open(path, errors="replace") as f:
        for line in f:
            if '"usage"' not in line:
                continue
            try:
                d = json.loads(line)
            except ValueError:
                continue
            u = (d.get("message") or {}).get("usage")
            if not u:
                continue
            v = u.get("input_tokens", 0) + u.get("cache_read_input_tokens", 0) + u.get("cache_creation_input_tokens", 0)
            if v > 0:
                last = v
    return last


def watch(limit, ignored):
    while True:
        now = time.time()
        hits = []
        for p in transcripts():
            if now - os.path.getmtime(p) > 300:
                continue
            agent = os.path.basename(p)[len("agent-"):-len(".jsonl")]
            c = None if agent in ignored else context(p)
            if c and c > limit:
                hits.append(f"agent {agent} context {c} > {limit}")
        if hits:
            print("\n".join(hits))
            return
        time.sleep(60)


def main(args):
    if not args:
        print(__doc__)
        return
    if args[0] == "--watch":
        watch(int(args[1]), set(args[2:]))
        return
    for agent in args:
        files = transcripts(agent)
        print(agent, context(max(files, key=os.path.getmtime)) if files else None)


if __name__ == "__main__":
    main(sys.argv[1:])
