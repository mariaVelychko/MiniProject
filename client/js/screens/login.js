import { h } from '../ui/dom.js';
import { Kind, userMessage } from '../api/errors.js';
import { watchRateLimit, banner } from '../ui/components.js';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Екран входу: форма, повідомлення про невірні дані, захист від подвійного натискання, пауза після 429. */
export function mountLogin({ container, services, api, navigate, notice }) {
  let busy = false;

  const email = h('input', { id: 'email', name: 'email', type: 'email', autocomplete: 'username', placeholder: 'name@example.com', required: true });
  const password = h('input', { id: 'password', name: 'password', type: 'password', autocomplete: 'current-password', required: true });
  const emailError = h('p', { class: 'field-error', id: 'email-error', role: 'alert' });
  const passwordError = h('p', { class: 'field-error', id: 'password-error', role: 'alert' });
  const message = h('div', { id: 'login-message' });
  const submit = h('button', { class: 'btn btn-primary', type: 'submit' }, 'Увійти');

  const fields = { email: [email, emailError], password: [password, passwordError] };

  const setFieldError = (name, text) => {
    const [input, out] = fields[name];
    out.textContent = text ?? '';
    input.toggleAttribute('aria-invalid', Boolean(text));
    input.setAttribute('aria-describedby', out.id);
  };

  const refresh = (left = api.rateLimit.remainingSeconds) => {
    submit.disabled = busy || left > 0;
    submit.textContent = busy ? 'Входимо…' : left > 0 ? `Спробуйте через ${left} с` : 'Увійти';
  };

  const stopWatching = watchRateLimit(api, submit, refresh);

  const form = h('form', {
    class: 'card form', novalidate: true,
    onsubmit: async (event) => {
      event.preventDefault();
      if (busy) return;   // подвійне натискання не відправляє другий запит

      setFieldError('email', null);
      setFieldError('password', null);
      message.replaceChildren();
      let valid = true;
      if (!EMAIL.test(email.value.trim())) { setFieldError('email', 'Введіть коректну електронну адресу.'); valid = false; }
      if (!password.value) { setFieldError('password', 'Введіть пароль.'); valid = false; }
      if (!valid) return;

      busy = true;
      refresh();

      try {
        await services.auth.login(email.value.trim(), password.value);
        navigate('#/');
      } catch (error) {
        busy = false;
        refresh();

        if (error.kind === Kind.UNAUTHORIZED) {
          message.replaceChildren(banner('error', 'Невірний email або пароль.'));
        } else if (error.kind === Kind.VALIDATION && Object.keys(error.fieldErrors).length) {
          for (const [name, errors] of Object.entries(error.fieldErrors)) {
            if (fields[name]) setFieldError(name, errors[0]);
          }
        } else if (error.kind === Kind.RATE_LIMITED) {
          message.replaceChildren(banner('warning', `Забагато спроб входу. Спробуйте ще раз через ${error.retryAfterSeconds} с.`));
        } else {
          message.replaceChildren(banner('error', userMessage(error)));
        }
      }
    },
  },
  h('h1', null, 'Вхід'),
  h('p', { class: 'muted' }, 'Увійдіть, щоб записатися на тренування.'),
  notice && banner('info', notice),
  message,
  h('label', { for: 'email' }, 'Електронна пошта'), email, emailError,
  h('label', { for: 'password' }, 'Пароль'), password, passwordError,
  submit);

  container.replaceChildren(h('div', { class: 'narrow' }, form));
  email.focus();

  return () => stopWatching();
}
