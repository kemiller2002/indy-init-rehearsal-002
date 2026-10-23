# Causa Rehearsal 002 — contributor entry points. `make run` restores, builds and serves the app.
# Every target fails with an actionable message (scripts/doctor.sh) when the toolchain is wrong.
PORT ?= 8080
SHELL := /bin/bash

.PHONY: help doctor restore build run test test-engine test-browser test-live validate package deploy clean

help:
	@echo "make run       restore, build the F# engine + WASM site, serve on http://127.0.0.1:$(PORT)/"
	@echo "make build     restore and build (dist/site)"
	@echo "make test      engine, conformance and browser tests"
	@echo "make validate  stack lock, vendored sources, zero demo data, kernel type-check"
	@echo "make package   dist/causa-site.tar.gz from dist/site"
	@echo "make deploy    how deployment happens (GitHub Actions → GitHub Pages)"

doctor:
	@scripts/doctor.sh

restore: doctor
	npm ci --no-audit --no-fund
	dotnet restore App.slnx

build: restore
	dotnet build App.slnx -c Release
	node scripts/build-site.mjs

run: build
	node scripts/serve.mjs dist/site $(PORT)

test-engine:
	tools/data/fetch-causa-data.sh
	dotnet test App.slnx -c Release

test-browser:
	npx playwright test

test: build test-engine test-browser

# Live acceptance against a real GitHub repository. Requires CAUSA_LIVE_TOKEN (a short-lived, repository-scoped
# token), CAUSA_LIVE_OWNER, CAUSA_LIVE_REPO and CAUSA_LIVE_BRANCH. Never commit or log the token.
test-live:
	npx playwright test -c playwright.live.config.js

validate: doctor
	python3 -I tools/stack/check_lock.py
	python3 -I tools/vendor/vendor.py verify
	tools/data/fetch-causa-data.sh
	node scripts/check-no-demo-data.mjs
	npm run check

package: build
	tar -czf dist/causa-site.tar.gz -C dist/site .
	@echo "dist/causa-site.tar.gz"

deploy:
	@echo "Deployment runs in GitHub Actions (.github/workflows/deploy-pages.yml) on pushes to main after the"
	@echo "build-and-test gates pass; it publishes dist/site to GitHub Pages and verifies that the served"
	@echo "build-info.json names the deployed commit. To deploy: merge to main."

clean:
	rm -rf dist test-results .cache/typecheck
	dotnet clean App.slnx -c Release >/dev/null
