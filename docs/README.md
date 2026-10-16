# Project documentation — start here

All durable knowledge about this project lives in this folder: how the game works, what has been done, what comes
next, how the AI team works, what was learned on the way. Nothing important may live only in an agent's private
memory, in a scratch folder or in a chat. If it would help the next person (the owner, a new agent, the next
orchestrator session), it belongs here.

## The documents

| File | Answers | Read it when |
|---|---|---|
| [ROADMAP.md](ROADMAP.md) | What is done, what is in progress, what comes next. The "▶ NEXT STEPS" section at the top is the current state. | Always first. |
| [ARCHITECTURE.md](ARCHITECTURE.md) | How the code works: threads, tick pipeline, data model, each game system, the client, the contracts between modules. | Before changing code in an area: read that area's section. |
| [HISTORY.md](HISTORY.md) | Each development wave: what each agent was asked to build (the briefs of the current wave) and what it delivered: features, how to try them, tests, measured numbers, known issues at merge time. | To see what is being built now, where a feature came from, or what was already tried. |
| [WORKFLOW.md](WORKFLOW.md) | How the project is built by AI agents: owner decisions, orchestrator, waves, worktrees, merges, context limits, usage limits, how to resume. | If you orchestrate agents, or want to know how the work is organized. |
| [AGENT_RULES.md](AGENT_RULES.md) | The rules block that goes into every agent prompt. | When writing an agent prompt. |
| [DEV_GUIDE.md](DEV_GUIDE.md) | Practical know-how: building/testing when agents share a worktree, shell quirks, sim pitfalls learned the hard way, perf checks and reference numbers, the tools in `tools/`. | Before your first build in a worktree; when something behaves strangely. |
| [AI_TUNING.md](AI_TUNING.md) | How the skirmish AI is measured (hundreds of headless games), the standard test matrices, and every tuning result so far. | Before touching the AI or game balance. |
| [tokens/](tokens/README.org) | (Org mode, in Italian) The token ledger: how many tokens each session and agent used and for which purpose, by area, purpose and day. Filled automatically by project hooks. | Before planning a wave or sizing a brief; when the owner asks what something cost. |
| [LESSONS.org](LESSONS.org) | (Org mode, in Italian) The design and architecture errors made so far, in order, each with the solution that held and its commit; and the "Costi" chapter: token cost per module, per goal and per kind of work, with what it teaches. | Before a design choice, to see whether it already failed; before planning a wave, to size it. |

The repo root has [CLAUDE.md](../CLAUDE.md): commands and the hard rules every agent must follow. It is loaded
automatically into every agent session, so it stays short and points here.

## Reading order

- **Owner**: this page → ROADMAP "NEXT STEPS" → HISTORY (latest wave) → WORKFLOW "Owner decisions".
- **New agent working on one area**: CLAUDE.md → ROADMAP → ARCHITECTURE (your area in full, skim the rest) →
  DEV_GUIDE. AI agents also read AI_TUNING.
- **Orchestrator (a new session)**: CLAUDE.md → ROADMAP "NEXT STEPS" → WORKFLOW (especially "Resuming") →
  AGENT_RULES.

## Where new knowledge goes

Put each thing in exactly one place and link to it from elsewhere. Don't copy the same text into several files.

| Kind of knowledge | Goes in |
|---|---|
| Current state, next steps, todo items, "later" follow-ups | ROADMAP.md |
| How a system works, its data, its contract with other modules | ARCHITECTURE.md (the section of that area) |
| An agent's brief, what it delivered, numbers measured at merge time, known issues at merge time | HISTORY.md (one section per agent) |
| A decision by the owner, a change to how agents work | WORKFLOW.md |
| Rules every agent prompt must contain | AGENT_RULES.md |
| A pitfall, a tool, a build trick, a reference benchmark number | DEV_GUIDE.md |
| AI/balance experiments and their results | AI_TUNING.md |
| Token cost of sessions and agents (automatic), purpose tags | `tokens/` (`ledger.py tag`, see tokens/README.org) |
| A command or a hard rule every agent needs at all times | CLAUDE.md (keep it short) |

What stays out of `docs/`: transient working notes, such as an agent's handoff note
(`<worktree>/.claude/handoff.md`), agent ids, raw logs and screenshots. When a piece of work ends, the lasting parts
of those notes move here.
