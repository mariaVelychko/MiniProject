import { createTokenStore } from './api/tokens.js';
import { createApiClient } from './api/http.js';
import { createServices } from './api/services.js';
import { state } from './state.js';
import { h } from './ui/dom.js';
import { toast } from './ui/toast.js';
import { toggleTheme, currentTheme } from './ui/theme.js';
import { watchRateLimit } from './ui/components.js';
import { mountLogin } from './screens/login.js';
import { mountCatalog } from './screens/catalog.js';
import { mountDetails } from './screens/details.js';
import { mountBook } from './screens/book.js';
import { mountBookings } from './screens/bookings.js';

const config = globalThis.APP_CONFIG ?? {};
const tokens = createTokenStore();

let sessionNotice = null;

const api = createApiClient({
  baseUrl: config.apiBaseUrl,
  tokens,
  timeoutMs: config.timeoutMs ?? 8000,

  onSessionExpired: () => { sessionNotice = 'Сесію завершено. Увійдіть знову.'; state.screens.clear(); state.userName = null; navigate('#/login'); },
});

const services = createServices(api, tokens);

const view = document.getElementById('view');
const topbar = document.getElementById('topbar');
const globalBanner = document.getElementById('global-banner');

const routes = [
  { pattern: /^#\/login$/, mount: mountLogin, public: true },
  { pattern: /^#\/item\/(?<id>[^/]+)$/, mount: mountDetails },
  { pattern: /^#\/book\/(?<id>[^/]+)(?:\/(?<session>[^/]+))?$/, mount: mountBook },
  { pattern: /^#\/my$/, mount: mountBookings },
  { pattern: /^(?:#\/?)?$/, mount: mountCatalog },
];

let dispose = null;
let rendered = null;

export function navigate(hash) {
  if ((location.hash || '#/') !== hash) history.pushState(null, '', hash);
  render();
}

function render() {
  dispose?.();
  dispose = null;

  const hash = location.hash || '#/';
  rendered = hash;
  const route = routes.find((r) => r.pattern.test(hash));

  if (!route) { navigate('#/'); return; }
  if (!route.public && !tokens.isAuthenticated) { navigate('#/login'); return; }
  if (route.public && tokens.isAuthenticated) { navigate('#/'); return; }

  renderTopbar(hash);

  const notice = sessionNotice;
  sessionNotice = null;

  const params = hash.match(route.pattern)?.groups ?? {};
  dispose = route.mount({ container: view, params, services, api, navigate, notice }) ?? null;
  view.focus({ preventScroll: true });
  document.title = 'SportClub — запис на тренування';
}

function renderTopbar(hash) {
  if (!tokens.isAuthenticated) { topbar.hidden = true; return; }

  const link = (href, text, active) => h('a', { href, class: `nav-link${active ? ' active' : ''}`, 'aria-current': active ? 'page' : null }, text);

  topbar.hidden = false;
  topbar.replaceChildren(
    h('a', { class: 'brand', href: '#/' }, '🏋️ SportClub'),
    h('nav', { class: 'nav' },
      link('#/', 'Заняття', /^(#\/?$|#\/item|#\/book)/.test(hash)),
      link('#/my', 'Мої записи', hash === '#/my')),
    h('div', { class: 'topbar-actions' },
      state.userName && h('span', { class: 'muted small user' }, state.userName),
      h('button', {
        class: 'btn btn-icon', type: 'button', id: 'theme-toggle',
        'aria-label': 'Перемкнути тему', title: 'Світла / темна тема',
        onclick: (event) => { event.currentTarget.textContent = toggleTheme() === 'dark' ? '☀️' : '🌙'; },
      }, currentTheme() === 'dark' ? '☀️' : '🌙'),
      h('button', {
        class: 'btn', type: 'button', id: 'logout',
        onclick: async () => { await services.auth.logout(); state.screens.clear(); state.userName = null; toast('Ви вийшли з акаунта.'); navigate('#/login'); },
      }, 'Вийти')));
}

watchRateLimit(api, globalBanner, (left) => {
  globalBanner.hidden = left <= 0;
  if (left > 0) globalBanner.textContent = `Забагато запитів. Нові запити можна буде надіслати через ${left} с.`;
});

addEventListener('hashchange', () => { if ((location.hash || '#/') !== rendered) render(); });
render();
