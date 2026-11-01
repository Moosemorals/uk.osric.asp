# SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
# SPDX-License-Identifier: MIT

# uk.osric.asp — task runner. Run `make help` for the menu.
#
# Everything a build produces lives under artifacts/ (UseArtifactsOutput), including the stamps that
# make repeat runs cheap. `make clean` deletes the lot.

.DEFAULT_GOAL := build

# ---- Tooling -------------------------------------------------------------------------------------

DOTNET ?= dotnet
CONFIGURATION ?= Debug

# There is no launchSettings.json, so `dotnet run` would be Production; local runs are Development so
# the session and antiforgery cookies are not marked Secure over http. Override for a local production
# rehearsal.
ASPNETCORE_ENVIRONMENT ?= Development

# ---- Layout --------------------------------------------------------------------------------------

SOLUTION := uk.osric.asp.slnx
ARTIFACTS := artifacts

WEB_PROJECT := src/uk.osric.asp.Web/uk.osric.asp.Web.csproj
CORE_TESTS := test/uk.osric.asp.Core.Tests/uk.osric.asp.Core.Tests.csproj
RUNTIME_TESTS := test/uk.osric.asp.Runtime.Tests/uk.osric.asp.Runtime.Tests.csproj
WEB_TESTS := test/uk.osric.asp.Web.Tests/uk.osric.asp.Web.Tests.csproj
INTEGRATION_TESTS := test/uk.osric.asp.Web.IntegrationTests/uk.osric.asp.Web.IntegrationTests.csproj

# ---- Container image and deployment --------------------------------------------------------------
#
# The image is framework-dependent on Alpine: the runtime lives in shared base layers, so a release
# pushes only the ~180 kB app layer to the registry. Trimming is not an option, MVC does not support it.

REGISTRY ?= registry.osric.uk
IMAGE_REPOSITORY := uk.osric.asp
IMAGE_TAG ?= latest
IMAGE_REF := $(REGISTRY)/$(IMAGE_REPOSITORY):$(IMAGE_TAG)
LOCAL_IMAGE := localhost/$(IMAGE_REPOSITORY):$(IMAGE_TAG)

# The commit the image is, so a pushed image names the tree that made it and a rollback has a target.
GIT_SHA := $(shell git rev-parse --short HEAD 2>/dev/null)
IMAGE_SHA_REF := $(REGISTRY)/$(IMAGE_REPOSITORY):$(GIT_SHA)

# The full commit, which the image records as its revision.
GIT_COMMIT := $(shell git rev-parse HEAD 2>/dev/null)

# Registry credentials. Podman writes logins to ${XDG_RUNTIME_DIR}/containers/auth.json by default,
# which is lost on reboot, so the pull must use the persistent file. The remote path is absolute: an
# unquoted ~ or $HOME here would be expanded by the local shell before ssh sees it.
AUTHFILE ?= $(HOME)/.config/containers/auth.json
REMOTE_AUTHFILE ?= /home/deploy/.config/containers/auth.json

# The base is pinned by digest, here and in deploy/Containerfile: a tag would let the base move
# between two builds of the same commit. `podman image inspect --format '{{json .RepoDigests}}'` lists
# both digests for the tag, and the manifest list is the one that resolves for amd64 and arm64.
CONTAINER_BASE_IMAGE ?= mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:254fe017495996d3abcc487ffacdcce2b73affb380d3af87af12c8dff41fe0b0
# RID of the server. On an ARM (CAX) server: linux-musl-arm64.
CONTAINER_RID ?= linux-musl-x64
# The platform podman builds for, which has to agree with CONTAINER_RID: musl-arm64 is linux/arm64.
CONTAINER_PLATFORM ?= linux/amd64
# The two builds inside `image-check` must not share a layer cache.
IMAGE_CHECK_FLAGS ?= --no-cache
# The commit's own timestamp: fixed for a commit, meaningful in the image, and the reason two builds
# of one commit produce one image. Override to reproduce a build whose commit history has moved.
SOURCE_DATE_EPOCH ?= $(shell git log -1 --format=%ct 2>/dev/null)

# Where `make deploy` sends the image and quadlets, and where the remote-* targets run.
HOST ?=

# The running container on HOST: the quadlet's ContainerName= and the entry assembly the SDK puts in
# /app. Reaching it with `podman exec` reuses the deployment's own storage volume and connection
# string, so the remote-* targets never have to guess an image tag.
CONTAINER_NAME ?= uk.osric.asp
APP_ASSEMBLY := uk.osric.asp.Web.dll

# ---- Incremental restore and build ---------------------------------------------------------------
#
# Two stamps keep repeat runs cheap: one records the last restore, one the last successful build.
# They live inside artifacts/, so `make clean` invalidates both. Everything else depends on `build`
# and passes --no-build, so no other target ever compiles anything.

RESTORE_STAMP := $(ARTIFACTS)/restore.stamp
BUILD_STAMP := $(ARTIFACTS)/build.$(CONFIGURATION).stamp

# Projects are discovered rather than listed so a new one works at any depth.
PROJECT_FILES := $(shell find src test -name '*.csproj' 2>/dev/null)

# Everything that can change what NuGet resolves.
RESTORE_INPUTS := global.json $(SOLUTION) \
	Directory.Build.props Directory.Build.targets Directory.Packages.props \
	$(PROJECT_FILES) $(wildcard NuGet.config nuget.config)

# Everything that can change what the compiler produces. Directory mtimes catch added and deleted
# files, and the output directories are listed so deleting part of artifacts/ forces a rebuild.
BUILD_INPUTS := $(shell find src test -type f 2>/dev/null) \
	$(shell find src test -mindepth 1 -type d 2>/dev/null) \
	global.json .editorconfig \
	Directory.Build.props Directory.Build.targets Directory.Packages.props $(SOLUTION) \
	$(wildcard $(ARTIFACTS)/bin $(ARTIFACTS)/obj)

.PHONY: help build restore check ci drift-check audit hooks test test-core test-runtime test-web test-integration migration user-add user-passwd user-list user-remove remote-user-add remote-user-passwd remote-user-list remote-user-remove run publish image image-check deploy deploy-guard format format-check license-check clean
.DELETE_ON_ERROR:

# ---- Targets -------------------------------------------------------------------------------------

help:
	@printf '%s\n' \
		'build              Build the solution when anything changed (default target).' \
		'restore            Force a NuGet restore (build restores only when inputs change).' \
		'check              The gate: formatting, licences, package audit, every suite, Release publish.' \
		'ci                 Clean tree, the gate, and two image builds that must agree.' \
		'audit              Fail if any NuGet package is vulnerable or deprecated.' \
		'test               Run every suite: core, runtime, web and integration (no browser required).' \
		'test-core          Run the NUnit core tests only.' \
		'test-runtime       Run the NUnit runtime tests only.' \
		'test-web           Run the NUnit web shell and storage tests only.' \
		'test-integration   Run the composed pipeline tests against a throwaway database.' \
		'migration          Add a storage migration with NAME=... (restores local dotnet-ef).' \
		'user-add           Create a local account with ACCOUNT=... (prompts for a password).' \
		'user-passwd        Change an account password with ACCOUNT=...' \
		'user-list          List local accounts.' \
		'user-remove        Remove an account with ACCOUNT=...' \
		'remote-user-add    Same as user-add, in the container on HOST=deploy@server.' \
		'remote-user-passwd Same as user-passwd, in the container on HOST=...' \
		'remote-user-list   Same as user-list, in the container on HOST=...' \
		'remote-user-remove Same as user-remove, in the container on HOST=...' \
		'run                Run the web app on the default URLs.' \
		'publish            Publish the app to artifacts/publish.' \
		'image              Build the container image with podman; the Containerfile is in deploy/.' \
		'image-check        Build it twice and fail if the two builds are not the same image.' \
		'deploy             Gate, build, push to $(REGISTRY) and restart on HOST=deploy@server.' \
		'format             Apply formatting and style fixes.' \
		'format-check       Fail if the tree is not formatted.' \
		'license-check      Fail if the tree is not REUSE compliant (needs the reuse tool).' \
		'hooks              Point git at .githooks, so a push runs the gate.' \
		'clean              Delete the artifacts folder.' \
		'help               Show this list.'

build: $(BUILD_STAMP)

# Forgets the restore stamp first, so the rule below runs even when it looks up to date.
restore:
	@rm -f $(RESTORE_STAMP)
	@$(MAKE) --no-print-directory $(RESTORE_STAMP)

$(RESTORE_STAMP): $(RESTORE_INPUTS)
	$(DOTNET) restore $(SOLUTION)
	@mkdir -p $(dir $@)
	@touch $@

$(BUILD_STAMP): $(BUILD_INPUTS) $(RESTORE_STAMP)
	$(DOTNET) build $(SOLUTION) -c $(CONFIGURATION) --no-restore
	@mkdir -p $(dir $@)
	@touch $@

test: test-core test-runtime test-web test-integration

# A suite that wedges should fail the gate rather than sit in it. NUnit's own timeout would not help
# here: the budget tests burn a wall clock on purpose, so only a generous whole-run ceiling is safe.
TEST_HANG_TIMEOUT ?= 5m
TEST_ARGS := --blame-hang --blame-hang-timeout $(TEST_HANG_TIMEOUT)

# The gate. Cheap checks first, so a formatting mistake fails in seconds rather than after the suites.
# Every part is a target of its own, so this is as runnable on a laptop as it is in CI: there is no
# second definition of "green" to drift from this one. `publish` is here because Release is what
# actually ships — a Release-only break should not wait for a deploy to be discovered.
check: format-check license-check audit test publish

# A clean tree in, a clean tree out. The stamps in artifacts/ make repeat runs cheap, which is also
# how an input that was never committed can hide; this is the one target that starts from nothing.
# It refuses a dirty tree because a build that rewrites a tracked file is indistinguishable from one
# the author has not committed yet — commit or stash first, then run `make ci`.
drift-check:
	@test -z "$$(git status --porcelain)" || { echo "drift-check needs a clean tree: commit or stash first"; git status --short; exit 1; }
	@$(MAKE) --no-print-directory build
	@test -z "$$(git status --porcelain)" || { echo "the build wrote to the working tree; commit it or ignore it"; git status --short; exit 1; }

# What a machine with nothing but this repository, the SDK, reuse and podman runs. No workflow file,
# no service, no secret: whatever eventually runs this is calling one target. It adds the clean tree
# at both ends and the reproducibility proof, which are the two things `check` alone cannot say.
ci: clean
	@$(MAKE) --no-print-directory drift-check
	@$(MAKE) --no-print-directory check
	@$(MAKE) --no-print-directory image-check

test-core: build
	$(DOTNET) test $(CORE_TESTS) -c $(CONFIGURATION) --no-build $(TEST_ARGS)

test-runtime: build
	$(DOTNET) test $(RUNTIME_TESTS) -c $(CONFIGURATION) --no-build $(TEST_ARGS)

test-web: build
	$(DOTNET) test $(WEB_TESTS) -c $(CONFIGURATION) --no-build $(TEST_ARGS)

# The host is real: routing, middleware order, exception handling and startup migrations. Slower
# than the other suites, which is why it has its own target rather than living in the unit tests.
test-integration: build
	$(DOTNET) test $(INTEGRATION_TESTS) -c $(CONFIGURATION) --no-build $(TEST_ARGS)

migration:
	@test -n "$(strip $(NAME))" || { echo "usage: make migration NAME=MigrationName"; exit 1; }
	$(DOTNET) tool restore
	@$(MAKE) --no-print-directory build
	$(DOTNET) tool run dotnet-ef migrations add "$(NAME)" \
		--project $(WEB_PROJECT) --startup-project $(WEB_PROJECT) \
		--configuration $(CONFIGURATION) --no-build --output-dir Storage/Migrations

# ---- Local accounts -------------------------------------------------------------------------------
#
# Accounts exist only through this command; the site has no registration or account UI. The password
# is read from the terminal. For a script, append --password-stdin and pipe one line in.

user-add: build
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make user-add ACCOUNT=name"; exit 1; }
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- users add "$(ACCOUNT)"

user-passwd: build
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make user-passwd ACCOUNT=name"; exit 1; }
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- users passwd "$(ACCOUNT)"

user-list: build
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- users list

user-remove: build
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make user-remove ACCOUNT=name"; exit 1; }
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- users remove "$(ACCOUNT)"

# ---- Remote accounts ------------------------------------------------------------------------------
#
# The same `users` command inside the container that is already serving the site, so it sees the
# deployment's storage volume and connection string; nothing is built or published first. `podman
# exec` needs the unit to be running — for a stopped host use the one-off container in
# deploy/README.md. add and passwd keep a terminal at both ends, because the password prompt is read
# a key at a time and must not echo; list and remove need no terminal.

remote-user-add:
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-add HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-add HOST=deploy@server ACCOUNT=name"; exit 1; }
	ssh -t $(HOST) podman exec -it -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users add '$(ACCOUNT)'

remote-user-passwd:
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-passwd HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-passwd HOST=deploy@server ACCOUNT=name"; exit 1; }
	ssh -t $(HOST) podman exec -it -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users passwd '$(ACCOUNT)'

remote-user-list:
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-list HOST=deploy@server"; exit 1; }
	ssh $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users list

remote-user-remove:
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-remove HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-remove HOST=deploy@server ACCOUNT=name"; exit 1; }
	ssh $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users remove '$(ACCOUNT)'

run:
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) $(DOTNET) run --project $(WEB_PROJECT)

publish:
	$(DOTNET) publish $(WEB_PROJECT) -c Release

# The publish output the Containerfile packs: UseArtifactsOutput puts a RID-specific publish beside
# the plain one, so `make publish` and the image do not overwrite each other.
IMAGE_PUBLISH_DIR := $(ARTIFACTS)/publish/uk.osric.asp.Web/release_$(CONTAINER_RID)

# One line, so `image` and `image-check` can call it without repeating the flags — and so no recipe
# line contains `$(MAKE)`, which make would run even under `-n`.
#
# --timestamp is what makes the build reproducible: without it the config's created time and every
# file mtime in the layer are "now", so two builds of one commit are two different images. The value
# is the commit's own timestamp, so the image says when its source was written and says it the same
# way every time. $(1) is the tag; $(2) carries extra flags, which is how `image-check` forces a real
# build instead of comparing a cache with itself.
image_command = $(DOTNET) publish $(WEB_PROJECT) -c Release -r $(CONTAINER_RID) && podman build $(2) --timestamp $(SOURCE_DATE_EPOCH) --platform $(CONTAINER_PLATFORM) --build-arg BASE_IMAGE=$(CONTAINER_BASE_IMAGE) --build-arg GIT_COMMIT=$(GIT_COMMIT) -t $(1) -f deploy/Containerfile $(IMAGE_PUBLISH_DIR)

# podman packs the publish output; the SDK is not in the image at all.
image:
	$(call image_command,$(LOCAL_IMAGE))

# Reproducibility is a property that regresses silently, so it is a target: two fresh publishes and
# two uncached builds of one commit have to produce one image id. The publish directory is deleted
# between the builds, because an incremental publish leaves the files it already has and their
# mtimes alone — which would let the check compare one set of timestamps with itself.
image-check:
	@rm -rf $(IMAGE_PUBLISH_DIR)
	@$(call image_command,localhost/$(IMAGE_REPOSITORY):reproducible-1,$(IMAGE_CHECK_FLAGS))
	@rm -rf $(IMAGE_PUBLISH_DIR)
	@$(call image_command,localhost/$(IMAGE_REPOSITORY):reproducible-2,$(IMAGE_CHECK_FLAGS))
	@a=$$(podman image inspect --format '{{.Id}}' localhost/$(IMAGE_REPOSITORY):reproducible-1); \
	b=$$(podman image inspect --format '{{.Id}}' localhost/$(IMAGE_REPOSITORY):reproducible-2); \
	podman rmi localhost/$(IMAGE_REPOSITORY):reproducible-1 localhost/$(IMAGE_REPOSITORY):reproducible-2 >/dev/null; \
	if [ "$$a" != "$$b" ]; then \
		echo "the image is not reproducible:"; \
		echo "  build 1: $$a"; \
		echo "  build 2: $$b"; \
		echo "look for a file mtime or a base tag that moves between builds"; \
		exit 1; \
	fi; \
	echo "reproducible: $$a"

# Pushes the image, installs the quadlets (the source of truth), and restarts. The app runs rootless
# as HOST's user; Caddy is the only rootful unit and is installed with sudo. The quadlet's Image= line
# is rewritten on the way, so REGISTRY and the commit tag reach the host. Both services are restarted
# rather than started, so a Caddyfile edit takes effect on a redeploy, and a restart is a reason to
# run this target on its own. There is no enable step: quadlet units are transient, so systemctl
# refuses to enable them, and the generator already applies each quadlet's [Install] section on
# daemon-reload.
#
# The whole gate runs before the image is built, and therefore long before anything is pushed: a tag
# that has been pulled by the server cannot be withdrawn, and a broken suite is cheaper to meet here
# than on the running site. Do not run this target with -j; the order is the point.
deploy: deploy-guard check image
	podman tag $(LOCAL_IMAGE) $(IMAGE_REF)
	podman tag $(LOCAL_IMAGE) $(IMAGE_SHA_REF)
	podman push --authfile $(AUTHFILE) $(IMAGE_REF)
	podman push --authfile $(AUTHFILE) $(IMAGE_SHA_REF)
	ssh $(HOST) podman pull --authfile $(REMOTE_AUTHFILE) $(IMAGE_SHA_REF)
	sed 's|^Image=.*|Image=$(IMAGE_SHA_REF)|' deploy/uk.osric.asp.container | ssh $(HOST) 'cat > .config/containers/systemd/uk.osric.asp.container'
	ssh $(HOST) sudo install -d -o root -g root -m 0755 /etc/caddy
	ssh $(HOST) 'sudo tee /etc/caddy/Caddyfile > /dev/null' < deploy/Caddyfile
	ssh $(HOST) systemctl --user daemon-reload
	ssh $(HOST) systemctl --user restart uk.osric.asp.service
	ssh $(HOST) 'sudo tee /etc/containers/systemd/caddy.container' < deploy/caddy.container > /dev/null
	ssh $(HOST) sudo systemctl daemon-reload
	ssh $(HOST) sudo systemctl restart caddy.service
	@echo "waiting for /heath"
	@ssh $(HOST) 'i=0; until curl -fsS -o /dev/null http://127.0.0.1:8080/heath; do \
		i=$$((i+1)); \
		if [ $$i -ge 30 ]; then \
			echo "the site is not healthy 60s after the restart: journalctl --user -u uk.osric.asp.service"; \
			exit 1; \
		fi; \
		sleep 2; \
	done'
	@echo "deployed $(GIT_SHA)"

# Everything the deploy destination insists on, checked before the gate rather than after it. An
# image pushed from an uncommitted tree cannot be rebuilt from the commit its tag names, which is
# also the only thing a rollback can rely on; ALLOW_DIRTY=1 is the deliberate exception.
deploy-guard:
	@test -n "$(strip $(HOST))" || { echo "usage: make deploy HOST=deploy@your.server"; exit 1; }
	@test -n "$(strip $(GIT_SHA))" || { echo "not a git checkout: there is no commit to tag the image with"; exit 1; }
	@if [ -z "$(strip $(ALLOW_DIRTY))" ] && [ -n "$$(git status --porcelain)" ]; then \
		echo "uncommitted changes: commit them first, or pass ALLOW_DIRTY=1"; \
		git status --short; exit 1; fi
	@if grep -qE '^[[:space:]]*(example\.com|admin@example\.com)' deploy/Caddyfile; then \
		echo "deploy/Caddyfile still contains the example.com placeholder"; exit 1; fi
	@ssh -o ConnectTimeout=10 $(HOST) true || { echo "cannot reach $(HOST)"; exit 1; }

# Needs the network: the advisory data comes from the package feed, not from the lock file.
# `dotnet list package` exits 0 whether or not it finds anything, so the wording is what is checked.
audit:
	@out=$$($(DOTNET) list $(SOLUTION) package --vulnerable --include-transitive); \
	if printf '%s\n' "$$out" | grep -q 'has the following'; then \
		printf '%s\n' "$$out"; echo "vulnerable packages: update or remove them"; exit 1; fi
	@out=$$($(DOTNET) list $(SOLUTION) package --deprecated); \
	if printf '%s\n' "$$out" | grep -q 'has the following'; then \
		printf '%s\n' "$$out"; echo "deprecated packages: update or remove them"; exit 1; fi
	@echo "no vulnerable or deprecated packages"

# A hook is only worth having if it is installed; this is the install step, not the hook.
hooks:
	git config core.hooksPath .githooks
	@echo "a push now runs make check; git push --no-verify skips it"

format:
	$(DOTNET) format $(SOLUTION)

format-check:
	$(DOTNET) format $(SOLUTION) --verify-no-changes

# Needs the `reuse` tool (apt install reuse, or pipx install reuse).
license-check:
	reuse lint

clean:
	rm -rf $(ARTIFACTS)
