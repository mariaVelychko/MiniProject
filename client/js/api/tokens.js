
const KEY = 'sportclub.tokens';

export function createTokenStore(storage = safeStorage()) {
  let memory = null;
  const listeners = new Set();

  const read = () => {
    if (memory) return memory;
    try {
      const raw = storage?.getItem(KEY);
      memory = raw ? JSON.parse(raw) : null;
    } catch {
      memory = null;
    }
    return memory;
  };

  const notify = () => listeners.forEach((fn) => fn(read()));

  return {
    get accessToken() { return read()?.accessToken ?? null; },
    get refreshToken() { return read()?.refreshToken ?? null; },
    get isAuthenticated() { return Boolean(read()?.accessToken); },

    set({ accessToken, refreshToken, expiresAt }) {
      memory = { accessToken, refreshToken, expiresAt };
      try { storage?.setItem(KEY, JSON.stringify(memory)); } catch { /* пам'яті достатньо */ }
      notify();
    },

    clear() {
      memory = null;
      try { storage?.removeItem(KEY); } catch { /* нічого */ }
      notify();
    },

    subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); },
  };
}

export function safeStorage() {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}
