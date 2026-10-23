// @ts-check
// Composition root. Loads the Causa F# engine (.NET WebAssembly) and hands it to the Limen kernel. No application
// decision is made here: the engine decides, the kernel binds and runs effects. This file only manages the
// browser-side startup experience (WASM-LOAD-001..008): the static loading shell, an honest status, a bounded
// timeout, a recoverable failure, and focus during the handoff. Paths are relative, so the site works under any
// sub-path (GitHub Pages project sites).

import { BrowserKernel } from "./vendor/limen/kernel/browser-kernel.js";
import { DotnetWasmTransport } from "./vendor/limen/hosts/dotnet-wasm-transport.js";
import "./vendor/folio/components/register.js";

const START_TIMEOUT_MS = 90000;

/** @param {string} id */
const byId = (id) => /** @type {HTMLElement} */ (document.getElementById(id));

const boot = byId("causa-boot");
const app = byId("causa-app");
const status = byId("causa-boot-status");
const failure = byId("causa-boot-failure");
const retry = byId("causa-boot-retry");

/** Kernel diagnostics carry phases and kinds only, never request headers, bodies or event values. */
/** @type {unknown[]} */
const diagnostics = [];
Reflect.set(window, "causaDiagnostics", diagnostics);

/** @param {string} text */
const setStatus = (text) => {
  status.textContent = text;
};

/** @param {string} reason */
const fail = (reason) => {
  boot.dataset.bootState = "failed";
  document.documentElement.dataset.boot = "failed";
  setStatus("Causa could not start.");
  byId("causa-boot-error").textContent = reason;
  failure.hidden = false;
  retry.focus();
};

retry.addEventListener("click", () => {
  // A safe reload: the kernel must never be started twice on one document. No data is kept across it.
  window.location.reload();
});

/** Print the report (browser print / save as PDF); presentation only. */
document.addEventListener("click", (event) => {
  const target = event.target instanceof Element ? event.target.closest("[data-causa-print]") : null;
  if (target !== null) window.print();
});

/** Move focus into a newly opened form panel and restore it afterwards (Limen Core does not manage focus). */
const watchFocus = () => {
  /** @type {Element | null} */
  let returnTo = null;
  new MutationObserver(() => {
    const dialog = app.querySelector(".causa-dialog");
    if (dialog !== null && !dialog.contains(document.activeElement)) {
      if (returnTo === null) returnTo = document.activeElement;
      const first = /** @type {HTMLElement | null} */ (
        dialog.querySelector(".ef-field:not([hidden]) [data-causa-autofocus], .ef-field:not([hidden]) input, .ef-field:not([hidden]) select, fieldset:not([hidden]) input, button[type=submit]")
      );
      first?.focus();
    } else if (dialog === null && returnTo !== null) {
      if (returnTo instanceof HTMLElement && returnTo.isConnected) returnTo.focus();
      else byId("causa-main").focus();
      returnTo = null;
    }
  }).observe(app, { childList: true, subtree: true });
};

const start = async () => {
  setStatus("Starting Causa…");
  const transport = new DotnetWasmTransport({
    loadRuntime: () => import(new URL("./wasm/_framework/dotnet.js", import.meta.url).href),
    exportPath: ["CausaEngine", "Dispatch"],
  });
  const kernel = new BrowserKernel(
    transport,
    document,
    {
      report: (/** @type {{ kind: string, phase?: string }} */ event) => {
        diagnostics.push({ kind: event.kind, phase: "phase" in event ? event.phase : undefined });
      },
    },
    { requireHandshake: true },
  );
  /** @type {ReturnType<typeof setTimeout> | undefined} */
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error("timeout")), START_TIMEOUT_MS);
  });
  // The .NET loader downloads framework files in parallel and reports a failed one (a 503, an integrity mismatch)
  // only as an unhandled rejection; the start promise then never settles. Any rejection while starting ends the
  // start at once with the recoverable failure, instead of a spinner until the timeout.
  /** @type {(event: PromiseRejectionEvent) => void} */
  let onRejection = () => {};
  const loaderFailure = new Promise((_, reject) => {
    onRejection = (event) => {
      event.preventDefault();
      reject(new Error("loader"));
    };
  });
  window.addEventListener("unhandledrejection", onRejection);
  try {
    await Promise.race([kernel.start(), timeout, loaderFailure]);
  } catch (error) {
    fail(error instanceof Error && error.message === "timeout"
      ? "Starting took too long. Your connection may be slow or interrupted."
      : error instanceof Error && error.message === "loader"
        ? "Part of the application could not be downloaded. Your connection or the site may be briefly unavailable."
        : "The application engine could not be loaded in this browser.");
    return;
  } finally {
    clearTimeout(timer);
    window.removeEventListener("unhandledrejection", onRejection);
  }
  if (kernel.status !== "running") {
    fail("The application engine did not accept this page (version mismatch). Reload to get a consistent version.");
    return;
  }
  // Ready only once the engine answered Initialize with its first complete projection (WASM-LOAD-004).
  watchFocus();
  app.hidden = false;
  boot.hidden = true;
  boot.dataset.bootState = "ready";
  document.documentElement.dataset.boot = "ready";
  byId("causa-main").focus({ preventScroll: true });
};

start();
