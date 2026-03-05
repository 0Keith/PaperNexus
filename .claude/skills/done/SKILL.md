---
name: done
description: Create PR, review, merge, and deploy
disable-model-invocation: true
---

Complete feature branch: PR, review, merge, clean up.

## Steps

1. **Verify branch** — create feature branch if on default, carry changes.
2. **Commit uncommitted** — `git status`, stage, commit.
3. **Sync default** — fetch, merge, resolve conflicts.
4. **Build & test** — auto-detect build system, build + test. Fix failures/warnings.
5. **Code comments** — read changed files; *why* comments only, not trivial. Commit if changed.
6. **Check PR** — `gh pr list --head <branch>` (skip to step 8 if found).
7. **Create PR** — `gh pr create`, title <70 chars, `## Summary` + `## Test plan`.
8. **Push branch** — `git push -u origin <branch>`.
9. **Auto-label** — labels: `ui` (views), `api` (backend), `infra` (CI/config), `docs`, `tests`.
10. **Parallel reviews** — 5 agents (read-only): Architecture, Security, Performance, Tests, Code quality.
11. **Post comments** — batch findings by category, note clean reviews.
12. **Fix issues** — write tests, commit, push, re-test until clean.
13. **Doc syncs** — 3 agents: CLAUDE.md, README.md (user behavior), optional README.
14. **Maintenance** — 2 agents: Skill sync, Compact docs (<6000 chars: CLAUDE.md, skills, memory).
15. **Update PR** — `gh pr edit --body`.
16. **Wait CI** — `gh pr checks`, fix failures, re-poll.
17. **Merge** — `gh pr merge --squash --delete-branch`.
18. **Cleanup** — switch default, pull, delete locals, prune.
19. **Deploy** — `gh run list --branch <default> --limit 1`, watch, fix if failed.
20. **Report** — merged changes + PR URL.
21. **Celebrate** — ASCII Pokemon.

## Notes

- $ARGUMENTS = PR title
- Conflicts: resolve, commit, push, re-check CI
- Never force-push or skip CI
- Default branch: `gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name'`
