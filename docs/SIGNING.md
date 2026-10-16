# Code signing and Windows warnings

## Current status

The repository prepares a SignPath Foundation release workflow. Enrollment, certificate issuance, protected-environment configuration, and provider approval must be completed before a production-signed release can be created. Development/CI builds remain unsigned. This document does not claim approval or an issued certificate.

Once the project is accepted, free code signing is provided by [SignPath.io](https://signpath.io), with a certificate provided by [SignPath Foundation](https://signpath.org). The certificate publisher is **SignPath Foundation**. Signing verifies publisher and file integrity; it does not promise immediate SmartScreen reputation. [Microsoft explains the distinction](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

## Signing policy

Only the final framework-dependent Windows x64 `OsrsWorldPing.exe`, built from this repository's reviewed `main` branch by GitHub-hosted CI, is eligible. No third-party binaries are submitted. Runtime dependencies are supplied by the installed Microsoft Desktop Runtime.

Contributors propose changes through pull requests. A maintainer reviews source changes, test results, the Windows smoke test, and UI previews before signing. The project administrator manages access and enrollment; an authorized release approver manually approves each production signing request. SignPath account access must use MFA. Configure the actual people and roles in SignPath during enrollment; this repository does not invent additional team members.

The protected GitHub `release-signing` environment must restrict deployment to `main` and require a maintainer review. Configure SignPath's trusted GitHub build integration and matching project policy to reject untrusted refs/artifacts. Signing uses the uploaded build artifact's origin rather than an arbitrary local executable. Missing configuration, failed checks, missing timestamp, an invalid signature, or an unexpected publisher fails the release workflow. There is no unsigned production fallback.

The workflow produces a signed release candidate and a SHA-256 checksum. Publishing a public release is a separate maintainer action after review.

## Enrollment checklist

1. Review [Foundation eligibility and terms](https://signpath.org/terms.html), then [apply](https://signpath.org/apply.html) using the public project URL, Apache 2.0 license, README, this policy, and [Privacy](PRIVACY.md). Document the current maintainer/approver account details directly with the provider.
2. After approval, install/authorize the SignPath GitHub integration for this repository and create its project, production signing policy, trusted-build rules, and artifact configuration from `.github/signpath/artifact-configuration.xml`.
3. Create the protected GitHub `release-signing` environment. Require reviewer approval and `main` only. Store `SIGNPATH_API_TOKEN` as an environment secret. Set environment variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, and `SIGNPATH_POLICY_SLUG` to the provider's actual values. Keep credentials out of the repository and chat.
4. Run **Signed release candidate** from `main`, review the build/UI artifacts, approve the GitHub environment and SignPath request, then inspect the downloaded signed result. The provider must apply a trusted Authenticode timestamp.

Integration follows [SignPath's GitHub documentation](https://docs.signpath.io/trusted-build-systems/github).

## Check a download

Download from the project's releases, open **Properties → Digital Signatures**, and inspect the certificate. Production builds should have a valid SignPath Foundation signature and trusted timestamp. CI testing artifacts may have no signature. You can also run PowerShell:

```powershell
Get-AuthenticodeSignature .\OsrsWorldPing.exe | Format-List Status,SignerCertificate,TimeStamperCertificate
Get-FileHash .\OsrsWorldPing.exe -Algorithm SHA256
```

Compare the hash with the checksum accompanying that exact signed release. An unsigned or modified file is not equivalent to the signed release candidate. A valid signature does not guarantee that Windows SmartScreen will immediately stop showing warnings.
