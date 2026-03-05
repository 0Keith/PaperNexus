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

5. **Review code comments**: Read all changed files (`git diff <default>...HEAD --name-only`). Ensure non-obvious logic has concise comments explaining *why*, not *what*. Add comments between logical blocks of code to describe intent — always with a blank line or opening brace before the comment for readability. Remove stale or misleading comments. Don't over-comment trivial code. Commit if changes were made.

6. **Check for existing PR**: `gh pr list --head <branch>`. Reuse if one exists (skip to step 8).

7. **Create PR**: `gh pr create` targeting the default branch. Title under 70 chars. Body with `## Summary` (bullet points) and `## Test plan`. Derive from `git diff <default>...HEAD` and `git log <default>..HEAD`.

8. **Push the branch**: `git push -u origin <branch>` if not already pushed.

9. **Auto-label PR**: Add labels via `gh pr edit --add-label` based on changed files:
   - `ui` — view/template files (`.axaml`, `.tsx`, `.vue`, `.html`, `.css`)
   - `api` — backend/route files
   - `infra` — CI/CD, config, build files (`.github/`, `.claude/`, `Dockerfile`)
   - `docs` — documentation (`README.md`, `CLAUDE.md`, `CHANGELOG.md`)
   - `tests` — test files
   Create missing labels with `gh label create`. Skip if nothing matches.

10. **Parallel reviews**: Launch **all five** review agents simultaneously in a **single message** with five parallel Agent tool calls. Each agent receives the PR diff (`gh pr diff`) and `CLAUDE.md` as context. Each agent is **read-only**. Use `subagent_type: "general-purpose"` for each.

   - **Architecture**: violations of `CLAUDE.md` patterns, wrong dependency direction, circular refs, coupling, concurrency, SRP, inconsistent APIs
   - **Security**: injection, hardcoded secrets, unsafe deserialization, HTTP vs HTTPS, broad permissions, unsafe FFI. Run vulnerability scanner (`dotnet list package --vulnerable` / `npm audit` / `cargo audit`)
   - **Performance**: unnecessary allocations, N+1, redundant I/O, blocking async, expensive hot-loop ops, missing caching, algorithmic inefficiency
   - **Test coverage**: new public APIs without tests, uncovered edge cases, stale tests
   - **Code quality**: bugs, style violations, missing error handling, dead code, debug leftovers, TODOs, naming

   Each returns findings with file paths and line numbers, or "No issues found."

11. **Post review comments**: Batch findings by category per agent. Note clean reviews.

12. **Fix all review issues**: Address findings, write missing tests. Commit, push, re-test until clean.

13. **Parallel doc syncs**: Launch agents simultaneously in a **single message**. Each reads PR diff, updates target if needed, commits and pushes. Use `subagent_type: "general-purpose"`.
    - **CLAUDE.md sync**: Update if structure/patterns/architecture changed. Skip otherwise.
    - **README.md sync**: Update if user-facing behavior changed (match existing tone). Skip if no README or nothing changed.

14. **Parallel maintenance**: Launch simultaneously in a **single message**. Use `subagent_type: "general-purpose"`.
    - **Skill sync**: Reflect on this run. Update SKILL.md if steps were ambiguous, wrong, missing, or improvable. Commit and push if changed.
    - **Compact docs**: Check char counts of context `.md` files (CLAUDE.md, skills, memory). Each must stay under **3% of token context window**. Compact if needed without losing meaning. Commit and push if changed.

15. **Update PR description**: `gh pr edit --body` to reflect final state. Keep `## Summary` and `## Test plan`.

16. **Wait for CI**: Poll `gh pr checks`. Fix failures, commit, push, re-poll.

17. **Merge**: `gh pr merge --squash --delete-branch`. Fallback to `--merge`, then `--rebase`.

18. **Clean up locally**: Switch to default branch, pull, delete feature branch. Prune remotes (`git fetch --prune`) and delete merged local branches. Report cleaned branches.

19. **Verify deploy**: Check CI/CD on default branch (`gh run list --branch <default> --limit 1`). Watch with `gh run watch`. On failure: read logs, create fix branch, restart from step 1. Skip if no deploy workflow.

20. **Report**: Summarize what merged, review fixes applied, and the PR URL.

21. **Celebrate**: Draw a funny ASCII art of a pokemon saying random phrase like "good job" or "job's done".

## Notes

- User arguments = PR title: $ARGUMENTS
- Merge conflicts: resolve, commit, push, re-check CI.
- Never force-push or skip CI.
- Detect default branch via `gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name'`.
