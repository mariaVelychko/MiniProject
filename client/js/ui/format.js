
const dateTime = new Intl.DateTimeFormat('uk-UA', { weekday: 'short', day: '2-digit', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' });
const time = new Intl.DateTimeFormat('uk-UA', { hour: '2-digit', minute: '2-digit' });
const money = new Intl.NumberFormat('uk-UA', { style: 'currency', currency: 'UAH', maximumFractionDigits: 0 });

/** Час із сервера — без часового поясу ("2026-12-01T18:00:00"), тому показуємо його як є. */
export const formatDateTime = (iso) => { const d = new Date(iso); return Number.isNaN(d.getTime()) ? '—' : dateTime.format(d); };
export const formatClock = (date) => time.format(date);
export const formatPrice = (value) => (typeof value === 'number' ? money.format(value) : '—');
export const formatMinutes = (value) => `${value} хв`;
