# GitHub Environment: `dev` for `green-lantern`

This repository uses the GitHub Actions environment named `dev` for the
development deployment flow in `green-lantern`.

## Purpose

- Scope the `Deploy to dev` workflow to the `dev` environment
- Keep the dev deployment target explicit in the repository
- Support trunk-based deploys from `main`
- Make the workflow-to-environment mapping easy to find in git

## Workflow mapping

- Workflow: [`.github/workflows/deploy-to-dev.yaml`](../workflows/deploy-to-dev.yaml)
- Job environment: `dev`

## Deployment target

- Subscription name: `acestus`
- Subscription ID: `df64929f-810d-4176-8097-35cd05cae10d`
- Azure Managed Identity: `umi-mgmt-dev-scus-ctl`
- Environment name used by GitHub OIDC federation: `dev`

## Notes

- The actual GitHub Environment settings live in repository settings, not in git.
- This file is the repo-tracked source of truth for the `dev` deployment environment name and target in `green-lantern`.
