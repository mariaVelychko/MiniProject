
export function createAttempt(generate = () => globalThis.crypto.randomUUID()) {
  let key = null;
  let payload = null;

  return {
    
    keyFor(data) {
      const serialized = JSON.stringify(data);
      if (key === null || serialized !== payload) {
        key = generate();
        payload = serialized;
      }
      return key;
    },

    settle({ outcomeUnknown }) {
      if (!outcomeUnknown) {
        key = null;
        payload = null;
      }
    },

    get current() { return key; },
  };
}
