// Тести клієнтського шару (ЛР4, плюшка): HTTP підміняється, мережа не потрібна.
// Запуск: npm test  (або: node --test)
import test from 'node:test';
import assert from 'node:assert/strict';

import { createApiClient } from '../js/api/http.js';
import { createTokenStore } from '../js/api/tokens.js';
import { ApiError, Kind, normalizeFieldErrors, parseRetryAfter, userMessage } from '../js/api/errors.js';
import { createAttempt } from '../js/idempotency.js';

const json = (status, body, headers = {}) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });

function setup(handler, { onSessionExpired = () => {}, timeoutMs = 200 } = {}) {
  const calls = [];
  const storage = new Map();
  const tokens = createTokenStore({
    getItem: (k) => storage.get(k) ?? null,
    setItem: (k, v) => storage.set(k, v),
    removeItem: (k) => storage.delete(k),
  });
  tokens.set({ accessToken: 'old-access', refreshToken: 'refresh-1', expiresAt: 'x' });

  let time = 1_000_000;
  const fetchImpl = async (url, init) => {
    calls.push({ url, init });
    return handler(url, init, calls.length);
  };

  const api = createApiClient({
    baseUrl: 'http://gateway.test',
    tokens, fetchImpl, timeoutMs, onSessionExpired,
    now: () => time,
    logger: { warn() {} },
  });

  return { api, calls, tokens, advance: (ms) => { time += ms; } };
}

// ---------- 1. 401: одна спроба оновити токен ----------

test('401: токен оновлюється ОДИН раз і запит повторюється з новим токеном', async () => {
  const { api, calls, tokens } = setup((url, init) => {
    if (url.endsWith('/api/v1/auth/refresh')) {
      return json(200, { accessToken: 'new-access', refreshToken: 'refresh-2', expiresAt: 'y' });
    }
    return init.headers.Authorization === 'Bearer new-access' ? json(200, { ok: true }) : json(401, { title: 'Потрібна автентифікація', status: 401 });
  });

  const data = await api.get('/api/v1/me/bookings');

  assert.deepEqual(data, { ok: true });
  assert.equal(calls.filter((c) => c.url.endsWith('/auth/refresh')).length, 1);
  assert.equal(calls.length, 3);   // запит → refresh → повтор запиту
  assert.equal(tokens.accessToken, 'new-access');
  assert.equal(tokens.refreshToken, 'refresh-2');
});

test('401: паралельні запити діляться ОДНИМ оновленням токена', async () => {
  const { api, calls } = setup((url, init) => {
    if (url.endsWith('/auth/refresh')) return json(200, { accessToken: 'new-access', refreshToken: 'r2', expiresAt: 'y' });
    return init.headers.Authorization === 'Bearer new-access' ? json(200, {}) : json(401, {});
  });

  await Promise.all([api.get('/a'), api.get('/b'), api.get('/c')]);

  assert.equal(calls.filter((c) => c.url.endsWith('/auth/refresh')).length, 1);
});

test('401: якщо оновити токен не вдалося — токени стираються, застосунок іде на екран входу', async () => {
  let expired = 0;
  const { api, calls, tokens } = setup((url) => (url.endsWith('/auth/refresh') ? json(401, {}) : json(401, {})), { onSessionExpired: () => { expired += 1; } });

  await assert.rejects(api.get('/api/v1/me/bookings'), (e) => e instanceof ApiError && e.kind === Kind.UNAUTHORIZED);

  assert.equal(expired, 1);
  assert.equal(tokens.accessToken, null);
  assert.equal(calls.filter((c) => c.url.endsWith('/auth/refresh')).length, 1);   // не більше однієї спроби
});

// ---------- 2. 429: Retry-After і пауза ----------

test('429: читається Retry-After, під час паузи запити на сервер НЕ надсилаються', async () => {
  const { api, calls, advance } = setup(() => json(429, { title: 'Забагато запитів', status: 429 }, { 'Retry-After': '30' }));

  await assert.rejects(api.get('/api/v1/catalog/items'), (e) => e.kind === Kind.RATE_LIMITED && e.retryAfterSeconds === 30);
  assert.equal(api.rateLimit.remainingSeconds, 30);

  advance(10_000);
  await assert.rejects(api.get('/api/v1/catalog/items'), (e) => e.kind === Kind.RATE_LIMITED && e.retryAfterSeconds === 20);
  assert.equal(calls.length, 1);   // другий запит до мережі не дійшов

  advance(21_000);
  assert.equal(api.rateLimit.remainingSeconds, 0);
});

test('429: після паузи запити знову йдуть', async () => {
  let first = true;
  const { api, calls, advance } = setup(() => {
    if (first) { first = false; return json(429, {}, { 'Retry-After': '5' }); }
    return json(200, { ok: 1 });
  });

  await assert.rejects(api.get('/x'));
  advance(6_000);

  assert.deepEqual(await api.get('/x'), { ok: 1 });
  assert.equal(calls.length, 2);
});

test('parseRetryAfter: секунди та HTTP-дата', () => {
  assert.equal(parseRetryAfter('42'), 42);
  assert.equal(parseRetryAfter('0'), 1);
  assert.equal(parseRetryAfter(null), null);
  const now = Date.parse('2026-11-02T10:00:00Z');
  assert.equal(parseRetryAfter('Mon, 02 Nov 2026 10:00:20 GMT', now), 20);
});

// ---------- 3. Помилки валідації ----------

test('400: помилки по полях розбираються з відповіді сервера (ключі → camelCase)', async () => {
  const { api } = setup(() => json(400, {
    title: 'Помилка валідації', status: 400,
    errors: { StartDate: ['Дата початку не може бути в минулому'], quantity: ['Кількість має бути більшою за нуль'], empty: [] },
  }));

  await assert.rejects(api.post('/api/v1/me/bookings', { workoutId: 0 }), (e) => {
    assert.equal(e.kind, Kind.VALIDATION);
    assert.deepEqual(e.fieldErrors, {
      startDate: ['Дата початку не може бути в минулому'],
      quantity: ['Кількість має бути більшою за нуль'],
    });
    return true;
  });
});

test('normalizeFieldErrors: рядок замість масиву, порожні значення відкидаються', () => {
  assert.deepEqual(normalizeFieldErrors({ Email: 'Невірний email', x: [''], y: null }), { email: ['Невірний email'] });
});

// ---------- 4. Мережеві збої та текст для користувача ----------

test('таймаут: запит не висить вічно, а закінчується ApiError(timeout)', async () => {
  const { api } = setup((url, init) => new Promise((_, reject) => init.signal.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')))), { timeoutMs: 30 });

  await assert.rejects(api.get('/slow'), (e) => e.kind === Kind.TIMEOUT && e.retryable && e.outcomeUnknown);
});

test('сервер недоступний: ApiError(network), а текст помилки не потрапляє до користувача', async () => {
  const { api } = setup(() => { throw new TypeError('Failed to fetch: ECONNREFUSED 127.0.0.1:5000'); });

  await assert.rejects(api.get('/x'), (e) => {
    assert.equal(e.kind, Kind.NETWORK);
    const text = userMessage(e);
    assert.ok(!/ECONNREFUSED|TypeError|fetch|127\.0\.0\.1/i.test(text), text);
    return true;
  });
});

test('500: користувач бачить загальний текст, а деталі сервера (stack/виняток) — ні', async () => {
  const { api } = setup(() => json(500, { title: 'Помилка', detail: 'System.NullReferenceException at Foo.Bar()', status: 500 }));

  await assert.rejects(api.get('/x'), (e) => {
    assert.equal(e.kind, Kind.SERVER);
    assert.ok(!userMessage(e).includes('NullReference'));
    return true;
  });
});

test('503 від шлюзу: показується зрозуміле українське повідомлення сервера', async () => {
  const { api } = setup(() => json(503, { title: 'Каталог недоступний', detail: 'Не вдалося отримати основну частину екрана. Спробуйте пізніше.', status: 503 }));

  await assert.rejects(api.get('/x'), (e) => e.kind === Kind.UNAVAILABLE && userMessage(e).startsWith('Не вдалося отримати'));
});

// ---------- 5. Токен додається автоматично, userId не надсилається ----------

test('Authorization додається автоматично; вхід іде без токена', async () => {
  const { api, calls } = setup(() => json(200, {}));

  await api.get('/api/v1/me/bookings');
  await api.post('/api/v1/auth/login', { email: 'a@b.c', password: 'x' }, { auth: false });

  assert.equal(calls[0].init.headers.Authorization, 'Bearer old-access');
  assert.equal(calls[1].init.headers.Authorization, undefined);
});

// ---------- 6. Idempotency-Key ----------

test('Idempotency-Key: після мережевої помилки той самий ключ, після відповіді сервера — новий', () => {
  let n = 0;
  const attempt = createAttempt(() => `key-${++n}`);

  const first = attempt.keyFor({ workoutId: 5 });
  attempt.settle({ outcomeUnknown: true });   // мережа/таймаут: невідомо, чи дійшов запит
  assert.equal(attempt.keyFor({ workoutId: 5 }), first);

  attempt.settle({ outcomeUnknown: false });  // сервер відповів визначено (409, 201…)
  assert.notEqual(attempt.keyFor({ workoutId: 5 }), first);
});

test('Idempotency-Key: інші дані форми — інша спроба — новий ключ', () => {
  let n = 0;
  const attempt = createAttempt(() => `key-${++n}`);

  const a = attempt.keyFor({ workoutId: 5 });
  const b = attempt.keyFor({ workoutId: 6 });

  assert.notEqual(a, b);
});
