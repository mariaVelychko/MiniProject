
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
