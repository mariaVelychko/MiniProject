// Темна тема: перемикач зберігає вибір між запусками (localStorage); без вибору — за налаштуванням системи.

const KEY = 'sportclub.theme';

export function currentTheme() {
  const saved = document.documentElement.dataset.theme;
  if (saved === 'dark' || saved === 'light') return saved;
  return globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function toggleTheme() {
  const next = currentTheme() === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem(KEY, next); } catch { /* вибір діє до перезавантаження */ }
  return next;
}
