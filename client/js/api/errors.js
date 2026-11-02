
export const Kind = Object.freeze({
  NETWORK: 'network',            // немає зв'язку / сервер не відповідає на з'єднання
  TIMEOUT: 'timeout',            // клієнтський таймаут
  UNAVAILABLE: 'unavailable',    // 502 / 503 / 504 від шлюзу
  RATE_LIMITED: 'rate_limited',  // 429
  UNAUTHORIZED: 'unauthorized',  // 401
  FORBIDDEN: 'forbidden',        // 403
  NOT_FOUND: 'not_found',        // 404
  CONFLICT: 'conflict',          // 409
  VALIDATION: 'validation',      // 400 / 422 з помилками по полях
  SERVER: 'server',              // решта 5xx, нечитабельна відповідь
});

export class ApiError extends Error {
  constructor({ kind, status = 0, title = null, detail = null, fieldErrors = {}, retryAfterSeconds = null, correlationId = null, code = null }) {
    super(`${kind}${status ? ` (${status})` : ''}`);
    this.name = 'ApiError';
    this.kind = kind;
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.fieldErrors = fieldErrors;
    this.retryAfterSeconds = retryAfterSeconds;
    this.correlationId = correlationId;
    this.code = code;
  }

  /** Є сенс показати кнопку «Повторити»: проблема тимчасова. */
  get retryable() {
    return [Kind.NETWORK, Kind.TIMEOUT, Kind.UNAVAILABLE, Kind.SERVER, Kind.RATE_LIMITED].includes(this.kind);
  }

  /**
   * Після такої помилки невідомо, чи виконав сервер запит (відповідь могла загубитись). Тому спроба
   * створення повторюється з тим самим Idempotency-Key: якщо запит дійшов, дубля не буде.
   */
  get outcomeUnknown() {
    return [Kind.NETWORK, Kind.TIMEOUT, Kind.UNAVAILABLE, Kind.SERVER].includes(this.kind);
  }
}

/** Помилки по полях: ключі приводимо до camelCase ("StartDate" → "startDate"), значення — завжди масив рядків. */
export function normalizeFieldErrors(errors) {
  const result = {};
  if (!errors || typeof errors !== 'object') return result;

  for (const [key, value] of Object.entries(errors)) {
    const name = key ? key[0].toLowerCase() + key.slice(1) : key;
    const list = (Array.isArray(value) ? value : [value]).filter((v) => typeof v === 'string' && v.trim());
    if (list.length) result[name] = list;
  }

  return result;
}

/** Секунди з заголовка Retry-After: або число, або HTTP-дата. */
export function parseRetryAfter(value, now = Date.now()) {
  if (value == null || value === '') return null;
  const asNumber = Number(value);
  if (Number.isFinite(asNumber)) return Math.max(1, Math.ceil(asNumber));
  const date = Date.parse(value);
  return Number.isNaN(date) ? null : Math.max(1, Math.ceil((date - now) / 1000));
}

/** Розбір відповіді з помилкою: ProblemDetails шлюзу, {"message": "..."} сервісів або порожнє тіло. */
export function errorFromResponse(status, body, headers, now = Date.now()) {
  const problem = body && typeof body === 'object' ? body : {};
  const correlationId = problem.correlationId ?? headers?.get?.('X-Correlation-Id') ?? null;
  const fieldErrors = normalizeFieldErrors(problem.errors);
  const detail = typeof problem.detail === 'string' ? problem.detail : (typeof problem.message === 'string' ? problem.message : null);
  const base = { status, title: typeof problem.title === 'string' ? problem.title : null, detail, fieldErrors, correlationId, code: problem.code ?? null };

  if (status === 429) {
    const retry = parseRetryAfter(headers?.get?.('Retry-After'), now) ?? problem.retryAfterSeconds ?? 60;
    return new ApiError({ ...base, kind: Kind.RATE_LIMITED, retryAfterSeconds: retry });
  }
  if (status === 401) return new ApiError({ ...base, kind: Kind.UNAUTHORIZED });
  if (status === 403) return new ApiError({ ...base, kind: Kind.FORBIDDEN });
  if (status === 404) return new ApiError({ ...base, kind: Kind.NOT_FOUND });
  if (status === 409) return new ApiError({ ...base, kind: Kind.CONFLICT });
  if (status === 400 || status === 422) return new ApiError({ ...base, kind: Kind.VALIDATION });
  if (status === 502 || status === 503 || status === 504) return new ApiError({ ...base, kind: Kind.UNAVAILABLE });
  return new ApiError({ ...base, kind: Kind.SERVER });
}

const GENERIC = {
  [Kind.NETWORK]: 'Немає зв’язку із сервером. Перевірте інтернет і спробуйте ще раз.',
  [Kind.TIMEOUT]: 'Сервер відповідає надто довго. Спробуйте ще раз.',
  [Kind.UNAVAILABLE]: 'Сервіс тимчасово недоступний. Спробуйте трохи пізніше.',
  [Kind.UNAUTHORIZED]: 'Сесію завершено. Увійдіть знову.',
  [Kind.FORBIDDEN]: 'Недостатньо прав для цієї дії.',
  [Kind.NOT_FOUND]: 'Нічого не знайдено.',
  [Kind.CONFLICT]: 'Дію не вдалося виконати.',
  [Kind.VALIDATION]: 'Перевірте введені дані.',
  [Kind.SERVER]: 'Щось пішло не так. Спробуйте ще раз.',
};

/**
 * Текст для користувача. Показуємо українське повідомлення шлюзу/сервісу лише для «очікуваних» помилок
 * (4xx та 503): там воно написане для людей. Для невідомих збоїв (500) — завжди загальний текст: деталі лише в лог.
 */
export function userMessage(error) {
  if (!(error instanceof ApiError)) return GENERIC[Kind.SERVER];

  if (error.kind === Kind.RATE_LIMITED) {
    return `Забагато запитів. Можна повторити через ${error.retryAfterSeconds ?? 60} с.`;
  }

  const friendly = [Kind.FORBIDDEN, Kind.NOT_FOUND, Kind.CONFLICT, Kind.VALIDATION, Kind.UNAVAILABLE].includes(error.kind);
  return (friendly && error.detail) || GENERIC[error.kind];
}
