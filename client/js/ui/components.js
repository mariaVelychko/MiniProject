import { h } from './dom.js';
import { userMessage, Kind } from '../api/errors.js';

/** Скелетони замість спінера: видно каркас майбутнього вмісту. */
export function skeletonCards(count = 6) {
  return h('div', { class: 'grid', 'aria-hidden': 'true' },
    Array.from({ length: count }, () => h('div', { class: 'card skeleton-card' },
      h('div', { class: 'skeleton line w60' }),
      h('div', { class: 'skeleton line w40' }),
      h('div', { class: 'skeleton line w80' }))));
}

export function skeletonRows(count = 3) {
  return h('div', { class: 'stack', 'aria-hidden': 'true' },
    Array.from({ length: count }, () => h('div', { class: 'card skeleton-card' },
      h('div', { class: 'skeleton line w60' }), h('div', { class: 'skeleton line w40' }))));
}

export function skeletonDetails() {
  return h('div', { class: 'stack', 'aria-hidden': 'true' },
    h('div', { class: 'card skeleton-card' },
      h('div', { class: 'skeleton line w60 tall' }), h('div', { class: 'skeleton line w80' }), h('div', { class: 'skeleton line w40' })),
    skeletonRows(2));
}

/** Порожній стан: це НЕ помилка («нічого не знайдено»), тож без червоного. */
export function emptyView({ title, text, action }) {
  return h('div', { class: 'state state-empty' },
    h('div', { class: 'state-icon', 'aria-hidden': 'true' }, '🗂️'),
    h('h2', null, title),
    text && h('p', { class: 'muted' }, text),
    action);
}

/**
 * Спостерігає за паузою після 429: щосекунди (поки елемент у документі) викликає onTick(залишилось секунд).
 * Повертає функцію зупинки. Сам зупиняється, коли елемент зникає зі сторінки.
 */
export function watchRateLimit(api, element, onTick) {
  let seen = false;
  const tick = () => {
    if (element.isConnected) seen = true;
    else if (seen) { stop(); return; }
    onTick(api.rateLimit.remainingSeconds);
  };
  const timer = setInterval(tick, 500);
  const unsubscribe = api.rateLimit.subscribe(tick);   // миттєва реакція на новий 429, не чекаючи таймера
  const stop = () => { clearInterval(timer); unsubscribe(); };
  onTick(api.rateLimit.remainingSeconds);
  return stop;
}

/** Кнопка «Повторити»: під час паузи після 429 вимкнена й показує, скільки лишилося; потім сама оживає. */
export function retryButton({ api, onClick, label = 'Повторити' }) {
  const button = h('button', { class: 'btn', type: 'button', onclick: onClick }, label);

  watchRateLimit(api, button, (left) => {
    button.disabled = left > 0;
    button.textContent = left > 0 ? `${label} (через ${left} с)` : label;
  });

  return button;
}

/** Стан помилки: зрозумілий текст (без винятків і стека), код запиту для підтримки, кнопка «Повторити». */
export function errorView({ error, api, onRetry, title }) {
  const limited = error.kind === Kind.RATE_LIMITED;

  return h('div', { class: 'state state-error', role: 'alert' },
    h('div', { class: 'state-icon', 'aria-hidden': 'true' }, limited ? '⏳' : '⚠️'),
    h('h2', null, title ?? (limited ? 'Забагато запитів' : 'Не вдалося завантажити')),
    h('p', null, userMessage(error)),
    error.correlationId && h('p', { class: 'muted small' }, `Код запиту: ${error.correlationId}`),
    error.retryable && onRetry ? retryButton({ api, onClick: onRetry }) : null);
}

export function banner(kind, ...content) {
  return h('div', { class: `banner banner-${kind}`, role: kind === 'error' ? 'alert' : 'status' }, ...content);
}
