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
# the editor's flash cookie is not marked Secure over http. Override for a local production rehearsal.
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

# Registry credentials. Podman writes logins to ${XDG_RUNTIME_DIR}/containers/auth.json by default,
# which is lost on reboot, so the pull must use the persistent file. The remote path is absolute: an
# unquoted ~ or $HOME here would be expanded by the local shell before ssh sees it.
AUTHFILE ?= $(HOME)/.config/containers/auth.json
REMOTE_AUTHFILE ?= /home/deploy/.config/containers/auth.json

CONTAINER_BASE_IMAGE := mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# RID of the server. On an ARM (CAX) server: linux-musl-arm64.
CONTAINER_RID ?= linux-musl-x64

# Where `make deploy` sends the image and quadlets.
HOST ?=

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

.PHONY: help build restore test test-core test-runtime test-web test-integration migration user-add user-passwd user-list user-remove run publish image deploy format format-check license-check clean
.DELETE_ON_ERROR:

# ---- Targets -------------------------------------------------------------------------------------

help:
	@printf '%s\n' \
		'build              Build the solution when anything changed (default target).' \
		'restore            Force a NuGet restore (build restores only when inputs change).' \
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
		'run                Run the web app on the default URLs.' \
		'publish            Publish the app to artifacts/publish.' \
		'image              Build the container image into podman.' \
		'deploy             Build, push to $(REGISTRY) and restart on HOST=deploy@server.' \
		'format             Apply formatting and style fixes.' \
		'format-check       Fail if the tree is not formatted.' \
		'license-check      Fail if the tree is not REUSE compliant (needs the reuse tool).' \
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

test-core: build
	$(DOTNET) test $(CORE_TESTS) -c $(CONFIGURATION) --no-build

test-runtime: build
	$(DOTNET) test $(RUNTIME_TESTS) -c $(CONFIGURATION) --no-build

test-web: build
	$(DOTNET) test $(WEB_TESTS) -c $(CONFIGURATION) --no-build

# The host is real: routing, middleware order, exception handling and startup migrations. Slower
# than the other suites, which is why it has its own target rather than living in the unit tests.
test-integration: build
	$(DOTNET) test $(INTEGRATION_TESTS) -c $(CONFIGURATION) --no-build

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

run:
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) $(DOTNET) run --project $(WEB_PROJECT)

publish:
	$(DOTNET) publish $(WEB_PROJECT) -c Release

# The SDK builds the OCI image itself and hands it to podman; there is no Dockerfile.
image:
	$(DOTNET) publish $(WEB_PROJECT) -c Release -r $(CONTAINER_RID) \
		-p:ContainerBaseImage=$(CONTAINER_BASE_IMAGE) \
		-t:PublishContainer -p:ContainerImageTags=$(IMAGE_TAG)

# Pushes the image, installs the quadlets (the source of truth), and restarts. The app runs rootless
# as HOST's user; Caddy is the only rootful unit and is installed with sudo. The quadlet's Image= line
# is rewritten on the way so REGISTRY and IMAGE_TAG overrides reach the host. Both services are
# restarted rather than started, so a tag-only image change or a Caddyfile edit takes effect on a
# redeploy. There is no enable step: quadlet units are transient, so systemctl refuses to enable
# them, and the generator already applies each quadlet's [Install] section on daemon-reload.
deploy: image
	@test -n "$(HOST)" || { echo "usage: make deploy HOST=deploy@your.server"; exit 1; }
	@if grep -qE '^[[:space:]]*(example\.com|admin@example\.com)' deploy/Caddyfile; then \
		echo "deploy/Caddyfile still contains the example.com placeholder"; exit 1; fi
	podman tag $(LOCAL_IMAGE) $(IMAGE_REF)
	podman push --authfile $(AUTHFILE) $(IMAGE_REF)
	ssh $(HOST) podman pull --authfile $(REMOTE_AUTHFILE) $(IMAGE_REF)
	sed 's|^Image=.*|Image=$(IMAGE_REF)|' deploy/uk.osric.asp.container | ssh $(HOST) 'cat > .config/containers/systemd/uk.osric.asp.container'
	ssh $(HOST) sudo install -d -o root -g root -m 0755 /etc/caddy
	ssh $(HOST) 'sudo tee /etc/caddy/Caddyfile > /dev/null' < deploy/Caddyfile
	ssh $(HOST) systemctl --user daemon-reload
	ssh $(HOST) systemctl --user restart uk.osric.asp.service
	ssh $(HOST) 'sudo tee /etc/containers/systemd/caddy.container' < deploy/caddy.container > /dev/null
	ssh $(HOST) sudo systemctl daemon-reload
	ssh $(HOST) sudo systemctl restart caddy.service

format:
	$(DOTNET) format $(SOLUTION)

format-check:
	$(DOTNET) format $(SOLUTION) --verify-no-changes

# Needs the `reuse` tool (apt install reuse, or pipx install reuse).
license-check:
	reuse lint

clean:
	rm -rf $(ARTIFACTS)
