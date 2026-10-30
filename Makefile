# uk.osric.asp — task runner. Run `make help` for the menu.
#
# Everything a build produces lives under artifacts/ (UseArtifactsOutput), including the stamps that
# make repeat runs cheap. `make clean` deletes the lot.

.DEFAULT_GOAL := build

# ---- Tooling -------------------------------------------------------------------------------------

DOTNET ?= dotnet
CONFIGURATION ?= Debug

# ---- Layout --------------------------------------------------------------------------------------

SOLUTION := uk.osric.asp.slnx
ARTIFACTS := artifacts

WEB_PROJECT := src/uk.osric.asp.Web/uk.osric.asp.Web.csproj
UNIT_TESTS := test/uk.osric.asp.UnitTests/uk.osric.asp.UnitTests.csproj
SPECS := test/uk.osric.asp.Specs/uk.osric.asp.Specs.csproj
E2E_TESTS := test/uk.osric.asp.EndToEnd/uk.osric.asp.EndToEnd.csproj

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
# files, generated Reqnroll code-behind is excluded because the build rewrites it, and the output
# directories are listed so deleting part of artifacts/ forces a rebuild.
BUILD_INPUTS := $(shell find src test -type f -not -name '*.feature.cs' 2>/dev/null) \
	$(shell find src test -mindepth 1 -type d 2>/dev/null) \
	global.json .editorconfig \
	Directory.Build.props Directory.Build.targets Directory.Packages.props $(SOLUTION) \
	$(wildcard $(ARTIFACTS)/bin $(ARTIFACTS)/obj)

.PHONY: help build restore test test-unit test-spec e2e run publish format format-check clean
.DELETE_ON_ERROR:

# ---- Targets -------------------------------------------------------------------------------------

help:
	@printf '%s\n' \
		'build              Build the solution when anything changed (default target).' \
		'restore            Force a NuGet restore (build restores only when inputs change).' \
		'test               Run the unit and specification tests (no browser required).' \
		'test-unit          Run the NUnit unit tests only.' \
		'test-spec          Run the Reqnroll specifications only.' \
		'e2e                Run the Playwright end-to-end tests (installs Chromium if missing).' \
		'run                Run the web app on the default URLs.' \
		'publish            Publish the app to artifacts/publish.' \
		'format             Apply formatting and style fixes.' \
		'format-check       Fail if the tree is not formatted.' \
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

test: test-unit test-spec

test-unit: build
	$(DOTNET) test $(UNIT_TESTS) -c $(CONFIGURATION) --no-build

test-spec: build
	$(DOTNET) test $(SPECS) -c $(CONFIGURATION) --no-build

e2e: build
	$(DOTNET) test $(E2E_TESTS) -c $(CONFIGURATION) --no-build

run:
	$(DOTNET) run --project $(WEB_PROJECT)

publish:
	$(DOTNET) publish $(WEB_PROJECT) -c Release

format:
	$(DOTNET) format $(SOLUTION)

format-check:
	$(DOTNET) format $(SOLUTION) --verify-no-changes

clean:
	rm -rf $(ARTIFACTS)
