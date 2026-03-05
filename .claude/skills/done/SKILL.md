---
name: done
description: Create PR, review (architecture/security/performance/tests/code), merge, deploy, and clean up
disable-model-invocation: true
---

Complete the current feature branch: create a PR, review, merge, and clean up.

## Steps

1. **Verify branch state**: If on the default branch, create an appropriately named feature branch carrying over all committed/uncommitted changes.

2. **Commit uncommitted changes**: Run `git status`. Stage and commit any uncommitted changes with an appropriate message.

3. **Sync with default branch**: Fetch and merge the remote default branch. Resolve any conflicts.

4. **Build and test locally**: Auto-detect the build system from project files (`CLAUDE.md`, `package.json`, `Cargo.toml`, `Makefile`, etc.) and run build + test commands. Fix warnings in changed files and any test failures. Commit and re-run until clean.

5. **Check for existing PR**: `gh pr list --head <branch>`. Reuse if one exists (skip to step 7).

6. **Create PR**: `gh pr create` targeting the default branch. Title under 70 chars. Body with `## Summary` (bullet points) and `## Test plan`. Derive from `git diff <default>...HEAD` and `git log <default>..HEAD`.

7. **Push the branch**: `git push -u origin <branch>` if not already pushed.

8. **Auto-label PR**: Add labels via `gh pr edit --add-label` based on changed files:
   - `ui` — view/template files (`.axaml`, `.tsx`, `.vue`, `.html`, `.css`)
   - `api` — backend/route files
   - `infra` — CI/CD, config, build files (`.github/`, `.claude/`, `Dockerfile`)
   - `docs` — documentation (`README.md`, `CLAUDE.md`, `CHANGELOG.md`)
   - `tests` — test files
   Create missing labels with `gh label create`. Skip if nothing matches.

9. **Review architecture**: Check the diff (`gh pr diff`) for:
   - Violations of patterns in `CLAUDE.md`; wrong dependency direction; circular refs
   - Unnecessary coupling; concurrency/thread-safety issues; SRP violations
   - Inconsistent public API conventions
   Post concerns as batched PR review comments. Note if clean.

10. **Fix architecture issues**: Commit and push any fixes.

11. **Review security**: Check the diff for:
    - Injection (command, path traversal, SQL), hardcoded secrets, unsafe deserialization
    - HTTP where HTTPS expected, overly broad permissions, unsafe FFI/interop
    - Vulnerable deps (use `dotnet list package --vulnerable`, `npm audit`, `cargo audit`, etc.)
    Post concerns as batched PR review comments. Note if clean.

12. **Fix security issues**: Commit and push any fixes.

13. **Review performance**: Check the diff for:
    - Unnecessary allocations/copies, N+1 patterns, redundant I/O
    - Blocking async calls, expensive ops in hot loops, missing caching
    - Algorithmic inefficiency (O(n²) where O(n log n) is possible)
    Post concerns as batched PR review comments. Note if clean.

14. **Fix performance issues**: Commit and push any fixes.

15. **Review test coverage**: Check that new public APIs have tests, edge cases are covered, and existing tests still match modified behavior. Write missing tests.
    Post concerns as PR review comments. Write tests if needed, commit, and push.

16. **Fix test coverage**: Run the test suite. Commit and push any new/updated tests.

17. **Self-review**: Check the full diff for bugs, style violations, missing error handling, dead code, debug leftovers, TODOs, and naming issues.
    Post concerns as batched PR review comments. Note if clean.

18. **Fix review issues**: Commit and push any fixes.

19. **Simplify**: Run `/simplify` for reuse, quality, and efficiency. Apply fixes, commit, and push.

20. **Sync CLAUDE.md**: Update if project structure, patterns, or architectural decisions changed. Commit and push.

21. **Sync README.md**: Update if user-facing behavior changed. Match the existing tone — keep it fun and witty. Skip if no README exists. Commit and push.

22. **Update changelog**: Add entry under `## Unreleased` in `CHANGELOG.md` using [Keep a Changelog](https://keepachangelog.com) conventions. Match existing format. Skip if no changelog exists. Commit and push.

23. **Compact docs**: Check character counts of `CLAUDE.md` (limit: 15,000 chars) and this skill file `.claude/skills/done/SKILL.md` (limit: 10,000 chars). If either exceeds its limit, compact it — remove redundancy, tighten wording, and consolidate without losing meaning. Commit and push if changes were made.

24. **Update PR description**: `gh pr edit --body` to reflect final state of all changes. Keep `## Summary` and `## Test plan` format.

25. **Wait for CI**: Poll `gh pr checks`. If failures, fix, commit, push, and re-poll.

26. **Merge**: `gh pr merge --squash --delete-branch`. Fallback to `--merge`, then `--rebase`.

27. **Clean up locally**: Switch to default branch, pull, delete feature branch. Prune remotes (`git fetch --prune`) and delete local branches merged to default. Report cleaned branches.

28. **Verify deploy**: Check for CI/CD on default branch (`gh run list --branch <default> --limit 1`). Watch with `gh run watch`. On failure: read logs, create fix branch, restart from step 1. Skip if no deploy workflow.

29. **Report**: Summarize what merged, review fixes applied, and the PR URL.

30. **Celebrate**: Web search for a funny "job's done" meme/gif and include the image URL.

## Notes

- User arguments = PR title: $ARGUMENTS
- Merge conflicts: resolve, commit, push, re-check CI.
- Never force-push or skip CI.
- Detect default branch via `gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name'`.
