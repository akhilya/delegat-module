import {
  AxiosError,
  axios_default
} from "./chunk-4XUY7AL3.js";
import {
  isRef,
  nextTick,
  ref,
  shallowRef,
  toValue,
  watch
} from "./chunk-IDACCYAP.js";
import "./chunk-6KBIBHQC.js";

// node_modules/@vueuse/shared/index.mjs
var isClient = typeof window !== "undefined" && typeof document !== "undefined";
var isWorker = typeof WorkerGlobalScope !== "undefined" && globalThis instanceof WorkerGlobalScope;
var noop = () => {
};
var isIOS = getIsIOS();
function getIsIOS() {
  var _a, _b;
  return isClient && ((_a = window == null ? void 0 : window.navigator) == null ? void 0 : _a.userAgent) && (/iP(?:ad|hone|od)/.test(window.navigator.userAgent) || ((_b = window == null ? void 0 : window.navigator) == null ? void 0 : _b.maxTouchPoints) > 2 && /iPad|Macintosh/.test(window == null ? void 0 : window.navigator.userAgent));
}
function promiseTimeout(ms, throwOnTimeout = false, reason = "Timeout") {
  return new Promise((resolve, reject) => {
    if (throwOnTimeout)
      setTimeout(() => reject(reason), ms);
    else
      setTimeout(resolve, ms);
  });
}
function cacheStringFunction(fn) {
  const cache = /* @__PURE__ */ Object.create(null);
  return (str) => {
    const hit = cache[str];
    return hit || (cache[str] = fn(str));
  };
}
var hyphenateRE = /\B([A-Z])/g;
var hyphenate = cacheStringFunction((str) => str.replace(hyphenateRE, "-$1").toLowerCase());
var camelizeRE = /-(\w)/g;
var camelize = cacheStringFunction((str) => {
  return str.replace(camelizeRE, (_, c) => c ? c.toUpperCase() : "");
});
function createUntil(r, isNot = false) {
  function toMatch(condition, { flush = "sync", deep = false, timeout, throwOnTimeout } = {}) {
    let stop = null;
    const watcher = new Promise((resolve) => {
      stop = watch(
        r,
        (v) => {
          if (condition(v) !== isNot) {
            if (stop)
              stop();
            else
              nextTick(() => stop == null ? void 0 : stop());
            resolve(v);
          }
        },
        {
          flush,
          deep,
          immediate: true
        }
      );
    });
    const promises = [watcher];
    if (timeout != null) {
      promises.push(
        promiseTimeout(timeout, throwOnTimeout).then(() => toValue(r)).finally(() => stop == null ? void 0 : stop())
      );
    }
    return Promise.race(promises);
  }
  function toBe(value, options) {
    if (!isRef(value))
      return toMatch((v) => v === value, options);
    const { flush = "sync", deep = false, timeout, throwOnTimeout } = options != null ? options : {};
    let stop = null;
    const watcher = new Promise((resolve) => {
      stop = watch(
        [r, value],
        ([v1, v2]) => {
          if (isNot !== (v1 === v2)) {
            if (stop)
              stop();
            else
              nextTick(() => stop == null ? void 0 : stop());
            resolve(v1);
          }
        },
        {
          flush,
          deep,
          immediate: true
        }
      );
    });
    const promises = [watcher];
    if (timeout != null) {
      promises.push(
        promiseTimeout(timeout, throwOnTimeout).then(() => toValue(r)).finally(() => {
          stop == null ? void 0 : stop();
          return toValue(r);
        })
      );
    }
    return Promise.race(promises);
  }
  function toBeTruthy(options) {
    return toMatch((v) => Boolean(v), options);
  }
  function toBeNull(options) {
    return toBe(null, options);
  }
  function toBeUndefined(options) {
    return toBe(void 0, options);
  }
  function toBeNaN(options) {
    return toMatch(Number.isNaN, options);
  }
  function toContains(value, options) {
    return toMatch((v) => {
      const array = Array.from(v);
      return array.includes(value) || array.includes(toValue(value));
    }, options);
  }
  function changed(options) {
    return changedTimes(1, options);
  }
  function changedTimes(n = 1, options) {
    let count = -1;
    return toMatch(() => {
      count += 1;
      return count >= n;
    }, options);
  }
  if (Array.isArray(toValue(r))) {
    const instance = {
      toMatch,
      toContains,
      changed,
      changedTimes,
      get not() {
        return createUntil(r, !isNot);
      }
    };
    return instance;
  } else {
    const instance = {
      toMatch,
      toBe,
      toBeTruthy,
      toBeNull,
      toBeNaN,
      toBeUndefined,
      changed,
      changedTimes,
      get not() {
        return createUntil(r, !isNot);
      }
    };
    return instance;
  }
}
function until(r) {
  return createUntil(r);
}

// node_modules/@vueuse/integrations/useAxios.mjs
function useAxios(...args) {
  const url = typeof args[0] === "string" ? args[0] : void 0;
  const argsPlaceholder = typeof url === "string" ? 1 : 0;
  const defaultOptions = {
    immediate: !!argsPlaceholder,
    shallow: true,
    abortPrevious: true
  };
  let defaultConfig = {};
  let instance = axios_default;
  let options = defaultOptions;
  const isAxiosInstance = (val) => !!(val == null ? void 0 : val.request);
  if (args.length > 0 + argsPlaceholder) {
    if (isAxiosInstance(args[0 + argsPlaceholder]))
      instance = args[0 + argsPlaceholder];
    else
      defaultConfig = args[0 + argsPlaceholder];
  }
  if (args.length > 1 + argsPlaceholder) {
    if (isAxiosInstance(args[1 + argsPlaceholder]))
      instance = args[1 + argsPlaceholder];
  }
  if (args.length === 2 + argsPlaceholder && !isAxiosInstance(args[1 + argsPlaceholder]) || args.length === 3 + argsPlaceholder) {
    options = args[args.length - 1] || defaultOptions;
  }
  const {
    shallow,
    onSuccess = noop,
    onError = noop,
    immediate,
    resetOnExecute = false
  } = options;
  const initialData = options.initialData;
  const response = shallowRef();
  const data = (shallow ? shallowRef : ref)(initialData);
  const isFinished = shallowRef(false);
  const isLoading = shallowRef(false);
  const isAborted = shallowRef(false);
  const error = shallowRef();
  let abortController = new AbortController();
  const abort = (message) => {
    if (isFinished.value || !isLoading.value)
      return;
    abortController.abort(message);
    abortController = new AbortController();
    isAborted.value = true;
    isLoading.value = false;
    isFinished.value = false;
  };
  const loading = (loading2) => {
    isLoading.value = loading2;
    isFinished.value = !loading2;
  };
  const resetData = () => {
    if (resetOnExecute)
      data.value = initialData;
  };
  const waitUntilFinished = () => new Promise((resolve, reject) => {
    until(isFinished).toBe(true).then(() => error.value ? reject(error.value) : resolve(result));
  });
  const promise = {
    then: (...args2) => waitUntilFinished().then(...args2),
    catch: (...args2) => waitUntilFinished().catch(...args2)
  };
  let executeCounter = 0;
  const execute = (executeUrl = url, config = {}) => {
    error.value = void 0;
    const _url = typeof executeUrl === "string" ? executeUrl : url != null ? url : config.url;
    if (_url === void 0) {
      error.value = new AxiosError(AxiosError.ERR_INVALID_URL);
      isFinished.value = true;
      return promise;
    }
    resetData();
    if (options.abortPrevious !== false)
      abort();
    loading(true);
    executeCounter += 1;
    const currentExecuteCounter = executeCounter;
    isAborted.value = false;
    instance(_url, { ...defaultConfig, ...typeof executeUrl === "object" ? executeUrl : config, signal: abortController.signal }).then((r) => {
      if (isAborted.value)
        return;
      response.value = r;
      const result2 = r.data;
      data.value = result2;
      onSuccess(result2);
    }).catch((e) => {
      error.value = e;
      onError(e);
    }).finally(() => {
      var _a;
      (_a = options.onFinish) == null ? void 0 : _a.call(options);
      if (currentExecuteCounter === executeCounter)
        loading(false);
    });
    return promise;
  };
  if (immediate && url)
    execute();
  const result = {
    response,
    data,
    error,
    isFinished,
    isLoading,
    cancel: abort,
    isAborted,
    isCanceled: isAborted,
    abort,
    execute
  };
  return {
    ...result,
    ...promise
  };
}
export {
  useAxios
};
//# sourceMappingURL=@vueuse_integrations_useAxios.js.map
