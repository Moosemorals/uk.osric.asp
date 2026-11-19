<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A release does not ship the image the tests passed

| | |
| --- | --- |
| Difficulty | persistent-salmon |
| Utility | high |
| Status | blocked |
| Blocked by | `ops-no-ci` |
| Plan | — |
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
| The release | [`Makefile`](../../Makefile) | `deploy` runs `deploy-guard`, `check`, the browser suite, then `image`; it builds the image after the browser suite and pushes `latest` and the commit tag |
| The image proof | [`Makefile`](../../Makefile) | `image-check` builds two images with an empty layer cache, compares their ids and removes them. Nothing keeps the proved image |
| The host gate | [`host-ci`](../plans/host-ci.md) | The proposed `make ci` on every trunk push; it publishes the result through Caddy at `/ci/status.json` and pushes no image |
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

## What this is not

- **Not auto-deploy on green.** The promotion is a command. Auto-deploy is a separate decision with
  its own snapshot, rollback and notification story.
- **Not a second VM.** The host that runs the gate, the browser and the app is the same machine; the
  slice is the control.
- **Not a gate for branch pushes.** Only `trunk` is a release candidate.
- **Not a page or shell feature.** The status file is Caddy's; the app is not given a route it does
  not need.

## What would prove it

The host builds and pushes `registry.osric.uk/uk.osric.asp:<sha>`; the browser suite drives that
image, not a `dotnet` child; the promotion pulls the same image id the status names and restarts the
quadlet with it; the running container's `org.opencontainers.image.revision` label is `<sha>`; a
failed browser run leaves the image unpromoted and the status says so; and the gate, browser and
image are all readable from one `/ci/status.json` served by Caddy.

## The name a plan would propose

- **deploy-promotes-the-tested-image** — the host builds one image, the browser suite drives that
  image, and the release promotes the image id the status names rather than rebuilding the commit.
