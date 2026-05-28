---
name: ecc
description: Contextual Engineering Coach methodology for Binexus — apply the ECC framework (working context, agents, hooks, security guide, longform/shortform guides) to a slice or refactor when the standard slice cadence is not enough. Use when the user references "ECC", asks for a structured engineering review with hooks + agents + rules, or when a slice needs the discipline of a long-form spec + a security pass + a working-context log. Pairs with `spec-kit` for spec-driven work and `prompt-improver` for scoping.
---

# ecc (Binexus)

Use the [ECC — Contextual Engineering Coach](https://github.com/Lum1104/ECC) methodology when a slice deserves the heavier framework: working-context tracking, agent definitions, hooks, security guide, longform/shortform guides.

The full ECC repo (vendored locally at [`skills/ECC-main/`](../../../skills/ECC-main/)) is opinionated and large. The upstream installer targets many platforms, but its real value for Binexus is the **methodology + documents**, not the platform-specific shims.

## What ECC gives us

The upstream repo ships these reference documents — all worth reading once, then drawing from when planning a large slice:

| Upstream doc                                                                | What it covers                                         |
| --------------------------------------------------------------------------- | ------------------------------------------------------ |
| [`AGENTS.md`](../../../skills/ECC-main/AGENTS.md)                           | Agent definitions: roles, prompts, guard rails         |
| [`CLAUDE.md`](../../../skills/ECC-main/CLAUDE.md)                           | Per-agent personality + invariants                     |
| [`RULES.md`](../../../skills/ECC-main/RULES.md)                             | Cross-cutting rules                                    |
| [`WORKING-CONTEXT.md`](../../../skills/ECC-main/WORKING-CONTEXT.md)         | Template for tracking what an agent is currently doing |
| [`the-shortform-guide.md`](../../../skills/ECC-main/the-shortform-guide.md) | Short-form spec template                               |
| [`the-longform-guide.md`](../../../skills/ECC-main/the-longform-guide.md)   | Long-form spec template (multi-context, multi-week)    |
| [`the-security-guide.md`](../../../skills/ECC-main/the-security-guide.md)   | Security review framework                              |
| [`EVALUATION.md`](../../../skills/ECC-main/EVALUATION.md)                   | Evaluation rubric for AI-assisted engineering work     |
| [`TROUBLESHOOTING.md`](../../../skills/ECC-main/TROUBLESHOOTING.md)         | Common failure modes                                   |
| [`hooks/`](../../../skills/ECC-main/hooks/)                                 | Pre / post hooks that enforce rules                    |
| [`commands/`](../../../skills/ECC-main/commands/)                           | Slash-command definitions for various agent platforms  |
| [`schemas/`](../../../skills/ECC-main/schemas/)                             | JSON Schemas for ECC artifacts                         |

## When to invoke ECC's methodology

- **Phase kickoff** (e.g. starting F7 Billing or F8 Reporting). The longform guide is the natural template for the phase spec.
- **Security review** of a multi-tenant boundary (e.g. tenant-isolation regression suite, JWT refresh flow, signed presigned MinIO URLs). Use `the-security-guide.md` as the audit checklist.
- **Cross-context refactor** that lasts longer than 3 PRs. Use `WORKING-CONTEXT.md` as the running log so context survives across sessions.
- **External review** (auditor, security firm, due diligence). The ECC docs translate "the way Binexus engineers think" into a vocabulary outside reviewers expect.

Do NOT invoke ECC for:

- Single-PR slices (the standard slice cadence is enough).
- Visual / UI work (use [`impeccable`](../impeccable/SKILL.md) + [`taste`](../taste/SKILL.md)).
- Bug fixes.

## Binexus mapping

ECC concepts map cleanly onto the existing Binexus stack:

| ECC concept       | Binexus equivalent                                                                                                                                               |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Working context   | The slice plan in Plan Mode + `docs/` updates + Notion `Ahora` callout                                                                                           |
| Agents            | This `.cursor/skills/*` tree + Cursor's modes (Agent / Plan / Ask / Debug)                                                                                       |
| Rules             | [`.cursor/rules/*`](../../rules/)                                                                                                                                |
| Shortform guide   | A single `docs/domains/<context>.md` slice section                                                                                                               |
| Longform guide    | Multi-week phase spec — currently lives partially in Notion `Roadmap`. Promote to `docs/phases/<n>-<name>.md` when adopting ECC.                                 |
| Security guide    | [`.cursor/rules/common-security.md`](../../rules/common-security.md) + [`.cursor/rules/typescript-security.md`](../../rules/typescript-security.md) + this skill |
| Evaluation rubric | PR template `Validation` + CI gates + reviewer checklist                                                                                                         |
| Hooks             | Husky pre-commit (when wired via [`setup-pre-commit`](../setup-pre-commit/SKILL.md))                                                                             |

## How to use the ECC docs without installing the CLI

Two practical patterns.

### Pattern 1 — Copy a template, fill it in

Before starting a phase, copy `skills/ECC-main/the-longform-guide.md` (the template) into `docs/phases/<n>-<name>.md` and fill the sections with Binexus specifics. Commit it as the phase spec. From then on, normal slice cadence operates against that spec.

### Pattern 2 — Use the security guide as an audit checklist

When closing a slice that touches a security-sensitive surface (auth, multi-tenant, signed URLs), run through `skills/ECC-main/the-security-guide.md` and document the results in the PR body under a `Security` section. This is heavier than [`.cursor/rules/common-security.md`](../../rules/common-security.md) and reserved for the slices that need it.

## Optional: install the ECC CLI

The upstream ships [`install.ps1`](../../../skills/ECC-main/install.ps1) (Windows) and [`install.sh`](../../../skills/ECC-main/install.sh) (POSIX). The installer expects Node and runs `npm install` in `skills/ECC-main/`, then `node scripts/install-apply.js` to register the integration for a target platform.

For Binexus this is OPTIONAL. The team's MVP path:

1. Use the ECC docs as templates / checklists (Pattern 1 + Pattern 2 above).
2. If, after a phase or two, we want the full agent + hooks integration, run the installer on the developer machine that pilots it. Do NOT mandate it for the whole team until it has paid for itself once.

If you do install:

```powershell
cd skills/ECC-main
./install.ps1
```

The installer auto-runs `npm install` if `node_modules/` is missing. It writes to platform-specific directories (`.cursor/`, `.claude/`, etc.). Review the diff before committing.

## Anti-patterns

- Adopting ECC across the team after one developer experimented. Pilot on one phase first.
- Treating ECC as a replacement for Binexus's own docs. ECC is a methodology layer; `docs/` + Notion remain the durable record.
- Copying ECC's working-context template and never updating it. The working context is the value; an outdated one is noise.

## Reference

- Upstream README: [`skills/ECC-main/README.md`](../../../skills/ECC-main/README.md)
- Upstream Quick Reference: [`skills/ECC-main/COMMANDS-QUICK-REF.md`](../../../skills/ECC-main/COMMANDS-QUICK-REF.md)
- Working-context template: [`skills/ECC-main/WORKING-CONTEXT.md`](../../../skills/ECC-main/WORKING-CONTEXT.md)
- Security guide: [`skills/ECC-main/the-security-guide.md`](../../../skills/ECC-main/the-security-guide.md)
- Longform guide: [`skills/ECC-main/the-longform-guide.md`](../../../skills/ECC-main/the-longform-guide.md)
- [`.cursor/skills/spec-kit/SKILL.md`](../spec-kit/SKILL.md) — alternative spec-driven workflow (GitHub Spec Kit)
- [`.cursor/skills/prompt-improver/SKILL.md`](../prompt-improver/SKILL.md) — scoping companion
