# Security Policy

## Supported Versions

Only the latest commit on the `main` branch is supported for security fixes.

## Reporting a Vulnerability

**Do not report security vulnerabilities in public GitHub Issues.**

Please use **GitHub Private Vulnerability Reporting**:
1. Go to the repository's **Security** tab
2. Click **Report a vulnerability**
3. Describe the issue with as much detail as possible

We will acknowledge receipt within 7 days and provide an estimated timeline for a fix.

## What Qualifies as a Security Issue

- Credential leakage (API keys written to disk, logs, or error messages)
- Command injection via user-controlled input
- Arbitrary process execution
- Path traversal or injection
- `settings.json` content exposure
- Improper use of Windows Credential Manager (e.g., storing keys in plaintext)

## What to Include in a Report

- Steps to reproduce
- Affected component and version (commit SHA)
- Whether the issue is publicly visible
- Any suggested fixes (optional)

## Response Timeline

We do not offer paid bounties and cannot make binding response-time guarantees. For critical credential-exposure issues, we aim to publish a fix within 14 days of confirmation.
