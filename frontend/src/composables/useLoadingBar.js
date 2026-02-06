import { ref, computed } from "vue";

const isLoading = ref(false);
const width = ref(0);
const opacity = ref(1);

let rafId = null;
let slowTimer = null;

export function startLoading() {
  cancelAnimationFrame(rafId);
  clearTimeout(slowTimer);
  width.value = 0;
  opacity.value = 1;
  isLoading.value = true;

  rafId = requestAnimationFrame(() => {
    width.value = 15;
  });

  slowTimer = setTimeout(() => {
    width.value = 80;
  }, 50);
}

export function finishLoading() {
  clearTimeout(slowTimer);
  cancelAnimationFrame(rafId);
  width.value = 100;

  setTimeout(() => {
    opacity.value = 0;
    setTimeout(() => {
      isLoading.value = false;
      width.value = 0;
      opacity.value = 1;
    }, 300);
  }, 200);
}

export const loadingBarState = computed(() => ({
  isLoading: isLoading.value,
  style: {
    width: `${width.value}%`,
    opacity: opacity.value,
  },
}));
