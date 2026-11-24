<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A release does not ship the image the tests passed

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | high |
| Status | open |
| Blocked by | — |
| Area | Deploy, testing, Makefile |
| Location | `test/uk.osric.asp.Web.BrowserTests/BrowserHost.cs`, `Makefile`, `deploy/` |
| Spike | — |

## Idea

The browser suite starts the app from the build output with `dotnet <assembly>`
([`BrowserHost`](../../test/uk.osric.asp.Web.BrowserTests/BrowserHost.cs)), and
[`make deploy`](../../Makefile) runs it before it builds the image. The image that ships is therefore
a rebuild of the tree the browser drove, not the image the browser drove. Reproducibility makes that
likely harmless, but "the deployed image is the image that passed the tests" is a property a release
should have rather than infer. The host's `make ci` currently builds two images for `image-check` and
discards both, so the one artifact that is proved is the one nobody keeps.

The release also does not answer the two questions the owner asks of it: **was this commit's gate
green?** and **is the running image the one that passed?** `make deploy` runs its own gate and waits
for `/health`; it does not fetch `/ci/status.json`, and it does not compare the running container's
image id with the image it pushed. The promotion should do both, not infer them.

The owner's direction is build once and promote on command:

- after a green gate, the host builds **one** image and pushes the commit tag;
- the browser suite runs **downstream of the image build**, against that image, so the image that
  passes is the image that ships;
- a release is promoted on command: verify the commit's gate, browser and image status, pull the
  tag, take a snapshot when the release could migrate, rewrite the quadlet, restart and wait for
  `/health`;
- `ci` gets a push-only registry credential, and `deploy` keeps its pull credential. The registry is
  the handoff; the `ci` account still has no sudo and no deploy authority.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| The browser host | [`BrowserHost.cs`](../../test/uk.osric.asp.Web.BrowserTests/BrowserHost.cs) | Starts the built app as a `dotnet` child process with a throwaway SQLite file, a scripted provider and a loopback port. It never starts the image |
| The release | [`Makefile`](../../Makefile) | `deploy` runs `deploy-guard`, `check`, the browser suite, then `image`; it builds the image after the browser suite, pushes `latest` and the commit tag, and waits for `/health`. It does not read `/ci/status.json` and does not compare the running container's image id |
| The status | [`check-push`](../../deploy/ci/check-push), Caddy | `/ci/status.json` carries the commit, ref, result, times, exitCode and summary. There is no image id, and nothing in the deploy reads the file |
| The image proof | [`Makefile`](../../Makefile) | `image-check` builds two images with an empty layer cache, compares their ids and removes them. Nothing keeps the proved image |
| The host gate | [`host-ci`](../plans/archive/host-ci.md) | The `make ci` on every trunk push, installed by the merged host plan; it publishes the result through Caddy at `/ci/status.json` and pushes no image. The host install is the post-merge step |
| The registry | [deploy/README.md](../../deploy/README.md#the-registry) | `registry.osric.uk`; the deploy user has a persistent pull auth file. The `ci` user has no registry credential today |

## Open questions

These are asked before a plan is written; the first is the one that decides the shape.

1. **How does the browser harness start the candidate image?** The likely answer is a podman
   container with a throwaway volume, `ASPNETCORE_ENVIRONMENT=Staging`, the same assistant and storage
   settings, and the scripted provider reachable from the container (`host.containers.internal`, or
   `--network=host`). The account seed becomes `podman run --rm … users add …` or `podman exec`. The
   harness needs a container mode; the question is whether the same `BrowserHost` can own both modes
   or whether the suite gets a second host.
2. **Does the browser suite run after every green push, or only when a promotion asks?** Running it
   against the image on every push adds the five to ten minutes it takes on the host to every push;
   running it only at promotion keeps the gate cheap and makes the promotion wait for a browser run.
3. **Does `make deploy` become `make promote`, or is `promote` a new target?** The existing `deploy`
   runs `check` and builds the image locally. The promotion trusts the host's status and pulls the
   host's image; the release command should say which it is.
4. **What does `/ci/status.json` carry?** The gate result, the browser result for the image, the
   image id and the commit are the candidates. The promotion should refuse a pulled image whose id
   does not match the status.
5. **Does the host push `latest` with the commit tag, or only the commit tag?** The quadlet names the
   commit, so `latest` is convenience; it is also a tag that can quietly move.
6. **What does the failure look like?** The image is in the registry but the browser suite fails. The
   status says failed, the promotion refuses it, and the image stays unreferenced until the next
   build. An automatic rollback is not part of this item.
7. **Does the promotion wait for `running`, or refuse it?** The host check is asynchronous. A
   promote command could poll until the status is `passed` or `failed`, or stop with "the host is
   still checking" and let the owner try again. Waiting is convenient; refusing is honest.
8. **What does the status carry for the image?** The `imageId` the host proved, the commit label and
   the pushed tag. The promotion pulls that id rather than rebuilding.
9. **What does "live" mean?** After the restart, `podman inspect uk.osric.asp --format '{{.Image}}'`
   equals the status's `imageId`, and the image's `org.opencontainers.image.revision` label is the
   commit. The quadlet's `Image=` names the commit tag.
10. **What if the status names another commit?** A host behind the checkout, or a deploy from a
    branch, means the status is not about this commit. The promotion should refuse, or wait for the
    host to check the commit it is about to deploy.

## What this is not

- **Not auto-deploy on green.** The promotion is a command. Auto-deploy is a separate decision with
  its own snapshot, rollback and notification story.
- **Not a second VM.** The host that runs the gate, the browser and the app is the same machine; the
  slice is the control.
- **Not a gate for branch pushes.** Only `trunk` is a release candidate.
- **Not a green light from another commit.** The status must name the commit being promoted; a
  status for a different commit is not evidence for this one.
- **Not a page or shell feature.** The status file is Caddy's; the app is not given a route it does
  not need.

## What would prove it

The host builds and pushes `registry.osric.uk/uk.osric.asp:<sha>`; the browser suite drives that
image, not a `dotnet` child; the promotion refuses a commit whose status is not `passed` or waits
while it is `running`; a status for another commit is a clear refusal; the status's `imageId` is the
image the promotion pulls and restarts the quadlet with; after the restart, the running container's
image id is that id and its `org.opencontainers.image.revision` label is `<sha>`; a failed browser run
leaves the image unpromoted and the status says so; and the gate, browser and image are all readable
from one `/ci/status.json` served by Caddy.

## The name a plan would propose

- **deploy-promotes-the-tested-image** — the host builds one image, the browser suite drives that
  image, and the release promotes the image id the status names rather than rebuilding the commit.
