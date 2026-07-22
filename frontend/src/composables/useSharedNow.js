import { ref, onMounted, onUnmounted } from "vue";

/** Shared 1s clock for operation elapsed times (one interval for all OperationItem rows). */
const now = ref(Date.now());
let subscribers = 0;
let timer = null;

function ensureTimer() {
  if (timer != null) return;
  timer = setInterval(() => {
    now.value = Date.now();
  }, 1000);
}

function releaseTimer() {
  if (subscribers > 0 || timer == null) return;
  clearInterval(timer);
  timer = null;
}

/**
 * @returns {import('vue').Ref<number>}
 */
export function useSharedNow() {
  onMounted(() => {
    subscribers += 1;
    ensureTimer();
  });
  onUnmounted(() => {
    subscribers = Math.max(0, subscribers - 1);
    releaseTimer();
  });
  return now;
}
