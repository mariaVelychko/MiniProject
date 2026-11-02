// Idempotency-Key для створення операції (ЛР4, 5.5): ключ генерує КЛІЄНТ, а не сервер.
// Один ключ = одна спроба користувача. Якщо відповідь загубилась (мережа, таймаут, 503), ми не знаємо, чи виконав
// сервер запит, тому повторюємо з ТИМ САМИМ ключем: сервер або виконає запит уперше, або поверне вже збережений
// результат — дубля не буде. Якщо користувач змінив дані форми — це інша спроба, ключ новий.

export function createAttempt(generate = () => globalThis.crypto.randomUUID()) {
  let key = null;
  let payload = null;

  return {
    /** Ключ для відправки цих даних. */
    keyFor(data) {
      const serialized = JSON.stringify(data);
      if (key === null || serialized !== payload) {
        key = generate();
        payload = serialized;
      }
      return key;
    },

    /** Результат спроби: невідомий (мережа) — ключ лишається; будь-який визначений (успіх, 409, 404…) — спробу закрито. */
    settle({ outcomeUnknown }) {
      if (!outcomeUnknown) {
        key = null;
        payload = null;
      }
    },

    get current() { return key; },
  };
}
