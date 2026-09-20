# Task routing

| Task | Read |
| --- | --- |
| Widget layout, tray, or Codex service | `AgentUsageBar.cs`, `docs/design-notes.md` |
| Claude data or bridge | `ClaudeUsageData.cs`, `ClaudeUsageService.cs`, `ClaudeUsageBridge.cs`, `SECURITY.md` |
| Claude setup | `scripts/configure-claude.ps1`, `scripts/claude-statusline.ps1`, `docs/claude-setup.md` |
| Build or regression checks | `build.ps1`, `scripts/test-local.ps1`, `AGENT_PLAYBOOK.md` |
| Screenshots or GIF | `scripts/render-docs-assets.ps1`, `docs/screenshots/README.md` |
| Public copy | `README.md`, `docs/reddit-post.md` |

Only load the files relevant to the current task.
