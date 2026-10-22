# CI/CD, explained

This document explains the GitHub Actions work on the `feature/stage4-gaps-cicd-observability` branch, written for someone new to CI/CD. Everything here was checked against the real files. Where something could only be checked on GitHub itself, it says so.

## 1. The big picture

- **CI (continuous integration)**: every time someone pushes code or opens a pull request, a robot builds the project and runs all the tests. If anything fails, everyone sees it before the code is merged.
- **CD (continuous delivery)**: when code is merged into `main`, the robot also builds the Docker images and uploads them to a registry, so there is always a ready-to-run package of exactly that commit.
- **GitHub Actions** is GitHub's built-in robot. A **workflow** is a recipe file in `.github/workflows/`. It is made of **jobs** (each runs on a fresh virtual machine, called a runner) and each job is a list of **steps**.

The recipe is `.github/workflows/ci.yml`.

## 2. The jobs, step by step

| Job | Brief step | What it does | Why |
|---|---|---|---|
| Build and test | 1, 2 | Restore, build in Release, start the Service Bus emulator, run all tests, upload results | A failing test fails the job. Test results are kept as a downloadable artefact |
| Migration check | 3 | `dotnet ef migrations has-pending-model-changes` | Catches "I changed a model but forgot to add a migration" |
| Docker build | 4 | Builds the app image and the migrator image without pushing | A broken Dockerfile is caught before merge |
| Container smoke test | smoke test in CI | Runs `scripts/smoke-test.ps1` | Proves the whole compose stack really starts and behaves |
| Publish images | 7 | Pushes images to `ghcr.io` | Only after all the other jobs pass, and only for `main` and `v*` tags |

### Caching

NuGet packages are stored with `actions/cache`, keyed on a hash of the `.csproj` files. When the dependencies have not changed, the next run restores them instead of downloading them again. The Docker builds use the GitHub Actions cache for their layers for the same reason.

### Integration tests: compose or Testcontainers?

Some tests talk to a real Service Bus emulator. The emulator needs a queue definition file mounted into it. GitHub's built-in "service containers" cannot mount a file from your repository, so there are two realistic choices:

| | Compose in the job (chosen) | Testcontainers |
|---|---|---|
| Source of truth for the emulator | The same `compose.yaml` and `Config.json` used locally | A second description inside the test code |
| Code changes | None | New NuGet packages and test setup code |
| `dotnet test` on its own | Needs the emulator started first (documented in the README) | Starts its own containers |
| Startup time in CI | About a minute for the emulator | Similar |

Compose was chosen because the pipeline then tests against exactly the same emulator setup a developer uses. The cost is that running the tests on your own machine needs the emulator started first.

The tests use their own queue (`order-placed-tests`), so even a developer's running stack cannot steal their messages.

### The version in the image

`Dockerfile` has `ARG COMMIT_SHA`, turned into the environment variable `Version__Commit`. The app binds it to `VersionOptions` and serves it on `GET /version`. The workflow passes `github.sha` (the full commit hash). A local build says `unknown`. This was verified locally: an image built with `COMMIT_SHA=deadbee` answered `{"commit":"deadbee"}`.

### Publishing

On a push to `main` or a tag starting with `v`, the `publish` job logs in to `ghcr.io` with the built-in `GITHUB_TOKEN` and pushes two images:

- `ghcr.io/birasadivine/ecommerce_backend-api` (the api and the consumer, same image)
- `ghcr.io/birasadivine/ecommerce_backend-migrator`

Tags: `sha-` plus the 7-character commit SHA always (for example `sha-b46ef00`), plus `1.2.0` when the tag `v1.2.0` is pushed. There is no `latest` tag, because `latest` does not tell you which commit you are running.

### Secrets

- Nothing secret is written in the workflow or the repository.
- The jobs that start containers run `scripts/make-ci-env.sh`, which creates a `.env` with random passwords for that run only, and tells GitHub to hide them in the logs (`::add-mask::`).
- Publishing uses `GITHUB_TOKEN`, which GitHub creates for each run. The workflow gives it `packages: write` only in the publish job and `contents: read` everywhere else.
- Every third-party action is pinned to a full commit SHA (the tag is in a comment). A tag can be moved by whoever owns the action; a commit SHA cannot.

## 3. What was verified and what was not

**Verified on this machine:** the exact commands of the build-and-test job (Release build, 107 of 107 tests, `.trx` file written); the migration check ("No changes have been made to the model"); both Docker images build; `/version` reports the build argument; the smoke test passes; the workflow file is valid YAML and every action is pinned to a 40-character SHA; the CI `.env` generator replaces every placeholder.

**Not verified (only GitHub can do it):** that the workflow actually runs green on GitHub's runners, that the caches hit, that the push to `ghcr.io` works and the package is visible, that branch protection blocks a failing pull request. The manual checklist in the final summary covers these.

## 4. Ten likely questions

1. **Why does a failing test fail the workflow?** `dotnet test` exits with a non-zero code when a test fails, and a non-zero exit code fails the step and the job.
2. **Why compose and not Testcontainers?** The emulator needs a mounted config file; compose reuses the repo's own file, so there is one source of truth and no code changes.
3. **Why a separate migration check?** Tests use an in-memory database, which never notices a missing migration. The check compares the model with the migration history.
4. **Why build Docker images on pull requests if nothing is pushed?** To catch a broken Dockerfile before it reaches `main`.
5. **Why pin actions to a SHA?** A version tag can be re-pointed to different code. A commit hash cannot, so a compromised action cannot silently change what runs.
6. **Why no `latest` tag?** It moves with every publish and says nothing about the commit. SHA and version tags are exact.
7. **Where are the registry credentials?** There are none stored. The workflow uses the temporary `GITHUB_TOKEN` that GitHub issues for each run.
8. **Why does the publish job need the other jobs?** `needs:` makes it wait for them, so an image is only published from a commit that built, passed its tests, had no pending migrations and passed the smoke test.
9. **How do I know which commit a running container is?** `GET /version` returns the commit it was built from.
10. **What stops someone merging a broken change?** Branch protection on `main` that requires the four checks. This is a GitHub setting, not part of the repository, so it has to be switched on by hand (see the checklist).
