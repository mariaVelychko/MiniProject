import { h } from './dom.js';

export function toast(text, { kind = 'info', ms = 4000 } = {}) {
  const host = document.getElementById('toasts');
  if (!host) return;
  const el = h('div', { class: `toast toast-${kind}`, role: 'status' }, text);
  host.append(el);
  setTimeout(() => el.remove(), ms);
}
