# Agent Usage Bar

A small Windows Forms utility for displaying Codex and Claude Code subscription usage.

Read `AGENT_PLAYBOOK.md`, then `CURRENT_STATE.md` and `TASK_ENTRYPOINTS.md` only as
needed. Stop reading when there is enough context for the task.

- Preserve the local-only storage model and the boundaries in `SECURITY.md`.
- Never read, commit, request, or log provider credentials or conversation files.
- Claude data must pass through the numeric allowlist in `ClaudeUsageData.cs`.
- Use a separate branch and worktree for implementation. Preserve unrelated work.
- Validate locally, commit the intended changes, and open a pull request.
- Do not merge, release, deploy, or delete worktrees without authorization.
- No GitHub-hosted runners. No public-fork execution on a personal runner.
- Keep generated executables, settings, account data, and private screenshots out of Git.

Update setup, security, and behavior docs when those contracts change.
