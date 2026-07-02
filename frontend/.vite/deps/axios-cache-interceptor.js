import "./chunk-6KBIBHQC.js";

// node_modules/cache-parser/dist/index.mjs
var e = Symbol("cache-parser");
function r(e5, r5) {
  (null == r5 || r5 > e5.length) && (r5 = e5.length);
  for (var t5 = 0, a2 = Array(r5); t5 < r5; t5++) a2[t5] = e5[t5];
  return a2;
}
function t(e5) {
  return ("string" == typeof e5 || "number" == typeof e5) && (e5 = Number(e5)) >= 0 && e5 < Number.POSITIVE_INFINITY;
}
function a(e5) {
  return true === e5 || "number" == typeof e5 || "string" == typeof e5 && "false" !== e5;
}
var n = Number;
function o(o3) {
  var i = Object.defineProperty({}, e, { enumerable: false, value: 1 });
  if (!o3 || "string" != typeof o3) return i;
  var s = function(e5) {
    for (var t5, a2 = {}, n4 = function(e6) {
      var t6 = "undefined" != typeof Symbol && e6[Symbol.iterator] || e6["@@iterator"];
      if (t6) return (t6 = t6.call(e6)).next.bind(t6);
      if (Array.isArray(e6) || (t6 = function(e7, t7) {
        if (e7) {
          if ("string" == typeof e7) return r(e7, t7);
          var a4 = {}.toString.call(e7).slice(8, -1);
          return "Object" === a4 && e7.constructor && (a4 = e7.constructor.name), "Map" === a4 || "Set" === a4 ? Array.from(e7) : "Arguments" === a4 || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(a4) ? r(e7, t7) : void 0;
        }
      }(e6))) {
        t6 && (e6 = t6);
        var a3 = 0;
        return function() {
          return a3 >= e6.length ? { done: true } : { done: false, value: e6[a3++] };
        };
      }
      throw new TypeError("Invalid attempt to iterate non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
    }(e5.toLowerCase().replace(/\s+/g, "").split(",")); !(t5 = n4()).done; ) {
      var o4, i2 = t5.value.split("=", 2);
      a2[i2[0]] = null == (o4 = i2[1]) || o4;
    }
    return a2;
  }(o3), u = s["max-age"], l = s["max-stale"], m = s["min-fresh"], p = s["s-maxage"], f = s["stale-if-error"], c = s["stale-while-revalidate"];
  return a(s.immutable) && (i.immutable = true), t(u) && (i.maxAge = n(u)), t(l) && (i.maxStale = n(l)), t(m) && (i.minFresh = n(m)), a(s["must-revalidate"]) && (i.mustRevalidate = true), a(s["must-understand"]) && (i.mustUnderstand = true), a(s["no-cache"]) && (i.noCache = true), a(s["no-store"]) && (i.noStore = true), a(s["no-transform"]) && (i.noTransform = true), a(s["only-if-cached"]) && (i.onlyIfCached = true), a(s.private) && (i.private = true), a(s["proxy-revalidate"]) && (i.proxyRevalidate = true), a(s.public) && (i.public = true), t(p) && (i.sMaxAge = n(p)), t(f) && (i.staleIfError = n(f)), t(c) && (i.staleWhileRevalidate = n(c)), i;
}

// node_modules/fast-defer/dist/index.mjs
var r2 = Symbol();
function e2() {
  var e5, n4, o3 = new Promise(function(r5, o4) {
    e5 = r5, n4 = o4;
  });
  return o3.resolve = e5, o3.reject = n4, o3[r2] = 1, o3;
}

// node_modules/http-vary/dist/index.mjs
function r3(r5, e5) {
  (null == e5 || e5 > r5.length) && (e5 = r5.length);
  for (var t5 = 0, n4 = Array(e5); t5 < e5; t5++) n4[t5] = r5[t5];
  return n4;
}
function e3(e5, t5) {
  var n4 = "undefined" != typeof Symbol && e5[Symbol.iterator] || e5["@@iterator"];
  if (n4) return (n4 = n4.call(e5)).next.bind(n4);
  if (Array.isArray(e5) || (n4 = function(e6, t6) {
    if (e6) {
      if ("string" == typeof e6) return r3(e6, t6);
      var n5 = {}.toString.call(e6).slice(8, -1);
      return "Object" === n5 && e6.constructor && (n5 = e6.constructor.name), "Map" === n5 || "Set" === n5 ? Array.from(e6) : "Arguments" === n5 || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n5) ? r3(e6, t6) : void 0;
    }
  }(e5)) || t5 && e5 && "number" == typeof e5.length) {
    n4 && (e5 = n4);
    var o3 = 0;
    return function() {
      return o3 >= e5.length ? { done: true } : { done: false, value: e5[o3++] };
    };
  }
  throw new TypeError("Invalid attempt to iterate non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.");
}
function t2(r5, t5, n4) {
  if ("*" === r5 || null === r5) return false;
  for (var o3, a2 = Object.keys(t5), i = Object.keys(n4), l = e3(r5); !(o3 = l()).done; ) {
    for (var u, f = o3.value, v = void 0, d = void 0, s = e3(a2); !(u = s()).done; ) {
      var c = u.value;
      if (c.toLowerCase() === f) {
        var y;
        v = null == (y = t5[c]) || null == (y = y.toString()) ? void 0 : y.trim();
        break;
      }
    }
    for (var b, m = e3(i); !(b = m()).done; ) {
      var g = b.value;
      if (g.toLowerCase() === f) {
        var h;
        d = null == (h = n4[g]) || null == (h = h.toString()) ? void 0 : h.trim();
        break;
      }
    }
    if (v != d) return false;
  }
  return true;
}
var n2 = /^[a-z0-9-]+$/i;
function o2(r5, e5) {
  if (void 0 === e5 && (e5 = 16), "string" != typeof r5) return null;
  if (r5.includes("*")) return "*";
  for (var t5 = /* @__PURE__ */ new Set(), o3 = 0; o3 < r5.length; o3++) {
    var a2 = r5[o3];
    if (" " !== a2 && "	" !== a2 && "," !== a2) {
      for (var i = o3; o3 < r5.length && "," !== r5[o3]; ) o3++;
      var l = r5.slice(i, o3).trim().toLowerCase();
      if (0 !== l.length && n2.test(l) && (t5.add(l), t5.size >= e5)) break;
    }
  }
  return 0 === t5.size ? null : Array.from(t5);
}

// node_modules/object-code/dist/index.mjs
function t3(t5, r5) {
  return t5 > r5 ? 1 : -1;
}
function r4(r5, n4) {
  return t3(String(r5[0]), String(n4[0]));
}
function n3(t5, r5) {
  return 33 * t5 ^ r5;
}
function e4(o3, i) {
  var f = 5381;
  if ("object" == typeof o3 && null !== o3) {
    var u = "function" == typeof o3.entries, a2 = Object.keys(o3).length > 0;
    if (o3.toString === Object.prototype.toString || o3.toString === Array.prototype.toString || u && !a2) {
      i || (i = /* @__PURE__ */ new WeakSet()), i.add(o3);
      var c = u && !a2 ? Array.from(o3.entries()) : Object.keys(o3).sort(t3).map(function(t5) {
        return [t5, o3[t5]];
      });
      u && !a2 && c.sort(r4);
      for (var g = 0; g < c.length; g++) {
        var y = c[g], p = y[1];
        if (f = n3(f, e4(y[0], i)), "object" == typeof p && null !== p && (p.toString === Object.prototype.toString || p.toString === Array.prototype.toString)) {
          if (i.has(p)) continue;
          i.add(p);
        }
        f = n3(f, e4(p, i));
      }
      return n3(f, e4(o3.constructor, i));
    }
  }
  for (var S, b = typeof o3, l = 0; l < b.length; l++) f = n3(f, b.charCodeAt(l));
  if (o3 instanceof Date) return n3(f, o3.getTime());
  if ("number" === b) return n3(f, function(t5) {
    if (Number.isNaN(t5)) return 2146959360;
    if (Infinity === t5) return 2146435072;
    if (-Infinity === t5) return 4293918720;
    if (Math.abs(t5) > Number.MAX_SAFE_INTEGER) {
      for (var r5 = String(t5), e5 = 0, o4 = 0; o4 < r5.length; o4++) e5 = n3(e5, r5.charCodeAt(o4));
      return e5;
    }
    return t5;
  }(o3));
  if ("boolean" === b) return n3(f, o3 ? 1 : 0);
  try {
    S = String(o3);
  } catch (t5) {
    S = Object.prototype.toString.call(o3);
  }
  for (var h = 0; h < S.length; h++) f = n3(f, S.charCodeAt(h));
  return f;
}

// node_modules/try/lib/index.js
var Result = class _Result {
  /**
   * @param {boolean} ok
   * @param {unknown} [error]
   * @param {V} [value]
   */
  constructor(ok2, error2, value) {
    this.ok = !!ok2;
    if (this.ok) {
      this.value = value;
    } else {
      this.error = error2;
    }
  }
  *[Symbol.iterator]() {
    yield this.ok;
    yield this.error;
    yield this.value;
  }
  /**
   * @this void
   * @template T
   * @param {T} value
   */
  static ok(value) {
    return new _Result(true, void 0, value);
  }
  /**
   * @this void
   * @param {unknown} error
   */
  static error(error2) {
    return new _Result(false, error2);
  }
  /**
   * @this void
   * @template T
   * @param {any} result
   * @param {...any} args
   * @returns {Promise<Result<T>> | Result<T>}
   */
  static try(result, ...args) {
    try {
      if (typeof result === "function") {
        result = result.apply(void 0, args);
      }
      if (result instanceof Promise) {
        return result.then(_Result.ok, _Result.error);
      }
      return _Result.ok(result);
    } catch (error2) {
      return _Result.error(error2);
    }
  }
};
var error = Result.error;
var ok = Result.ok;
var t4 = Result.try;

// node_modules/axios-cache-interceptor/dist/index.mjs
var Header = {
  IfModifiedSince: "if-modified-since",
  LastModified: "last-modified",
  IfNoneMatch: "if-none-match",
  CacheControl: "cache-control",
  Pragma: "pragma",
  ETag: "etag",
  Expires: "expires",
  Age: "age",
  XAxiosCacheEtag: "x-axios-cache-etag",
  XAxiosCacheLastModified: "x-axios-cache-last-modified",
  XAxiosCacheStaleIfError: "x-axios-cache-stale-if-error",
  Vary: "vary"
};
var defaultHeaderInterpreter = (headers, location) => {
  if (!headers) return "not enough headers";
  const cacheControl = headers[Header.CacheControl];
  if (cacheControl) {
    const cc = o(String(cacheControl));
    if (cc.noCache || cc.noStore || location === "server" && cc.private) return "dont cache";
    if (cc.immutable) return { cache: 1e3 * 60 * 60 * 24 * 365 };
    if (cc.maxAge !== void 0) {
      const age = headers[Header.Age];
      return {
        cache: age ? (cc.maxAge - Number(age)) * 1e3 : cc.maxAge * 1e3,
        stale: cc.maxStale !== void 0 ? cc.maxStale * 1e3 : cc.staleWhileRevalidate !== void 0 ? cc.staleWhileRevalidate * 1e3 : void 0
      };
    }
  }
  const expires = headers[Header.Expires];
  if (expires) {
    const milliseconds = Date.parse(String(expires)) - Date.now();
    return milliseconds >= 0 ? { cache: milliseconds } : "dont cache";
  }
  return "not enough headers";
};
function extractHeaders(requestHeaders, headerNames) {
  var _a;
  const result = {};
  for (const name of headerNames) result[name] = (_a = requestHeaders.get(name)) == null ? void 0 : _a.toString();
  return result;
}
async function testCachePredicate(response, predicate) {
  if (typeof predicate === "function") return predicate(response);
  const { statusCheck, responseMatch, containsHeaders } = predicate;
  if (statusCheck && !await statusCheck(response.status) || responseMatch && !await responseMatch(response)) return false;
  if (containsHeaders) {
    for (const [header, predicate2] of Object.entries(containsHeaders)) if (!await predicate2(response.headers[header.toLowerCase()] ?? response.headers[header])) return false;
  }
  return true;
}
function regexOrStringMatch(matchPattern, configUrl) {
  if (matchPattern instanceof RegExp) {
    matchPattern.lastIndex = 0;
    return matchPattern.test(configUrl);
  }
  return configUrl.includes(matchPattern);
}
function createValidateStatus(oldValidate) {
  return oldValidate ? (status) => oldValidate(status) || status === 304 : (status) => status >= 200 && status < 300 || status === 304;
}
function isMethodIn(requestMethod = "get", methodList = []) {
  requestMethod = requestMethod.toLowerCase();
  return methodList.some((method) => method === requestMethod);
}
function updateStaleRequest(cache, config) {
  var _a, _b, _c;
  const { etag, modifiedSince } = config.cache;
  const revalidation = (_b = (_a = cache.data) == null ? void 0 : _a.meta) == null ? void 0 : _b.revalidation;
  if (etag) {
    let etagValue;
    if (revalidation == null ? void 0 : revalidation.etag) etagValue = revalidation.etag;
    else if (etag === true) etagValue = (_c = cache.data) == null ? void 0 : _c.headers[Header.ETag];
    else etagValue = etag;
    if (etagValue) config.headers.set(Header.IfNoneMatch, etagValue);
  }
  if (modifiedSince) {
    let lastModifiedValue;
    if (revalidation == null ? void 0 : revalidation.lastModified) lastModifiedValue = revalidation.lastModified === true ? new Date(cache.createdAt).toUTCString() : revalidation.lastModified;
    else if (modifiedSince === true) lastModifiedValue = cache.data.headers[Header.LastModified] || new Date(cache.createdAt).toUTCString();
    else lastModifiedValue = modifiedSince.toUTCString();
    config.headers.set(Header.IfModifiedSince, lastModifiedValue);
  }
}
function createCacheResponse(response, previousCache) {
  if (response.status === 304 && previousCache) {
    response.cached = true;
    response.data = previousCache.data;
    response.status = previousCache.status;
    response.statusText = previousCache.statusText;
    response.headers = {
      ...previousCache.headers,
      ...response.headers
    };
    return previousCache;
  }
  return {
    data: response.data,
    status: response.status,
    statusText: response.statusText,
    headers: response.headers
  };
}
function defaultRequestInterceptor(axios) {
  const onFulfilled = async (config) => {
    var _a, _b, _c, _d, _e, _f, _g, _h;
    config.id = axios.generateKey(config, { vary: config.cache && Array.isArray(config.cache.vary) ? extractHeaders(config.headers, config.cache.vary) : void 0 });
    if (config.cache === false) return config;
    config.cache = {
      ...axios.defaults.cache,
      ...config.cache
    };
    if (config.cache.enabled === false) return config;
    if (typeof config.cache.cachePredicate === "object" && config.cache.cachePredicate.ignoreUrls && config.url) {
      for (const url of config.cache.cachePredicate.ignoreUrls) if (regexOrStringMatch(url, config.url)) return config;
    }
    if (typeof config.cache.cachePredicate === "object" && config.cache.cachePredicate.allowUrls && config.url) {
      let matched = false;
      for (const url of config.cache.cachePredicate.allowUrls) if (regexOrStringMatch(url, config.url)) {
        matched = true;
        break;
      }
      if (!matched) return config;
    }
    if (config.cache.cacheTakeover) {
      config.headers.set(Header.CacheControl, "no-cache, no-store, must-revalidate, max-age=0", false);
      config.headers.set(Header.Pragma, "no-cache", false);
      config.headers.set(Header.Expires, "0", false);
    }
    if (!isMethodIn(config.method, config.cache.methods)) return config;
    let cache = await axios.storage.get(config.id, config);
    const overrideCache = config.cache.override;
    if (config.cache.vary !== false && ((_b = (_a = cache.data) == null ? void 0 : _a.meta) == null ? void 0 : _b.vary) && cache.data.headers[Header.Vary]) {
      const vary = Array.isArray(config.cache.vary) ? config.cache.vary : o2(cache.data.headers[Header.Vary]);
      if (vary && vary !== "*" && !t2(vary, (_c = cache.data.meta) == null ? void 0 : _c.vary, config.headers)) {
        const extractedHeaders = extractHeaders(config.headers, vary);
        const newKey = axios.generateKey({
          ...config,
          id: void 0
        }, { vary: extractedHeaders });
        if (config.id !== newKey) {
          config.id = newKey;
          cache = await axios.storage.get(newKey, config);
        }
      }
    }
    ignoreAndRequest: if (cache.state === "empty" || cache.state === "stale" || cache.state === "must-revalidate" || overrideCache) {
      if (axios.waiting.has(config.id) && !overrideCache) {
        cache = await axios.storage.get(config.id, config);
        if (cache.state !== "empty" && cache.state !== "must-revalidate") break ignoreAndRequest;
      }
      const def = e2();
      axios.waiting.set(config.id, def);
      def.catch(() => void 0);
      await axios.storage.set(config.id, {
        state: "loading",
        previous: overrideCache ? cache.data ? "stale" : "empty" : cache.state,
        data: cache.data,
        createdAt: overrideCache && !cache.createdAt ? Date.now() : cache.createdAt
      }, config);
      if ((cache.state === "stale" || cache.state === "must-revalidate") && !overrideCache) updateStaleRequest(cache, {
        ...config,
        cache: config.cache
      });
      config.validateStatus = createValidateStatus(config.validateStatus);
      if (cache.state === "stale" || cache.data && cache.state !== "must-revalidate") await ((_e = (_d = config.cache).hydrate) == null ? void 0 : _e.call(_d, cache));
      return config;
    }
    let cachedResponse;
    if (cache.state === "loading") {
      const deferred = axios.waiting.get(config.id);
      if (!deferred) {
        if (cache.data) await ((_g = (_f = config.cache).hydrate) == null ? void 0 : _g.call(_f, cache));
        return config;
      }
      try {
        await deferred;
        const state = await axios.storage.get(config.id, config);
        if (!state.data) return onFulfilled(config);
        if (config.cache.vary !== false && ((_h = state.data.meta) == null ? void 0 : _h.vary) && state.data.headers[Header.Vary]) {
          const vary = Array.isArray(config.cache.vary) ? config.cache.vary : o2(state.data.headers[Header.Vary]);
          if (vary && vary !== "*" && !t2(vary, state.data.meta.vary, config.headers)) return onFulfilled(config);
        }
        cachedResponse = state.data;
      } catch (err) {
        throw err;
      }
    } else cachedResponse = cache.data;
    config.transformResponse = void 0;
    config.adapter = function cachedAdapter() {
      return Promise.resolve({
        config,
        data: cachedResponse.data,
        headers: cachedResponse.headers,
        status: cachedResponse.status,
        statusText: cachedResponse.statusText,
        cached: true,
        stale: cache.previous === "stale",
        id: config.id
      });
    };
    return config;
  };
  return { onFulfilled };
}
async function updateCache(storage, data, cacheUpdater) {
  if (typeof cacheUpdater === "function") return cacheUpdater(data);
  for (const [cacheKey, updater] of Object.entries(cacheUpdater)) {
    if (updater === "delete") {
      await storage.remove(cacheKey, data.config);
      continue;
    }
    const value = await storage.get(cacheKey, data.config);
    if (value.state === "loading") continue;
    const newValue = await updater(value, data);
    if (newValue === "delete") {
      await storage.remove(cacheKey, data.config);
      continue;
    }
    if (newValue !== "ignore") await storage.set(cacheKey, newValue, data.config);
  }
}
function defaultResponseInterceptor(axios) {
  const replyDeferred = (responseId, mode, error2) => {
    const deferred = axios.waiting.get(responseId);
    if (deferred) {
      deferred[mode](error2);
      axios.waiting.delete(responseId);
    }
  };
  const onFulfilled = async (response) => {
    if (!(response == null ? void 0 : response.config)) throw response;
    response.id = response.config.id;
    response.cached ?? (response.cached = false);
    const config = response.config;
    const cacheConfig = config.cache;
    if (response.cached) return response;
    if (!cacheConfig) {
      response.cached = false;
      return response;
    }
    if (cacheConfig.update) await updateCache(axios.storage, response, cacheConfig.update);
    if (!isMethodIn(config.method, cacheConfig.methods)) return response;
    const cache = await axios.storage.get(response.id, config);
    if (cache.state !== "loading") {
      axios.waiting.delete(response.id);
      return response;
    }
    if (!cache.data && !await testCachePredicate(response, cacheConfig.cachePredicate)) {
      replyDeferred(response.id, "resolve");
      return response;
    }
    for (const header of Object.keys(response.headers)) if (header.startsWith("x-axios-cache")) delete response.headers[header];
    let ttl = cacheConfig.ttl || -1;
    let staleTtl;
    if (cacheConfig.interpretHeader) {
      const expirationTime = axios.headerInterpreter(response.headers, axios.location);
      if (expirationTime === "dont cache") {
        replyDeferred(response.id, "resolve");
        return response;
      }
      if (expirationTime !== "not enough headers") if (typeof expirationTime === "number") ttl = expirationTime;
      else {
        ttl = expirationTime.cache;
        staleTtl = expirationTime.stale;
      }
    }
    if (typeof ttl === "function") ttl = await ttl(response);
    const data = createCacheResponse(response, cache.data);
    if (cacheConfig.etag || cacheConfig.modifiedSince) {
      data.meta ?? (data.meta = {});
      data.meta.revalidation = {};
      if (cacheConfig.etag) {
        const etag = cacheConfig.etag === true ? response.headers[Header.ETag] : cacheConfig.etag;
        if (etag) data.meta.revalidation.etag = etag;
      }
      if (cacheConfig.modifiedSince) data.meta.revalidation.lastModified = cacheConfig.modifiedSince === true ? response.headers[Header.LastModified] || true : cacheConfig.modifiedSince.toUTCString();
    }
    if (cacheConfig.vary !== false && response.headers[Header.Vary]) {
      const vary = Array.isArray(cacheConfig.vary) ? cacheConfig.vary : o2(response.headers[Header.Vary]);
      if (Array.isArray(vary)) {
        data.meta ?? (data.meta = {});
        data.meta.vary = extractHeaders(config.headers, vary);
      } else if (vary === "*") {
        await axios.storage.set(response.id, {
          state: "stale",
          createdAt: Date.now(),
          data,
          ttl
        }, config);
        replyDeferred(response.id, "resolve");
        return response;
      }
    }
    const newCache = {
      state: "cached",
      ttl,
      staleTtl,
      createdAt: Date.now(),
      data
    };
    await axios.storage.set(response.id, newCache, config);
    replyDeferred(response.id, "resolve");
    return response;
  };
  const onRejected = async (error2) => {
    if (!error2.isAxiosError || !error2.config) throw error2;
    const config = error2.config;
    const id = config.id;
    const cacheConfig = config.cache;
    const response = error2.response;
    if (!cacheConfig || !id) throw error2;
    if (!isMethodIn(config.method, cacheConfig.methods)) {
      await axios.storage.remove(id, config);
      replyDeferred(id, "reject", error2);
      throw error2;
    }
    const cache = await axios.storage.get(id, config);
    if (cache.state !== "loading" || cache.previous !== "stale") {
      if (error2.code !== "ERR_CANCELED" || error2.code === "ERR_CANCELED" && cache.state !== "cached") await axios.storage.remove(id, config);
      if (error2.code === "ERR_CANCELED") replyDeferred(id, "resolve");
      else replyDeferred(id, "reject", error2);
      throw error2;
    }
    if (cacheConfig.staleIfError) {
      const cacheControl = String(response == null ? void 0 : response.headers[Header.CacheControl]);
      const staleHeader = cacheControl && o(cacheControl).staleIfError;
      const staleIfError = typeof cacheConfig.staleIfError === "function" ? await cacheConfig.staleIfError(response, cache, error2) : cacheConfig.staleIfError === true && staleHeader ? staleHeader * 1e3 : cacheConfig.staleIfError;
      if (staleIfError === true || typeof staleIfError === "number" && cache.createdAt + staleIfError > Date.now()) {
        await axios.storage.set(id, {
          state: "stale",
          createdAt: Date.now(),
          data: cache.data
        }, config);
        const waiting = axios.waiting.get(id);
        if (waiting) {
          waiting.resolve();
          axios.waiting.delete(id);
        }
        return {
          cached: true,
          stale: true,
          config,
          id,
          data: cache.data.data,
          headers: cache.data.headers,
          status: cache.data.status,
          statusText: cache.data.statusText
        };
      }
    }
    await axios.storage.remove(id, config);
    replyDeferred(id, "reject", error2);
    throw error2;
  };
  return {
    onFulfilled,
    onRejected
  };
}
var isStorage = (obj) => !!obj && !!obj["is-storage"];
function migrateRevalidationHeaders(data) {
  var _a;
  if ((_a = data.meta) == null ? void 0 : _a.revalidation) return;
  const oldEtag = data.headers[Header.XAxiosCacheEtag];
  const oldLastModified = data.headers[Header.XAxiosCacheLastModified];
  if (oldEtag || oldLastModified) {
    data.meta ?? (data.meta = {});
    data.meta.revalidation = {};
    if (oldEtag) data.meta.revalidation.etag = oldEtag;
    if (oldLastModified) data.meta.revalidation.lastModified = oldLastModified === "use-cache-timestamp" ? true : oldLastModified;
    delete data.headers[Header.XAxiosCacheEtag];
    delete data.headers[Header.XAxiosCacheLastModified];
    delete data.headers[Header.XAxiosCacheStaleIfError];
  }
}
function hasRevalidationMetadata(value) {
  var _a;
  migrateRevalidationHeaders(value.data);
  const headers = value.data.headers;
  const revalidation = (_a = value.data.meta) == null ? void 0 : _a.revalidation;
  return Header.ETag in headers || Header.LastModified in headers || !!((revalidation == null ? void 0 : revalidation.etag) || (revalidation == null ? void 0 : revalidation.lastModified));
}
function mustRevalidate(value) {
  return String(value.data.headers[Header.CacheControl]).includes("must-revalidate");
}
function canStale(value) {
  if (hasRevalidationMetadata(value)) return true;
  return value.state === "cached" && value.staleTtl !== void 0 && Math.abs(Date.now() - (value.createdAt + value.ttl)) <= value.staleTtl;
}
function isExpired(value) {
  return value.ttl !== void 0 && value.createdAt + value.ttl <= Date.now();
}
var StateEvictionOrder = {
  empty: 0,
  "must-revalidate": 1,
  stale: 2,
  cached: 3,
  loading: 4
};
function storageEntriesSorter([, a2], [, b]) {
  const stateDiff = StateEvictionOrder[a2.state] - StateEvictionOrder[b.state];
  if (stateDiff !== 0) return stateDiff;
  return (a2.createdAt || 0) - (b.createdAt || 0);
}
function canRemoveStorageEntry(value, maxStaleAge) {
  switch (value.state) {
    case "loading":
      return false;
    case "empty":
    case "must-revalidate":
      return true;
    case "cached":
      return isExpired(value) && !canStale(value);
    case "stale":
      if (maxStaleAge !== void 0 && value.ttl !== void 0) return Date.now() > value.createdAt + value.ttl + maxStaleAge;
      return false;
  }
}
function buildStorage({ set, find, remove, clear }) {
  return {
    "is-storage": 1,
    set,
    remove,
    clear,
    get: async (key, config) => {
      let value = await find(key, config);
      if (!value) return { state: "empty" };
      if (value.state === "empty" || value.state === "loading" || value.state === "must-revalidate") return value;
      if (value.state === "cached" || value.state === "stale") migrateRevalidationHeaders(value.data);
      if (value.state === "cached") {
        if (!isExpired(value)) return value;
        if (!canStale(value)) {
          await remove(key, config);
          return { state: "empty" };
        }
        value = {
          state: "stale",
          createdAt: value.createdAt,
          data: value.data,
          ttl: value.staleTtl !== void 0 ? value.staleTtl + value.ttl : void 0
        };
        await set(key, value, config);
        if (mustRevalidate(value)) return {
          ...value,
          state: "must-revalidate"
        };
      }
      if (!isExpired(value)) return value;
      if (hasRevalidationMetadata(value)) return value;
      await remove(key, config);
      return { state: "empty" };
    }
  };
}
var clone = typeof structuredClone === "function" ? structuredClone : (value) => JSON.parse(JSON.stringify(value));
function buildMemoryStorage(cloneData = false, cleanupInterval = 300 * 1e3, maxEntries = 1024, maxStaleAge = 3600 * 1e3) {
  function sortedEntries() {
    return Array.from(storage.data.entries()).sort(storageEntriesSorter);
  }
  const storage = buildStorage({
    set: (key, value) => {
      if (maxEntries && storage.data.size >= maxEntries) {
        storage.cleanup();
        if (storage.data.size >= maxEntries) for (const [key2] of sortedEntries()) {
          storage.data.delete(key2);
          if (storage.data.size < maxEntries) break;
        }
      }
      storage.data.set(key, cloneData === "double" ? clone(value) : value);
    },
    remove: (key) => {
      storage.data.delete(key);
    },
    find: (key) => {
      const value = storage.data.get(key);
      return cloneData && value !== void 0 ? clone(value) : value;
    },
    clear: () => {
      storage.data.clear();
    }
  });
  storage.data = /* @__PURE__ */ new Map();
  storage.cleanup = () => {
    for (const [key, value] of sortedEntries()) if (canRemoveStorageEntry(value, maxStaleAge)) storage.data.delete(key);
  };
  if (cleanupInterval) {
    storage.cleaner = setInterval(storage.cleanup, cleanupInterval);
    if (typeof storage.cleaner === "object" && "unref" in storage.cleaner) storage.cleaner.unref();
  }
  return storage;
}
var SLASHES_REGEX = /^\/|\/$/g;
function buildKeyGenerator(generator) {
  return (request, meta) => {
    if (request.id) return request.id;
    const key = generator(request, meta);
    if (typeof key === "string" || typeof key === "number") return `${key}`;
    return `${e4(key)}`;
  };
}
var defaultKeyGenerator = buildKeyGenerator(({ baseURL, url, method, params, data }, meta) => {
  if (baseURL !== void 0) baseURL = baseURL.replace(SLASHES_REGEX, "");
  else baseURL = "";
  if (url !== void 0) url = url.replace(SLASHES_REGEX, "");
  else url = "";
  if (method !== void 0) method = method.toLowerCase();
  else method = "get";
  return {
    url: baseURL + (baseURL && url ? "/" : "") + url,
    params,
    method,
    data,
    ...meta
  };
});
function setupCache(axios, options = {}) {
  const axiosCache = axios;
  if (axiosCache.defaults.cache) throw new Error("setupCache() should be called only once");
  axiosCache.location = typeof window === "undefined" ? "server" : "client";
  axiosCache.storage = options.storage || buildMemoryStorage();
  if (!isStorage(axiosCache.storage)) throw new Error("Use buildStorage() function");
  axiosCache.waiting = options.waiting || /* @__PURE__ */ new Map();
  axiosCache.generateKey = options.generateKey || defaultKeyGenerator;
  axiosCache.headerInterpreter = options.headerInterpreter || defaultHeaderInterpreter;
  axiosCache.requestInterceptor = options.requestInterceptor || defaultRequestInterceptor(axiosCache);
  axiosCache.responseInterceptor = options.responseInterceptor || defaultResponseInterceptor(axiosCache);
  axiosCache.debug = options.debug || function noop() {
  };
  axiosCache.defaults.cache = {
    enabled: options.enabled ?? true,
    update: options.update || {},
    ttl: options.ttl ?? 1e3 * 60 * 5,
    methods: options.methods || ["get", "head"],
    cachePredicate: options.cachePredicate || { statusCheck: (status) => [
      200,
      203,
      300,
      301,
      302,
      404,
      405,
      410,
      414,
      501
    ].includes(status) },
    etag: options.etag ?? true,
    modifiedSince: options.modifiedSince ?? options.etag === false,
    interpretHeader: options.interpretHeader ?? true,
    cacheTakeover: options.cacheTakeover ?? true,
    staleIfError: options.staleIfError ?? true,
    override: options.override ?? false,
    hydrate: options.hydrate ?? void 0,
    vary: options.vary ?? true
  };
  if (options.register ?? true) {
    axiosCache.interceptors.request.use(axiosCache.requestInterceptor.onFulfilled, axiosCache.requestInterceptor.onRejected);
    axiosCache.interceptors.response.use(axiosCache.responseInterceptor.onFulfilled, axiosCache.responseInterceptor.onRejected);
  }
  return axiosCache;
}
function buildWebStorage(storage, prefix = "axios-cache-", maxStaleAge = 3600 * 1e3) {
  function save(key, value) {
    storage.setItem(prefix + key, JSON.stringify(value));
  }
  return buildStorage({
    clear: () => {
      for (const key in storage) if (key.startsWith(prefix)) storage.removeItem(key);
    },
    find: (key) => {
      const json = storage.getItem(prefix + key);
      return json ? JSON.parse(json) : void 0;
    },
    remove: (key) => {
      storage.removeItem(prefix + key);
    },
    set: (key, value) => {
      const result = Result.try(save, key, value);
      if (result.ok) return;
      if (!isDomQuotaExceededError(result.error)) throw result.error;
      const allValues = Object.entries(storage).filter(([key2]) => key2.startsWith(prefix)).map(([key2, value2]) => [key2, JSON.parse(value2)]);
      for (const [key2, value2] of allValues) if (canRemoveStorageEntry(value2, maxStaleAge)) storage.removeItem(key2);
      const retry = Result.try(save, key, value);
      if (retry.ok) return;
      if (!isDomQuotaExceededError(retry.error)) throw retry.error;
      const descItems = allValues.sort((a2, b) => (a2[1].createdAt || 0) - (b[1].createdAt || 0));
      for (const item of descItems) {
        storage.removeItem(item[0]);
        const lastTry = Result.try(save, key, value);
        if (lastTry.ok) return;
        if (!isDomQuotaExceededError(lastTry.error)) throw lastTry.error;
      }
    }
  });
}
function isDomQuotaExceededError(error2) {
  var _a;
  return (error2 instanceof DOMException || typeof error2 === "object" && error2 !== null && "name" in error2 && ((_a = error2.constructor) == null ? void 0 : _a.name) === "DOMException") && "name" in error2 && (error2.name === "QuotaExceededError" || error2.name === "NS_ERROR_DOM_QUOTA_REACHED" || error2.name === "QUOTA_EXCEEDED_ERR");
}
export {
  Header,
  buildKeyGenerator,
  buildMemoryStorage,
  buildStorage,
  buildWebStorage,
  canRemoveStorageEntry,
  canStale,
  createCacheResponse,
  createValidateStatus,
  defaultHeaderInterpreter,
  defaultKeyGenerator,
  defaultRequestInterceptor,
  defaultResponseInterceptor,
  isExpired,
  isMethodIn,
  isStorage,
  mustRevalidate,
  regexOrStringMatch,
  setupCache,
  storageEntriesSorter,
  testCachePredicate,
  updateCache,
  updateStaleRequest
};
//# sourceMappingURL=axios-cache-interceptor.js.map
