# CommandBlock — working conventions

## Workflow (enforced)

Never work on main. Always:
1. `gh issue create` (with a label)
2. Branch `feature/<issue#>_PascalCase` or `fix/<issue#>_PascalCase`
3. `gh pr create` (with a label) — body is Summary + `Closes #<issue>` only
4. Squash-merge + delete branch

## Commits

- Short imperative subject line.
- No AI / Claude attribution. No `Co-Authored-By`, no `🤖 Generated with...`, nothing.

## PRs

- Title mirrors the commit / issue.
- Body: 1–2 sentence summary + `Closes #<issue>`. No test plans, no checklists, no headers.
- Labels: `bug`, `enhancement`, `refactor`, `stale`.

## Versioning

- The version lives in `application.properties` (`<version>`); it's baked into the build.
- Every feature commit bumps the **minor** version and gets a matching annotated git tag `vX.Y.Z`; fixes bump **patch**. Chores/refactors/docs don't bump.
- History is tagged retroactively from `v0.1.0`; current release is `v1.5.0`.

## CLI generators

Use them whenever one exists — `gh issue create`, `gh pr create`, `npx create-expo-app`, `npx expo install`, etc.

## Local dev setup

- `compose.yml` runs CommandBlock with `mock-oauth2-server` + Postgres + SeaweedFS. Ports are fixed in `compose.yml` — edit them if they conflict.
- Server world data is bind-mounted to a host folder (`storage.host_path`, default `/data/servers`); CommandBlock never uses Docker named volumes.
- World backups go to the S3/SeaweedFS bucket configured via the `Backup__*` env vars.
- The Minecraft router listens on `25565` — the only game port to open.

## Migrations

After pulling upstream, run:
```
dotnet ef migrations has-pending-model-changes --project src/CommandBlock.Infrastructure --startup-project src/CommandBlock.API
```
If pending, remove any stale local migration and regenerate:
```
dotnet ef migrations remove --project src/CommandBlock.Infrastructure --startup-project src/CommandBlock.API --force
dotnet ef migrations add <Name> --project src/CommandBlock.Infrastructure --startup-project src/CommandBlock.API
```
Then rebuild the image.
