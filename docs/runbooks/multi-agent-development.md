<!-- agentops:policy:start -->
# Multi-Agent Development - Agent Usage Bar

This is the canonical parallel-development policy for this repository. A
stricter repository, security, release, or validation rule still wins.

## Task isolation

- Every writing task owns one branch and one worktree. Never share a coding
  checkout between tasks or agents.
- Invoke `$isolated-git-task-workflow` before implementation. A continuation
  resumes its recorded worktree; desktop/cloud worktrees are not nested.
- Preserve dirty shared checkouts. Do not stash, reset, clean, adopt, or delete
  another task's work.
- Declare likely shared files or cross-repository dependencies before coding.


## Status without agent chatter

At task start and important transitions, run the deterministic `agentops.py`
commands from `$repo-initialization`. Record `active`, `pr-open`,
`ready`, `integration`, `verified`, and `cleanup-ready` as they become true.
These commands write local JSON and make no LLM calls.

## Default task delivery

- A scoped implementation request defaults to: isolate, implement, run the
  focused checks, commit only the intended diff, push the task branch, open a
  pull request, then stop before merge.
- The implementation request is standing approval for that bounded commit,
  push, and pull-request handoff. `local only` or `do not publish` disables it.
- If implementation is complete but an external gate cannot run, publish only
  a clearly labelled draft PR, record the limitation, and mark the task
  `blocked` rather than `ready`.
- Never stage mixed or ambiguously owned files. Never self-merge, deploy,
  release, mutate providers or databases, change remotes or branch protection,
  or clean a worktree as part of feature implementation.
- Use self-hosted runners only. Never add GitHub-hosted runner labels.
- Do not call a task ready when required checks were not run or did not pass.

## Integration lane

- Start one dedicated integration task and invoke `$bring-to-prod` with exact
  repository and pull-request numbers. It processes only that bounded set
  against freshly fetched `main`, one pull request at a time and in dependency
  order.
- The named-PR invocation authorizes ordinary Git integration, cumulative
  verification, and guarded removal of only those tasks' eligible local
  worktrees. It does not authorize deployment, release, provider/database
  mutation, remote-branch deletion, or branch-protection changes.
- After each merge, update remaining work against the new base and rerun the
  affected gate. Run the full product gate after the batch.
- Stop on conflicts, changed remote state, failed proof, or missing approval.
- Worktrees prevent file contamination, not logical conflicts in shared APIs,
  schemas, navigation, design tokens, or dependency files.

## Housekeeping

- Mark `cleanup-ready` only after the integrated result was tested and the task
  worktree contains no unique useful state.
- Housekeeping may automatically create a cleanup preview. Removal requires
  the exact unexpired preview and either an explicit cleanup instruction or
  the authorization carried by an exact-PR `$bring-to-prod` invocation.
- Never force-remove a dirty, unmerged, unpublished, active, or unproven
  worktree. Leave a branch when branch-deletion proof is weaker.

## Done

Report the worktree, branch, base, changed scope, verification evidence,
remaining risks, pull-request state, and cleanup readiness. Update the
repository's existing current-state, playbook, task-routing, feature, PRD, or
ADR docs only when their canonical facts changed.
<!-- agentops:policy:end -->
