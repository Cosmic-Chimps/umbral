

Yes, I understand it well. And yes, I think it solves a real problem for a real audience.

## What Umbral Is

| Concept | Simple Summary |
| --- | --- |
| **Core** | Self-hosted OIDC server with pre-built social connectors |
| **Positioning** | Between "wire OAuth in each app" and "run Keycloak" |
| **User** | Indie devs, homelabbers, small communities |
| **Differentiator** | Simplicity—no realms, roles, SAML, tenants |

## Does It Solve a Real Problem?

**Yes.** Here's why:

### 1. The Pain Is Documented Everywhere
If you search GitHub issues, Reddit r/selfhosted, or hacker news threads, you'll find repeated complaints:
- *"Keycloak is too heavy for my 2 hobby projects"*
- *"I don't want Auth0 billing surprises when my app grows"*
- *"Why do I need to implement Google OAuth in every new repo?"*

The three options you listed (per-app wiring, full IAM platform, or rent it) are indeed the reality today. There's no dedicated "social login only, self-hosted" solution.

### 2. Market Gap Analysis
Looking at the landscape from your document:

| Need | Current Options | Umbral Fit |
| --- | --- | --- |
| Social login in 5 apps | Implement 5× OAuth flows | 1 container, toggle providers once |
| Control user data | Auth0/Clerk (hosted) | Self-hosted |
| Run on modest hardware | Keycloak (Java+Postgres) | Single container + SQLite |
| Learn < 3 concepts | Authentik (flows, realms) | App/Connector/User |

### 3. The Numbers Are Plausible
Your measurable targets hold up under scrutiny:

| Target | Feasible? | Why |
| --- | --- | --- |
| 5 minutes to first login | ✅ | Docker run → enable Discord → configure app is straightforward if documented well |
| 3 screens in admin | ✅ | Apps, Connectors, Users covers core needs |
| 3 config values per app | ✅ | Issuer URL, client ID, client secret is standard OIDC |
| 1 YAML for config-as-code | ⚠️ | Requires careful design but achievable |

The weak spot is **"never touch login again"** — that depends on edge cases like provider API changes, token rotation failures, and account merging. But as a north star, it's good.

## Where Success Depends

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| **Provider breakage** | Medium | Discord/Google change OAuth specs occasionally |
| **Community adoption** | Unknown | Homelab community is passionate but niche |
| **Scope creep pressure** | High | Users will ask: "Can I add MFA?" "Can I add SSO?" |
| **Name trademark** | Medium | Hodor is HBO/Warner Bros IP |

## Honest Assessment

**Umbral works if:**
- You ship early with MVP scope (Discord, GitHub, Google first)
- You resist feature requests that don't match the simplicity principle
- You document well enough that users don't need support tickets

**Umbral struggles if:**
- People discover it and demand enterprise features (RBAC, audit logs, SAML)
- A competitor launches the same thing with better marketing
- Provider APIs become more restrictive on self-hosted callbacks

---

My verdict: **Yes, this fills a gap.** The key insight—that nobody offers *only* social login in a self-hosted, simple form—is correct. Whether it succeeds depends on execution discipline, not idea quality.

Do you want to talk through the MVP scope next, or discuss how to validate demand before building?