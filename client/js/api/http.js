import { ApiError, Kind, errorFromResponse } from './errors.js';

export function createApiClient({
  baseUrl,
  tokens,
  fetchImpl = (...args) => globalThis.fetch(...args),
  timeoutMs = 8000,
  onSessionExpired = () => {},
  now = () => Date.now(),
  logger = console,
} = {}) {
  if (!baseUrl) throw new Error('Не задано apiBaseUrl у config.js');

  const base = baseUrl.replace(/\/+$/, '');
  let blockedUntil = 0;
  let refreshing = null;
  const rateListeners = new Set();

  const rateLimit = {
    
    get remainingSeconds() { return Math.max(0, Math.ceil((blockedUntil - now()) / 1000)); },
    get until() { return blockedUntil; },
    subscribe(fn) { rateListeners.add(fn); return () => rateListeners.delete(fn); },
  };

  function block(seconds) {
    blockedUntil = now() + seconds * 1000;
    rateListeners.forEach((fn) => fn(blockedUntil));
  }

  function url(path, query) {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(query ?? {})) {
      if (value !== undefined && value !== null && value !== '') search.set(key, String(value));
    }
    const qs = search.toString();
    return `${base}${path}${qs ? `?${qs}` : ''}`;
  }

  async function perform(method, path, { body, query, headers = {}, auth = true, signal } = {}) {
    const controller = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; controller.abort(); }, timeoutMs);
    const onAbort = () => controller.abort();
    signal?.addEventListener('abort', onAbort);

    const init = { method, headers: { Accept: 'application/json', ...headers }, signal: controller.signal };

    if (body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }

    if (auth && tokens.accessToken) {
      init.headers.Authorization = `Bearer ${tokens.accessToken}`;
    }

    try {
      return await fetchImpl(url(path, query), init);
    } catch (cause) {
      if (signal?.aborted) throw cause;

      logger.warn?.(`[api] ${method} ${path}: ${timedOut ? 'таймаут' : 'мережева помилка'}`, cause);
      throw new ApiError({ kind: timedOut ? Kind.TIMEOUT : Kind.NETWORK });
    } finally {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
    }
  }

  async function readBody(response) {
    if (response.status === 204) return null;
    const type = response.headers?.get?.('Content-Type') ?? '';
    if (!type.includes('json')) return null;
    try { return await response.json(); } catch { return null; }
  }

  function refreshOnce() {
    if (!tokens.refreshToken) return Promise.resolve(false);

    refreshing ??= (async () => {
      try {
        const response = await perform('POST', '/api/v1/auth/refresh', { body: { refreshToken: tokens.refreshToken }, auth: false });
        if (!response.ok) return false;
        const data = await readBody(response);
        if (!data?.accessToken) return false;
        tokens.set(data);
        return true;
      } catch {
        return false;
      } finally {
        refreshing = null;
      }
    })();

    return refreshing;
  }

  async function send(method, path, options = {}) {

    if (rateLimit.remainingSeconds > 0) {
      throw new ApiError({ kind: Kind.RATE_LIMITED, status: 429, retryAfterSeconds: rateLimit.remainingSeconds });
    }

    let response = await perform(method, path, options);

    if (response.status === 401 && options.auth !== false) {
      if (await refreshOnce()) {
        response = await perform(method, path, options);
      }

      if (response.status === 401) {
        tokens.clear();
        onSessionExpired();
        throw new ApiError({ kind: Kind.UNAUTHORIZED, status: 401 });
      }
    }

    if (response.ok) {
      return { data: await readBody(response), status: response.status, headers: response.headers };
    }

    const error = errorFromResponse(response.status, await readBody(response), response.headers, now());

    if (error.kind === Kind.RATE_LIMITED) block(error.retryAfterSeconds);

    logger.warn?.(`[api] ${method} ${path} → ${response.status}`, { correlationId: error.correlationId, title: error.title });
    throw error;
  }

  return {
    rateLimit,
    send,
    get: (path, options) => send('GET', path, options).then((r) => r.data),
    post: (path, body, options) => send('POST', path, { ...options, body }).then((r) => r.data),
    delete: (path, options) => send('DELETE', path, options).then((r) => r.data),
  };
}
