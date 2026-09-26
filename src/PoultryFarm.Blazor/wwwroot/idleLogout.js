let checkId = null;
let dotNetRef = null;
let timeoutMs = 300000;
let lastActivityAt = Date.now();
let loggingOut = false;

const ACTIVITY_EVENTS = ["mousemove", "mousedown", "keydown", "scroll", "touchstart", "pointerdown", "wheel"];
const WAKE_EVENTS = ["focus", "pageshow"];
const ACTIVITY_STORAGE_KEY = "poultryfarm.lastActivityAt";
const CHECK_EVERY_MS = 5000;
const ACTIVITY_WRITE_THROTTLE_MS = 1000;

let lastStorageWriteAt = 0;

function now() {
  return Date.now();
}

function readSharedActivity() {
  try {
    const raw = window.localStorage.getItem(ACTIVITY_STORAGE_KEY);
    const parsed = raw ? Number(raw) : NaN;
    return Number.isFinite(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function writeSharedActivity(timestamp) {
  try {
    window.localStorage.setItem(ACTIVITY_STORAGE_KEY, String(timestamp));
  } catch {
    // Ignore quota / private-mode failures.
  }
}

function markActivity() {
  if (!dotNetRef || loggingOut) {
    return;
  }

  lastActivityAt = now();
  const writeNow = lastActivityAt;
  if (writeNow - lastStorageWriteAt >= ACTIVITY_WRITE_THROTTLE_MS) {
    lastStorageWriteAt = writeNow;
    writeSharedActivity(writeNow);
  }
}

function effectiveLastActivity() {
  const shared = readSharedActivity();
  if (shared != null && shared > lastActivityAt) {
    lastActivityAt = shared;
  }
  return lastActivityAt;
}

async function logoutIfIdle() {
  if (!dotNetRef || loggingOut) {
    return;
  }

  const idleFor = now() - effectiveLastActivity();
  if (idleFor < timeoutMs) {
    return;
  }

  loggingOut = true;
  stopTimersOnly();
  try {
    await dotNetRef.invokeMethodAsync("OnIdleTimeout");
  } catch {
    // Circuit may already be gone.
  }
}

function onVisibilityChange() {
  // Always re-check on show/hide so minimized/background time still counts.
  void logoutIfIdle();
}

function onStorage(event) {
  if (event.key !== ACTIVITY_STORAGE_KEY || event.newValue == null) {
    return;
  }

  const parsed = Number(event.newValue);
  if (Number.isFinite(parsed) && parsed > lastActivityAt) {
    lastActivityAt = parsed;
  }
}

function stopTimersOnly() {
  if (checkId != null) {
    window.clearInterval(checkId);
    checkId = null;
  }
}

function bindListeners() {
  ACTIVITY_EVENTS.forEach((eventName) =>
    window.addEventListener(eventName, markActivity, { passive: true, capture: true }));
  WAKE_EVENTS.forEach((eventName) =>
    window.addEventListener(eventName, onVisibilityChange));
  document.addEventListener("visibilitychange", onVisibilityChange);
  window.addEventListener("storage", onStorage);
}

function unbindListeners() {
  ACTIVITY_EVENTS.forEach((eventName) =>
    window.removeEventListener(eventName, markActivity, { capture: true }));
  WAKE_EVENTS.forEach((eventName) =>
    window.removeEventListener(eventName, onVisibilityChange));
  document.removeEventListener("visibilitychange", onVisibilityChange);
  window.removeEventListener("storage", onStorage);
}

export function start(ref, timeoutMilliseconds) {
  stop();
  dotNetRef = ref;
  timeoutMs = timeoutMilliseconds || 300000;
  loggingOut = false;
  lastActivityAt = now();
  writeSharedActivity(lastActivityAt);
  bindListeners();
  checkId = window.setInterval(() => {
    void logoutIfIdle();
  }, CHECK_EVERY_MS);
  void logoutIfIdle();
}

export function updateTimeout(timeoutMilliseconds) {
  timeoutMs = timeoutMilliseconds || 300000;
  // Changing preference should not treat the user as newly active forever,
  // but do restart the idle window from "now" so they aren't logged out mid-save.
  if (dotNetRef) {
    markActivity();
  }
}

export function stop() {
  stopTimersOnly();
  unbindListeners();
  dotNetRef = null;
  loggingOut = false;
}
