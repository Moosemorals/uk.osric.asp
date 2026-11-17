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

# The deployed commit's history, newest first: the file `/admin/changes` serves. The running image has
# no .git, so `make changelist` writes it and the build packs it.
CHANGELIST := $(ARTIFACTS)/changes.txt

WEB_PROJECT := src/uk.osric.asp.Web/uk.osric.asp.Web.csproj
CORE_TESTS := test/uk.osric.asp.Core.Tests/uk.osric.asp.Core.Tests.csproj
RUNTIME_TESTS := test/uk.osric.asp.Runtime.Tests/uk.osric.asp.Runtime.Tests.csproj
WEB_TESTS := test/uk.osric.asp.Web.Tests/uk.osric.asp.Web.Tests.csproj
INTEGRATION_TESTS := test/uk.osric.asp.Web.IntegrationTests/uk.osric.asp.Web.IntegrationTests.csproj
BROWSER_TESTS := test/uk.osric.asp.Web.BrowserTests/uk.osric.asp.Web.BrowserTests.csproj

# Work happens in a branch and a worktree beside the repository, so two agents never share a
# checkout. The folder takes its name from the main worktree, so it is found from any of them.
MAIN_WORKTREE := $(shell git worktree list --porcelain 2>/dev/null | sed -n '1s/^worktree //p')
WORKTREES := $(MAIN_WORKTREE).worktrees
BASE ?= trunk

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

# The base is pinned by its arm64 image digest: a tag would let the base move between two builds of
# the same commit. MCR does not resolve its manifest-list digest, so an x86 build must override
# CONTAINER_BASE_IMAGE with the matching amd64 image digest. `-extra` rather than plain alpine: the
# status page labels its time axis in Europe/London, and only the extra flavour carries tzdata. It
# costs about 38 MB in shared base layers, once.
CONTAINER_BASE_IMAGE ?= mcr.microsoft.com/dotnet/aspnet:11.0-alpine-extra@sha256:e6abc337f78cea3273eb7d91fc7b2cf1360b21e928df6e5cb8a338fb1b5c6ac8
# RID of the server. The host is a Hetzner CAX21 (Ampere, arm64), so arm64 is the default; an x86
# host builds with CONTAINER_RID=linux-musl-x64. The RID is also the image's platform.
CONTAINER_RID ?= linux-musl-arm64
# The SDK keeps the layers it has packed in a content-addressed cache; `image-check` empties it so
# the second build has to pack and compress the layers again rather than reuse the first one's work.
CONTAINER_CACHE := $(HOME)/.cache/dotnet/Containers/Content
# The commit's own timestamp: fixed for a commit, meaningful in the image, and the reason two builds
# of one commit produce one image. Override to reproduce a build whose commit history has moved.
SOURCE_DATE_EPOCH ?= $(shell git log -1 --format=%ct 2>/dev/null)

# Where `make deploy` sends the image and quadlets, and where the remote-* targets run.
HOST ?=

# A deploy is a dozen ssh commands to one host. A control socket makes them one connection: the host
# key check and authentication happen once, and every later command is a channel on the connection
# that is already open. ControlPersist closes the master 60s after the last of them — long enough to
# cover the next recipe line, short enough that nothing lingers once the target is done. The socket
# is named by %C, a hash of user, host and port: a unix socket path must fit in 108 bytes, and the
# hostnames this target is given need not.
SSH_CONTROL_DIR ?= $(HOME)/.ssh/control
SSH ?= ssh -o ControlMaster=auto -o ControlPath=$(SSH_CONTROL_DIR)/%C -o ControlPersist=60s

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
PROJECT_FILES := $(shell find src test tools -name '*.csproj' 2>/dev/null)

# Everything that can change what NuGet resolves.
RESTORE_INPUTS := global.json $(SOLUTION) \
	Directory.Build.props Directory.Build.targets Directory.Packages.props \
	$(PROJECT_FILES) $(wildcard NuGet.config nuget.config)

# Everything that can change what the compiler produces. Directory mtimes catch added and deleted
# files, and the output directories are listed so deleting part of artifacts/ forces a rebuild.
BUILD_INPUTS := $(shell find src test tools -type f 2>/dev/null) \
	$(shell find src test tools -mindepth 1 -type d 2>/dev/null) \
	global.json .editorconfig \
	Directory.Build.props Directory.Build.targets Directory.Packages.props $(SOLUTION) \
	$(wildcard $(ARTIFACTS)/bin $(ARTIFACTS)/obj)

.PHONY: help build restore check ci drift-check audit check-pages decisions test test-core test-runtime test-web test-integration test-browser test-browser-live browsers vscode-extension migration user-add user-passwd user-list user-remove remote-user-add remote-user-passwd remote-user-list remote-user-remove seeds-list seeds-pull remote-seeds-list remote-seeds-pull run publish image image-check deploy deploy-guard pull-install format format-check license-check mutation-core mutation-runtime mutation-sdk mutation-web worktree worktrees worktree-remove changelist clean
.DELETE_ON_ERROR:

# ssh will not create the directory its control socket lives in.
$(SSH_CONTROL_DIR):
	@install -d -m 700 $@

# ---- Targets -------------------------------------------------------------------------------------

help:
	@printf '%s\n' \
		'build              Build the solution when anything changed (default target).' \
		'restore            Force a NuGet restore (build restores only when inputs change).' \
		'check              The gate: formatting, licences, package audit, the record, the seeded pages, every suite, Release publish.' \
		'ci                 Clean tree, the gate, and two image builds that must agree.' \
		'audit              Fail if any NuGet package is vulnerable or deprecated.' \
		'check-pages        Compile the seeded pages and print compiler diagnostics.' \
		'decisions          Check the decision record: names, areas, duplicates, citations.' \
		'test               Run every suite: core, runtime, web and integration (no browser required).' \
		'test-core          Run the NUnit core tests only.' \
		'test-runtime       Run the NUnit runtime tests only.' \
		'test-web           Run the NUnit web shell and storage tests only.' \
		'test-integration   Run the composed pipeline tests against a throwaway database.' \
		'test-browser       Drive the real host in Firefox with Playwright (needs a browser download).' \
		'test-browser-live  The browser tests that call the configured model provider, which cost tokens.' \
		'migration          Add a storage migration with NAME=... (restores local dotnet-ef).' \
		'user-add           Create a local account with ACCOUNT=... (prompts for a password).' \
		'user-passwd        Change an account password with ACCOUNT=...' \
		'user-list          List local accounts.' \
		'user-remove        Remove an account with ACCOUNT=...' \
		'remote-user-add    Same as user-add, in the container on HOST=deploy@server.' \
		'remote-user-passwd Same as user-passwd, in the container on HOST=...' \
		'remote-user-list   Same as user-list, in the container on HOST=...' \
		'remote-user-remove Same as user-remove, in the container on HOST=...' \
		'run                Run the web app on the site origin, http://127.0.0.1:8080.' \
		'publish            Publish the app to artifacts/publish.' \
		'image              Build the container image with the SDK; the base is pinned in the Makefile.' \
		'image-check        Build it twice and fail if the two builds are not the same image.' \
		'deploy             Gate with the browser suite, build, push to $(REGISTRY) and restart on HOST=deploy@server.' \
		'pull-install       Install the backup pull script and its timer on HOST=osric@backuphost.' \
		'format             Apply formatting and style fixes.' \
		'format-check       Fail if the tree is not formatted.' \
		'license-check      Fail if the tree is not REUSE compliant (needs the reuse tool).' \
		'mutation-core      Mutation-test Core with Stryker: does the suite notice a change in the code?' \
		'mutation-runtime   Mutation-test the page compiler.' \
		'mutation-sdk       Mutation-test the page contract, which the Web suite drives.' \
		'mutation-web       Mutation-test the shell: the long one, hours rather than minutes.' \
		'browsers           Install the Firefox build the browser suite drives.' \
		'vscode-extension   Link tools/vscode-asp into VS Code, then reload the window.' \
		'worktree           Create the branch and worktree for BRANCH=... (BASE=trunk).' \
		'worktrees          List the worktrees.' \
		'worktree-remove    Remove the worktree for BRANCH=... once its work has merged.' \
		'changelist         Write the history of the deployed commit to artifacts/changes.txt.' \
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
# here: the budget tests burn a wall clock on purpose, so only a whole-run ceiling is safe.
# The ceiling is VSTest's session timeout, in milliseconds because that is how runsettings counts.
# It is not `--blame-hang`: that reports a warning on every run that ends normally — that it wrote no
# sequence file, which is the good outcome — and a warning nobody can act on is one everybody learns
# to skip.
#
# The three unit suites are supposed to be fast — core under a second, runtime a couple of seconds,
# web half a minute — but "fast" is not a fixed number and web grew past the minute: with the machine
# to itself it takes thirty-five seconds, and beside a `dotnet format` and its own eight parallel
# fixtures it takes fifty-five to seventy-five. A minute therefore aborted healthy runs, and did it
# by reporting the tests it had run as "Passed!" and then "Test Run Aborted" — a suite that failed for
# being slow looked, in the log, like a suite that passed. Three minutes is two and a half times the
# worst measured run and still fails a wedged suite long before anyone would give up on it. The
# integration and browser suites drive a real host and a browser, so their ceiling stays generous;
# both can be overridden, as can the fast one on a machine slower than this.
#
# The browser suite has a ceiling of its own because it is the slowest thing here and the least
# predictable: fifty tests that each open a page in Firefox and wait on a streamed answer take a
# little over two minutes with the machine to themselves, and a deploy runs them straight after a
# full gate and a publish, on a machine that was busy a moment ago. Five minutes was only two and a
# half times that, so a healthy run under a deploy's load reached the ceiling and was aborted for
# being slow rather than for being wedged. Ten is five times the measured run, which still fails a
# wedged suite long before anyone would give up on it.
TEST_FAST_TIMEOUT ?= 180000
TEST_HANG_TIMEOUT ?= 300000
TEST_BROWSER_TIMEOUT ?= 600000
# Test results belong with everything else the build produces. Left to itself the runner creates a
# TestResults/ folder beside each project, which puts output in the source tree. Everything after the
# `--` is a runsettings property, which is the only way in from the command line.
TEST_RESULTS := $(ARTIFACTS)/test-results
TEST_ARGS := --results-directory $(TEST_RESULTS) -- RunConfiguration.TestSessionTimeout=$(TEST_HANG_TIMEOUT)
TEST_FAST_ARGS := --results-directory $(TEST_RESULTS) -- RunConfiguration.TestSessionTimeout=$(TEST_FAST_TIMEOUT)
TEST_BROWSER_ARGS := --results-directory $(TEST_RESULTS) -- RunConfiguration.TestSessionTimeout=$(TEST_BROWSER_TIMEOUT)

# The gate. Cheap checks first, so a formatting mistake fails in seconds rather than after the suites.
# Every part is a target of its own, so this is as runnable on a laptop as it is in CI: there is no
# second definition of "green" to drift from this one. `publish` is here because Release is what
# actually ships — a Release-only break should not wait for a deploy to be discovered.
check: format-check license-check audit decisions check-pages test publish

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
	$(DOTNET) test $(CORE_TESTS) -c $(CONFIGURATION) --no-build $(TEST_FAST_ARGS)

test-runtime: build
	$(DOTNET) test $(RUNTIME_TESTS) -c $(CONFIGURATION) --no-build $(TEST_FAST_ARGS)

test-web: build
	$(DOTNET) test $(WEB_TESTS) -c $(CONFIGURATION) --no-build $(TEST_FAST_ARGS)

# The host is real: routing, middleware order, exception handling and startup migrations. Slower
# than the other suites, which is why it has its own target rather than living in the unit tests.
test-integration: build
	$(DOTNET) test $(INTEGRATION_TESTS) -c $(CONFIGURATION) --no-build $(TEST_ARGS)

# A browser engine is an external dependency with its own download, so this suite is out of `check`:
# a suite that takes a couple of minutes and can flake is a suite people stop running all day. It is
# `make deploy` that runs it (`code-browser-suite-is-opt-in`), before anything is pushed. `make browsers` installs Firefox
# once; the tests launch the built app themselves on a loopback port.
test-browser: build
	$(DOTNET) test $(BROWSER_TESTS) -c $(CONFIGURATION) --no-build $(TEST_BROWSER_ARGS)

# The live tests are [Explicit]: they need deploy/deepseek.token (or ASSISTANT_LIVE_TOKEN) and spend
# real tokens against the configured provider.
test-browser-live: build
	$(DOTNET) test $(BROWSER_TESTS) -c $(CONFIGURATION) --no-build --filter "FullyQualifiedName~AssistantLive" $(TEST_BROWSER_ARGS)

# Playwright ships its own Node, and this downloads the single browser the suite drives. The path is
# the copy the package puts beside the test assembly, so it is the version the tests will use.
BROWSER_TESTS_BIN := $(ARTIFACTS)/bin/uk.osric.asp.Web.BrowserTests/$(shell echo $(CONFIGURATION) | tr 'A-Z' 'a-z')

browsers: build
	pwsh -NoProfile -File $(BROWSER_TESTS_BIN)/playwright.ps1 install firefox

# ---- Editor support -------------------------------------------------------------------------------

# `code --install-extension` takes an extension id or a .vsix, not a folder, and this extension has no
# build step to make a .vsix from. A symlink in the user extensions folder is what
# `--extensionDevelopmentPath` would load, without opening a second window, and it keeps the grammar
# in the repository the one copy: an edit and a reload is the whole update loop.
#
# The icon is the site's rocket rather than a second copy of it, so it is rendered from the seed when
# the tool that draws it is here; a checkout without rsvg-convert keeps the committed PNG.
VSCODE_EXTENSIONS := $(HOME)/.vscode/extensions
VSCODE_EXTENSION := $(VSCODE_EXTENSIONS)/uk-osric-asp.asp-template-language-0.1.0
ROCKET_SVG := src/uk.osric.asp.Web/wwwroot/rocket.svg
EXTENSION_ICON := tools/vscode-asp/icon.png

vscode-extension:
	@if command -v rsvg-convert >/dev/null; then \
		rsvg-convert -w 128 -h 128 $(ROCKET_SVG) -o $(EXTENSION_ICON); \
	fi
	@mkdir -p $(VSCODE_EXTENSIONS)
	@rm -f $(VSCODE_EXTENSIONS)/uk-osric-asp.asp-template-language-*
	@ln -sfn $(abspath tools/vscode-asp) $(VSCODE_EXTENSION)
	@echo "Linked $(VSCODE_EXTENSION); run 'Developer: Reload Window'."

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

remote-user-add: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-add HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-add HOST=deploy@server ACCOUNT=name"; exit 1; }
	$(SSH) -t $(HOST) podman exec -it -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users add '$(ACCOUNT)'

remote-user-passwd: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-passwd HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-passwd HOST=deploy@server ACCOUNT=name"; exit 1; }
	$(SSH) -t $(HOST) podman exec -it -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users passwd '$(ACCOUNT)'

remote-user-list: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-list HOST=deploy@server"; exit 1; }
	$(SSH) $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users list

remote-user-remove: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-user-remove HOST=deploy@server ACCOUNT=name"; exit 1; }
	@test -n "$(strip $(ACCOUNT))" || { echo "usage: make remote-user-remove HOST=deploy@server ACCOUNT=name"; exit 1; }
	$(SSH) $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) users remove '$(ACCOUNT)'

# ---- Seeds ----------------------------------------------------------------------------------------
#
# The pack is edited in production and the repository falls behind it. `seeds` prints what the store
# holds for each seed and pulls the store's bytes back into the working tree; git is the merge tool and
# `git diff` is the review. Pull refuses a dirty seed folder unless FORCE=1, because extracting over an
# uncommitted edit is the one thing it can destroy.

seeds-list: build
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) \
		$(DOTNET) run --project $(WEB_PROJECT) --no-build -- seeds list

seeds-pull: build
	@test -z "$$(git status --porcelain -- $(SEED_DIRECTORY))" || test -n "$(strip $(FORCE))" || { echo "Pages/Seeds has uncommitted changes; commit them, or pass FORCE=1 to overwrite"; exit 1; }
	@archive=$$(mktemp); trap 'rm -f "$$archive"' EXIT; \
		ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) $(DOTNET) run --project $(WEB_PROJECT) --no-build -- seeds pull $(if $(strip $(ALL)),--all) $(if $(strip $(MISSING)),--missing) > "$$archive" && \
		tar -x -C $(SEED_DIRECTORY) -f "$$archive"

remote-seeds-list: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-seeds-list HOST=deploy@server"; exit 1; }
	$(SSH) $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) seeds list

remote-seeds-pull: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make remote-seeds-pull HOST=deploy@server"; exit 1; }
	@test -z "$$(git status --porcelain -- $(SEED_DIRECTORY))" || test -n "$(strip $(FORCE))" || { echo "Pages/Seeds has uncommitted changes; commit them, or pass FORCE=1 to overwrite"; exit 1; }
	@archive=$$(mktemp); trap 'rm -f "$$archive"' EXIT; \
		$(SSH) $(HOST) podman exec -w /app $(CONTAINER_NAME) dotnet $(APP_ASSEMBLY) seeds pull $(if $(strip $(ALL)),--all) $(if $(strip $(MISSING)),--missing) > "$$archive" && \
		tar -x -C $(SEED_DIRECTORY) -f "$$archive"

# The port is not a preference: Site:Origin in appsettings.json names it, and the assistant's view
# fetches that origin, so a developer host on any other port is a host whose own view cannot connect.
# Override ASPNETCORE_URLS and Site__Origin together, or not at all.
run:
	ASPNETCORE_ENVIRONMENT=$(ASPNETCORE_ENVIRONMENT) ASPNETCORE_URLS=$(or $(ASPNETCORE_URLS),http://127.0.0.1:8080) \
		$(DOTNET) run --project $(WEB_PROJECT)

publish: changelist
	$(DOTNET) publish $(WEB_PROJECT) -c Release -p:ChangelistFile=$(abspath $(CHANGELIST))

# ---- Decision checks ------------------------------------------------------------------------------

# docs/decisions.md is the record, so its shape is a thing to prove rather than to remember: every
# entry a name in one of the areas the file lists, no name used twice, a "Recorded in" line under each,
# a retired name not taken again, and no document citing a decision that is not there.
CHECK_DECISIONS_PROJECT := tools/check-decisions/check-decisions.csproj

decisions: $(BUILD_STAMP)
	$(DOTNET) run --project $(CHECK_DECISIONS_PROJECT) -c $(CONFIGURATION) --no-build -- .

# ---- Page checks ----------------------------------------------------------------------------------

# A seed is compiled when it is first requested, so a broken one ships silently and the first visitor
# gets the error page. This runs the product's own compiler over them at build time and prints the
# author-facing diagnostics in the file:line:column shape the editor's `check pages` task reads.
CHECK_PAGES_PROJECT := tools/check-pages/check-pages.csproj
SEED_DIRECTORY := src/uk.osric.asp.Web/Pages/Seeds

check-pages: $(BUILD_STAMP)
	$(DOTNET) run --project $(CHECK_PAGES_PROJECT) -c $(CONFIGURATION) --no-build -- $(SEED_DIRECTORY)

# The publish output the image packs: UseArtifactsOutput puts a RID-specific publish beside the
# plain one, so `make publish` and the image do not overwrite each other.
IMAGE_PUBLISH_DIR := $(ARTIFACTS)/publish/uk.osric.asp.Web/release_$(CONTAINER_RID)

# One line, so `image` and `image-check` can call it without repeating the flags — and so no recipe
# line contains `$(MAKE)`, which make would run even under `-n`.
#
# SOURCE_DATE_EPOCH is what makes the SDK's image reproducible: it stamps the config's created time,
# the history entries and the layer mtimes, so two builds of one commit are one image. The value is
# the commit's own timestamp, so the image says when its source was written, and says it the same way
# every time. $(1) is the tag; $(2) carries extra flags.
image_command = SOURCE_DATE_EPOCH=$(SOURCE_DATE_EPOCH) $(DOTNET) publish $(WEB_PROJECT) -c Release -r $(CONTAINER_RID) -t:PublishContainer -p:ContainerBaseImage=$(CONTAINER_BASE_IMAGE) -p:ContainerImageTags=$(1) -p:GIT_COMMIT=$(GIT_COMMIT) -p:ChangelistFile=$(abspath $(CHANGELIST)) $(2)

# The SDK builds the OCI image and hands it to podman; there is no Containerfile, and the SDK is not
# in the image.
image: changelist
	@rm -rf $(IMAGE_PUBLISH_DIR)
	$(call image_command,$(IMAGE_TAG))

# Reproducibility is a property that regresses silently, so it is a target: two fresh publishes and
# two uncached packs of one commit have to produce one image id. The publish directory is deleted
# between the builds, because an incremental publish leaves the files it already has and their
# mtimes alone — and the layer cache is emptied so the second pack is not the first one's work.
image-check: changelist
	@rm -rf $(IMAGE_PUBLISH_DIR) $(CONTAINER_CACHE)
	@$(call image_command,reproducible-1)
	@rm -rf $(IMAGE_PUBLISH_DIR) $(CONTAINER_CACHE)
	@$(call image_command,reproducible-2)
	@a=$$(podman image inspect --format '{{.Id}}' $(IMAGE_REPOSITORY):reproducible-1); \
	b=$$(podman image inspect --format '{{.Id}}' $(IMAGE_REPOSITORY):reproducible-2); \
	podman rmi $(IMAGE_REPOSITORY):reproducible-1 $(IMAGE_REPOSITORY):reproducible-2 >/dev/null; \
	if [ "$$a" != "$$b" ]; then \
		echo "the image is not reproducible:"; \
		echo "  build 1: $$a"; \
		echo "  build 2: $$b"; \
		echo "look for a timestamp or a base tag that moves between builds"; \
		exit 1; \
	fi; \
	echo "reproducible: $$a"

# Pushes the image, installs the quadlets (the source of truth), and restarts. The app and Prometheus
# run rootless as HOST's user on a shared network; Caddy is the only rootful unit and is installed
# with sudo. The quadlet's Image= line is rewritten on the way, so REGISTRY and the commit tag reach
# the host. Every quadlet is written before the single daemon-reload that reads them, and both
# services are restarted in one command afterwards: a reload or a restart each costs a round trip,
# and neither has anything to do until the last file is in place. Restarting rather than starting is
# what makes a Caddyfile edit take effect on a redeploy, and a restart is a reason to run this target
# on its own. There is no enable step: quadlet units are transient, so systemctl refuses to enable
# them, and the generator already applies each quadlet's [Install] section on daemon-reload.
#
# The whole gate runs before the image is built, and therefore long before anything is pushed: a tag
# that has been pulled by the server cannot be withdrawn, and a broken suite is cheaper to meet here
# than on the running site. The browser suite is part of this gate and not of `check` (`code-browser-suite-is-opt-in`): two
# minutes is cheap for a deploy and expensive for the gate a change is run against all day, and a
# deploy is the moment it matters that the site serves a page in a browser at all. Do not run this
# target with -j; the order is the point.
deploy: deploy-guard check test-browser image | $(SSH_CONTROL_DIR)
	@$(SSH) $(HOST) 'dpkg -s aardvark-dns >/dev/null 2>&1 || { \
		echo "the host is missing aardvark-dns: container names will not resolve"; \
		echo "run: sudo apt-get install -y aardvark-dns"; \
		exit 1; }'
	@# The backup host pulls with rsync, which runs a copy on this host as the remote side.
	@$(SSH) $(HOST) 'command -v rsync >/dev/null 2>&1 || { \
		echo "the host is missing rsync: the backup host cannot copy its snapshots"; \
		echo "run: sudo apt-get install -y rsync"; \
		exit 1; }'
	@# cloud-init installs the ban once, when the host is created; a host that predates it needs the
	@# packages before the jail below has anything to run in. nft is the ban action's own command and
	@# lives in /usr/sbin, which is not on the PATH an ssh command runs with, so it is named in full
	@# rather than looked up: a jail without it installs, starts and silently fails to ban. Each
	@# missing piece is named, because "missing fail2ban or nftables" sends an operator to apt for
	@# packages that are already there.
	@$(SSH) $(HOST) 'missing=; \
		dpkg -s fail2ban >/dev/null 2>&1 || missing="$$missing fail2ban"; \
		dpkg -s nftables >/dev/null 2>&1 || missing="$$missing nftables"; \
		test -x /usr/sbin/nft || missing="$$missing /usr/sbin/nft"; \
		if [ -n "$$missing" ]; then \
			echo "the host is missing$${missing}: nothing will ban an address that keeps failing to sign in"; \
			echo "run: sudo apt-get install -y fail2ban nftables jq"; \
			exit 1; \
		fi'
	podman tag $(LOCAL_IMAGE) $(IMAGE_REF)
	podman tag $(LOCAL_IMAGE) $(IMAGE_SHA_REF)
	podman push --authfile $(AUTHFILE) $(IMAGE_REF)
	podman push --authfile $(AUTHFILE) $(IMAGE_SHA_REF)
	$(SSH) $(HOST) podman pull --authfile $(REMOTE_AUTHFILE) $(IMAGE_SHA_REF)
	@# The assistant's EnvironmentFile has no optional form in podman: quadlet turns it into
	@# `podman run --env-file`, which has no `-` form and fails the container when the path is
	@# absent. Seed it once so the path exists; it must hold Assistant__ApiKey, because the host
	@# refuses to start without one. `test -e` leaves the secret alone.
	$(SSH) $(HOST) 'install -d -m 0700 .config/uk.osric.asp; test -e .config/uk.osric.asp/assistant.env || install -m 0600 /dev/null .config/uk.osric.asp/assistant.env'
	@# The host's settings override is the same shape of problem: a single-file bind of a missing
	@# path becomes a directory and the app cannot read it. Seed it once with an empty object, which
	@# merges over the shipped section and changes nothing, and leave a host's own file alone.
	$(SSH) $(HOST) 'test -e .config/uk.osric.asp/appsettings.Production.json || echo "{}" > .config/uk.osric.asp/appsettings.Production.json'
	$(SSH) $(HOST) install -d .config/prometheus
	$(SSH) $(HOST) 'cat > .config/containers/systemd/uk.osric.asp.network' < deploy/uk.osric.asp.network
	sed 's|^Image=.*|Image=$(IMAGE_SHA_REF)|' deploy/uk.osric.asp.container | $(SSH) $(HOST) 'cat > .config/containers/systemd/uk.osric.asp.container'
	$(SSH) $(HOST) 'cat > .config/prometheus/prometheus.yml' < deploy/prometheus.yml
	$(SSH) $(HOST) 'cat > .config/containers/systemd/prometheus.container' < deploy/prometheus.container
	@# The snapshot script and its clock live on the host like the quadlets do. The pull is the backup
	@# host's job and runs from its own copy of deploy/pull-backups.sh, so nothing here reaches it.
	$(SSH) $(HOST) install -d bin
	$(SSH) $(HOST) 'cat > bin/backup-snapshot.sh' < deploy/backup-snapshot.sh
	$(SSH) $(HOST) chmod 0755 bin/backup-snapshot.sh
	$(SSH) $(HOST) install -d .config/systemd/user
	$(SSH) $(HOST) 'cat > .config/systemd/user/uk.osric.asp-backup.service' < deploy/uk.osric.asp-backup.service
	$(SSH) $(HOST) 'cat > .config/systemd/user/uk.osric.asp-backup.timer' < deploy/uk.osric.asp-backup.timer
	$(SSH) $(HOST) systemctl --user daemon-reload
	$(SSH) $(HOST) systemctl --user restart uk.osric.asp.service prometheus.service
	$(SSH) $(HOST) systemctl --user enable --now uk.osric.asp-backup.timer
	@$(SSH) $(HOST) 'i=0; until podman exec prometheus wget -qO- -T 3 http://app:8080/health >/dev/null 2>&1; do \
		i=$$((i+1)); \
		if [ $$i -ge 10 ]; then \
			echo "Prometheus cannot reach the app at app:8080: check podman network inspect uk.osric.asp"; \
			exit 1; \
		fi; \
		sleep 2; \
	done'
	$(SSH) $(HOST) sudo install -d -o root -g root -m 0755 /etc/caddy
	$(SSH) $(HOST) 'sudo tee /etc/caddy/Caddyfile > /dev/null' < deploy/Caddyfile
	$(SSH) $(HOST) 'sudo tee /etc/containers/systemd/caddy.container' < deploy/caddy.container > /dev/null
	$(SSH) $(HOST) sudo systemctl daemon-reload
	$(SSH) $(HOST) sudo systemctl restart caddy.service
	@# The ban's one jail and its filter live here like the quadlets and the Caddyfile do. Installing
	@# them beside the app's restart keeps the words the app logs and the words fail2ban matches in
	@# one commit; the reload re-reads both without losing the rest of the host's state.
	$(SSH) $(HOST) 'sudo tee /etc/fail2ban/filter.d/uk.osric.asp.conf > /dev/null' < deploy/fail2ban/filter.d/uk.osric.asp.conf
	$(SSH) $(HOST) 'sudo tee /etc/fail2ban/jail.local > /dev/null' < deploy/fail2ban/jail.local
	$(SSH) $(HOST) sudo systemctl enable fail2ban.service
	$(SSH) $(HOST) sudo systemctl reload-or-restart fail2ban.service
	@echo "waiting for /health"
	@$(SSH) $(HOST) 'i=0; until curl -fsS -o /dev/null http://127.0.0.1:8080/health; do \
		i=$$((i+1)); \
		if [ $$i -ge 30 ]; then \
			echo "the site is not healthy 60s after the restart: journalctl --user -u uk.osric.asp.service"; \
			exit 1; \
		fi; \
		sleep 2; \
	done'
	@echo "deployed $(GIT_SHA)"
	@# The image is tagged with the commit, so a commit that reached the registry but never reached
	@# the remote leaves the running site with no source anyone else can fetch, review, or rebuild a
	@# rollback from. A note rather than a gate: the push has already happened and cannot be
	@# withdrawn, so failing here would only make a successful deploy look like a failed one.
	@if up=$$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null); then \
		ahead=$$(git rev-list --count "$$up..HEAD"); \
		if [ "$$ahead" -gt 0 ]; then \
			echo "but $$up is missing $$ahead commit(s): git push"; \
			git --no-pager log --oneline "$$up..HEAD"; \
		fi; \
	else \
		echo "but this checkout has no upstream to compare $(GIT_SHA) with: is the commit on the remote?"; \
	fi

# The other half of the backup, installed where it belongs: the host that keeps the copies, not the
# site. It is HOST=osric@<backuphost> rather than the deploy user because that machine owns the key
# the pull authenticates with and the directory it writes, so nothing here needs sudo there.
# `deploy/` stays the source of truth; this copies what it holds.
pull-install: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make pull-install HOST=osric@backuphost"; exit 1; }
	@$(SSH) $(HOST) 'command -v rsync >/dev/null 2>&1 && command -v sqlite3 >/dev/null 2>&1 || { \
		echo "the backup host needs rsync and sqlite3 to pull and verify"; \
		echo "run: sudo apt-get install -y rsync sqlite3"; \
		exit 1; }'
	@$(SSH) $(HOST) 'command -v systemctl >/dev/null 2>&1 || { \
		echo "the backup host needs systemd to schedule the pull"; exit 1; }'
	$(SSH) $(HOST) 'command -v loginctl >/dev/null 2>&1 && loginctl show-user $$USER --property=Linger | grep -qx "Linger=yes" || echo "warning: user lingering is off, so the timer stops at reboot until someone logs in; run: sudo loginctl enable-linger $$USER"'
	$(SSH) $(HOST) install -d bin .config/systemd/user
	$(SSH) $(HOST) 'cat > bin/pull-backups.sh' < deploy/pull-backups.sh
	$(SSH) $(HOST) chmod 0755 bin/pull-backups.sh
	$(SSH) $(HOST) 'cat > .config/systemd/user/uk.osric.asp-pull.service' < deploy/uk.osric.asp-pull.service
	$(SSH) $(HOST) 'cat > .config/systemd/user/uk.osric.asp-pull.timer' < deploy/uk.osric.asp-pull.timer
	$(SSH) $(HOST) systemctl --user daemon-reload
	$(SSH) $(HOST) systemctl --user enable --now uk.osric.asp-pull.timer
	@$(SSH) $(HOST) systemctl --user list-timers uk.osric.asp-pull.timer --no-pager

# Everything the deploy destination insists on, checked before the gate rather than after it. An
# image pushed from an uncommitted tree cannot be rebuilt from the commit its tag names, which is
# also the only thing a rollback can rely on; ALLOW_DIRTY=1 is the deliberate exception.
deploy-guard: | $(SSH_CONTROL_DIR)
	@test -n "$(strip $(HOST))" || { echo "usage: make deploy HOST=deploy@your.server"; exit 1; }
	@test -n "$(strip $(GIT_SHA))" || { echo "not a git checkout: there is no commit to tag the image with"; exit 1; }
	@if [ -z "$(strip $(ALLOW_DIRTY))" ] && [ -n "$$(git status --porcelain)" ]; then \
		echo "uncommitted changes: commit them first, or pass ALLOW_DIRTY=1"; \
		git status --short; exit 1; fi
	@if grep -qE '^[[:space:]]*(example\.com|admin@example\.com)' deploy/Caddyfile; then \
		echo "deploy/Caddyfile still contains the example.com placeholder"; exit 1; fi
	@$(SSH) -o ConnectTimeout=10 $(HOST) true || { echo "cannot reach $(HOST)"; exit 1; }

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

format:
	$(DOTNET) format $(SOLUTION)

# What the build cannot see is layout, so layout is what this asks for: every whitespace rule, and
# the `IMPORTS` fixer that orders using directives, which raises no diagnostic the compiler owns.
# Everything else `dotnet format` would check is already an error in the build — `AnalysisMode=All`
# turns the CA rules on and `EnforceCodeStyleInBuild` turns the IDE ones on, and a warning is an
# error (Directory.Build.props) — so asking for it here would run the whole analysis a second time
# for no answer the build has not already given.
#
# It is not a small second time. Measured on this tree, the analyzer phase is 419 of the 503
# CPU-seconds the whole of `dotnet format` spends — seven minutes of cores for a minute and a half of
# wall clock, the busiest thing the gate does — and the style phase is another 70 for the one
# diagnostic the build does not raise. Narrowed to layout and using order the check spends about 25
# CPU-seconds instead, and the cores go to the suites.
format-check:
	$(DOTNET) format whitespace $(SOLUTION) --verify-no-changes
	$(DOTNET) format style $(SOLUTION) --verify-no-changes --diagnostics IMPORTS

# Needs the `reuse` tool (apt install reuse, or pipx install reuse).
license-check:
	reuse lint

# Mutation testing rewrites a project's source and reruns the suite once per mutant, so it is minutes
# to hours rather than seconds and stays out of `check`. Stryker mutates one project per run, so each
# project has a target of its own and a configuration beside the suite that drives it. Each report
# lands in its own folder, so one run cannot overwrite another's.
#
# Stryker compiles the mutated project itself, which is why this needs only a restore: the build stamp
# would compile the same projects for nothing. It runs from the test project, its own recommendation:
# started at the solution file, Stryker takes the whole solution as its scope and runs every suite for
# every mutant.
#
# Its builds land in the same artifacts/ tree as a normal one, so the stamp is deleted before a run
# starts: interrupted halfway it would leave mutated assemblies behind, and a stamp that still said
# "built" would let the next `make test` run against them. Only the restore stamp is kept.

MUTATION_DIR := $(ARTIFACTS)/stryker
FORGET_BUILD = @rm -f $(BUILD_STAMP)

mutation-core: $(RESTORE_STAMP)
	$(FORGET_BUILD)
	$(DOTNET) tool restore
	cd $(dir $(CORE_TESTS)) && $(DOTNET) tool run dotnet-stryker --output $(abspath $(MUTATION_DIR))/core

mutation-runtime: $(RESTORE_STAMP)
	$(FORGET_BUILD)
	$(DOTNET) tool restore
	cd $(dir $(RUNTIME_TESTS)) && $(DOTNET) tool run dotnet-stryker --output $(abspath $(MUTATION_DIR))/runtime

# Sdk has no suite of its own, so its configuration sits beside the Web suite, which is the one that
# drives it: the document builder is asserted there.
mutation-sdk: $(RESTORE_STAMP)
	$(FORGET_BUILD)
	$(DOTNET) tool restore
	cd $(dir $(WEB_TESTS)) && $(DOTNET) tool run dotnet-stryker -f stryker-sdk-config.json --output $(abspath $(MUTATION_DIR))/sdk

# The shell is the long one: ten times the code of the rest together, and the slowest suite.
mutation-web: $(RESTORE_STAMP)
	$(FORGET_BUILD)
	$(DOTNET) tool restore
	cd $(dir $(WEB_TESTS)) && $(DOTNET) tool run dotnet-stryker --output $(abspath $(MUTATION_DIR))/web

# ---- Worktrees -----------------------------------------------------------------------------------
#
# Every job or plan gets a branch and a worktree of its own. The branch is what merges; the worktree
# is what one agent at a time works in. The branch is left alone by `worktree-remove`, because `git
# branch -d` refusing an unmerged branch is the check that the work got back.

worktree:
	@test -n "$(strip $(BRANCH))" || { printf '%s\n' \
		"usage: make worktree BRANCH=<branch> [BASE=$(BASE)]" >&2; exit 2; }
	@install -d "$(WORKTREES)"
	@if git show-ref --verify --quiet "refs/heads/$(BRANCH)"; then \
		git worktree add "$(WORKTREES)/$(BRANCH)" "$(BRANCH)"; \
	else \
		git worktree add -b "$(BRANCH)" "$(WORKTREES)/$(BRANCH)" "$(BASE)"; \
	fi
	@printf 'Worktree: %s\n' "$(WORKTREES)/$(BRANCH)"

worktrees:
	@git worktree list

worktree-remove:
	@test -n "$(strip $(BRANCH))" || { printf '%s\n' \
		"usage: make worktree-remove BRANCH=<branch>" >&2; exit 2; }
	git worktree remove "$(WORKTREES)/$(BRANCH)"
	@printf 'Removed %s; the branch stays until it is merged and deleted.\n' "$(WORKTREES)/$(BRANCH)"

# Full hashes, because /admin/changes links each commit to its page on GitHub and an abbreviation is
# only unique within one repository. --no-decorate keeps a local branch name out of the file, so the
# same commit writes the same file wherever it is built.
changelist:
	@install -d $(ARTIFACTS)
	git --no-pager log --no-decorate --format='%H %s' > $(CHANGELIST)

clean:
	rm -rf $(ARTIFACTS)
