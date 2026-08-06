---
title: "Cloud Adoption Framework Naming for Green Lantern"
date: 2026-08-05
description: "A practical naming standard for Green Lantern resource groups, Azure Functions, storage accounts, and Key Vaults."
draft: false
---

## Overview

The database question for Green Lantern is simple: can a person look at an Azure resource name and know what it stores, where it runs, and which environment owns it? Cloud Adoption Framework naming gives that question a practical answer. Treat the name as operational metadata: resource type first, then workload, environment, region, and instance where the platform allows it.

{{< storage-diagram path="/diagrams/cloud-adoption-framework-naming.png" alt="Cloud Adoption Framework naming flow for Green Lantern resources" caption="The CI/CD pipeline renders this PlantUML source as a PNG and publishes it with the Hugo site." >}}

For this site, the short workload code is `lantrnfx`. The shared long suffix is `lantrnfx-dev-usw2-001`, built from project, environment, region, and instance. That makes names readable without asking an operator to open tags first. The resource group stays shorter as `rg-lantrnfx-dev`, because resource groups are scanned constantly in portal and CLI output, but the full Cloud Adoption Framework name can still live in tags as `CAFName = lantrnfx-dev-usw2-001`.

The database and storage side needs the most care because Azure naming rules are uneven. A storage account cannot use hyphens, so Green Lantern compresses the same idea into `stlantrnfxdevusw2001`. A Key Vault can keep the normal hyphenated form, `kv-lantrnfx-dev-usw2-001`. The naming rule should not change its meaning just because the platform changes the allowed characters.

For compute, Function Apps should identify the role as well as the platform. The current hello API uses `func-lantrnfx-hello-dev-usw2-001`. If the workload grows, use role segments such as `api`, `jobs`, or `ingest` instead of hiding that meaning in tags. Logs, alerts, dashboards, and deployment summaries are easier to read when the purpose is visible in the name.

The diagram is intentionally source-controlled as PlantUML at `swa/hello-portal/assets/plantuml/cloud-adoption-framework-naming.puml`, while Hugo reads the generated image at `/diagrams/cloud-adoption-framework-naming.png`. CI/CD renders changed `.puml` files, generates missing images on clean runners, and then builds the Hugo site. That keeps the Markdown clean, keeps the diagram editable, and prevents generated PNGs from becoming the source of truth.
