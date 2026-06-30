let timerId = null;
let dotNetRef = null;
let timeout = 180000;

const events = ["mousemove", "mousedown", "keydown", "scroll", "touchstart"];

function resetTimer() {
  if (!dotNetRef) return;
  window.clearTimeout(timerId);
  timerId = window.setTimeout(() => {
    dotNetRef.invokeMethodAsync("OnIdleTimeout");
  }, timeout);
}

export function start(ref, timeoutMilliseconds) {
  stop();
  dotNetRef = ref;
  timeout = timeoutMilliseconds || 180000;
  events.forEach((eventName) => window.addEventListener(eventName, resetTimer, { passive: true }));
  resetTimer();
}

export function stop() {
  if (timerId) {
    window.clearTimeout(timerId);
    timerId = null;
  }
  events.forEach((eventName) => window.removeEventListener(eventName, resetTimer));
  dotNetRef = null;
}
