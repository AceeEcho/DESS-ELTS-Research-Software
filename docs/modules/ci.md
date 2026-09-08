# Development CI

The repository workflow runs policy, local documentation links, accepted plan
hashes, generated progress/schema consistency, dependency pins and Python suites
on Windows and Ubuntu. Run its exact entry locally with
`python tools/ci/check.py --baseline`. CI pins Python 3.12.10, available for both
Windows and Ubuntu in the official actions/python-versions manifest. The initial
remote Windows run could not install 3.12.14; that version is the local bundled
baseline, not an available Windows Actions distribution. The local development
host was also tested with 3.14.3.

The separate Unity workflow is manual and requires a provisioned Windows x64
runner labeled `elts-unity-6000.3.23f1`, with that exact licensed Editor and Windows
Mono support. It runs bootstrap, both Unity test suites, a Windows build, then
artifact verification. No runner, license, remote workflow execution, or required
status check is claimed by local validation. Keep the workflows advisory until
their actual remote check names and behavior have been observed.

Workflow files use JSON syntax, a YAML subset, so Python can validate their
structure without adding a YAML dependency. Actions are pinned to full upstream
commits. The token has only `contents: read`, and checkout does not retain its
credential. Unity execution is manual to keep it off unreviewed pull-request
code on a persistent runner. Raw Editor logs stay on the runner; do not publish
them as artifacts without reviewing their contents.

Policy checks editable executable source for machine-specific user paths and
checks tracked assets for metadata. Original accepted documents and immutable
historical evidence retain their original bytes and are checked through their
authority hashes. Documentation checks cover local Markdown file links in the
README, agent guides and module documentation; external links and illustrative
code paths are outside that check.

Reference: [GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).
