const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('src/ui/public/assets/account.js', 'utf8')
  .replace('init().catch(error => showStatus(error.message, true));', 'globalThis.destination = returnDestination;');
const context = { URL, URLSearchParams, document: {}, location: { origin: 'https://example.test', search: '' } };
vm.runInNewContext(source, context);
for (const isAdmin of [false, true]) {
  const session = { isAdmin };
  const fallback = isAdmin ? '/admin/' : '/account/';
  for (const value of ['', 'https://other.test/', '//other.test/', 'javascript:alert(1)', '/\\other.test/']) {
    context.location.search = '?returnUrl=' + encodeURIComponent(value);
    assert.equal(context.destination(session, 'login'), fallback);
  }
  for (const action of ['login', 'register']) {
    for (const [value, expected] of [
      ['/projects/?page=2#details', '/projects/?page=2&action=' + action + '#details'],
      ['https://example.test/projects/', '/projects/?action=' + action],
      ['/projects/?action=old&action=duplicate', '/projects/?action=' + action],
    ]) {
      context.location.search = '?returnUrl=' + encodeURIComponent(value);
      assert.equal(context.destination(session, action), expected);
    }
  }
}
console.log('PASS account and admin defaults, local return URLs with login/register actions, and external redirect rejection');

for (const action of ['login', 'register']) {
  for (const [origin, domains, value, expected] of [
    ['http://localhost:5000', ['localhost'], 'http://localhost:3000/', 'http://localhost:3000/?action=' + action],
    ['http://localhost:5000', ['localhost'], 'http://localhost:8123/?x=1#here', 'http://localhost:8123/?x=1&action=' + action + '#here'],
    ['https://gradinware.com', ['dnd.gradinware.com'], 'https://dnd.gradinware.com/', 'https://dnd.gradinware.com/?action=' + action],
    ['https://gradinware.com', [], 'https://dnd.gradinware.com/', '/account/'],
    ['https://gradinware.com', ['dnd.gradinware.com'], 'http://dnd.gradinware.com/', '/account/'],
    ['https://gradinware.com', ['dnd.gradinware.com'], 'https://dnd.gradinware.com.evil.test/', '/account/'],
    ['http://localhost:5000', ['localhost'], 'http://localhost.evil.test:3000/', '/account/'],
    ['https://gradinware.com', ['dnd.gradinware.com'], 'https://user:pass@dnd.gradinware.com/', '/account/'],
  ]) {
    context.location.origin = origin;
    context.location.search = '?returnUrl=' + encodeURIComponent(value);
    assert.equal(context.destination({ isAdmin: false }, action, domains), expected);
  }
}
console.log('PASS configured domain redirects for login/register, any localhost port, and untrusted URL rejection');

async function checkForm(action) {
  const requests = [];
  const redirects = [];
  let submit;
  const button = {};
  const form = {
    id: action + '-form',
    querySelector: () => button,
    addEventListener: (event, handler) => { submit = handler; },
  };
  const link = { href: action === 'login' ? '/register/' : '/login/', hasAttribute: () => false };
  const sandbox = {
    URL, URLSearchParams,
    FormData: class { *[Symbol.iterator]() { yield ['email', 'user@example.test']; yield ['password', 'test password']; } },
    document: {
      documentElement: { dataset: {} },
      getElementById: id => id === form.id ? form : id === 'register-submit' ? button : null,
      querySelector: () => null,
      querySelectorAll: () => [link],
      addEventListener() {},
      createElement: () => ({}),
      head: { append() {} },
    },
    window: { addEventListener() {}, grecaptcha: { getResponse: () => 'verified-token' } },
    location: {
      origin: 'http://localhost:5000', search: '?returnUrl=http%3A%2F%2Flocalhost%3A3000%2F',
      assign: destination => redirects.push(destination),
    },
    fetch: async (path, options) => {
      requests.push({ path, options });
      const body = path.endsWith('/config')
        ? { captchaSiteKey: 'test-key', accountRedirectDomains: ['localhost'] }
        : { isAdmin: false };
      return { ok: true, json: async () => body };
    },
  };
  vm.runInNewContext(fs.readFileSync('src/ui/public/assets/account.js', 'utf8'), sandbox);
  for (let turn = 0; turn < 10; turn++) await new Promise(setImmediate);
  assert.equal(typeof submit, 'function', action + ' form initializes');
  assert.ok(link.href.endsWith('?returnUrl=http%3A%2F%2Flocalhost%3A3000%2F'));
  await submit({ preventDefault() {} });
  assert.deepEqual(redirects, ['http://localhost:3000/?action=' + action]);
  const request = requests.find(request => request.options.method === 'POST');
  assert.equal(request.path, action === 'login' ? '/api/v1/users/sessions' : '/api/v1/users');
  if (action === 'register') assert.equal(JSON.parse(request.options.body).captchaToken, 'verified-token');
}
Promise.all(['login', 'register'].map(checkForm)).then(() => {
  console.log('PASS login and registration submissions redirect using the configured list and preserve return URLs between forms');
}).catch(error => { console.error(error); process.exitCode = 1; });
