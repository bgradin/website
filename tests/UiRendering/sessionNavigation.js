const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const source = fs.readFileSync('src/ui/public/assets/account.js', 'utf8');
const signedInSession = { user: { id: 1, firstName: 'Alex' }, isAdmin: false };
const settle = async () => {
  for (let turn = 0; turn < 10; turn++) await new Promise(setImmediate);
};

async function openPage(initialSession, { logoutFails = false, noNavigation = false } = {}) {
  const documentEvents = {};
  const windowEvents = {};
  const requests = [];
  const redirects = [];
  let session = initialSession;
  let status;
  const document = {
    documentElement: { dataset: {} },
    body: { append: element => { status = element; } },
    getElementById: id => id === 'status' ? status : null,
    querySelector: selector => selector === '[data-session-visibility]' && !noNavigation ? {} : null,
    createElement: () => ({ dataset: {}, setAttribute() {} }),
    addEventListener: (name, handler) => { documentEvents[name] = handler; },
  };
  const context = {
    document,
    window: { addEventListener: (name, handler) => { windowEvents[name] = handler; } },
    location: { assign: path => redirects.push(path) },
    fetch: async (path, options) => {
      requests.push({ path, options });
      if (path.endsWith('/session')) {
        return { ok: Boolean(session), status: session ? 200 : 401, json: async () => session };
      }
      assert.equal(path, '/api/v1/site/logout');
      return { ok: !logoutFails, status: logoutFails ? 503 : 204, json: async () => ({}) };
    },
  };
  vm.runInNewContext(source, context);
  await settle();
  return {
    document, requests, redirects,
    status: () => status,
    restore: async nextSession => {
      session = nextSession;
      windowEvents.pageshow({ persisted: true });
      await settle();
    },
    logout: async () => {
      let prevented = false;
      const link = { setAttribute() {}, removeAttribute() {} };
      await documentEvents.click({
        target: { closest: () => link },
        preventDefault: () => { prevented = true; },
      });
      assert.equal(prevented, true);
    },
  };
}

(async () => {
  const anonymous = await openPage(null);
  assert.equal(anonymous.document.documentElement.dataset.signedIn, 'false');
  assert.equal(anonymous.status(), undefined, 'an anonymous session is not an error');

  const authenticated = await openPage(signedInSession);
  assert.equal(authenticated.document.documentElement.dataset.signedIn, 'true');
  await authenticated.logout();
  const logoutRequest = authenticated.requests.find(request => request.path.endsWith('/logout'));
  assert.equal(logoutRequest.options.method, 'POST');
  assert.equal(logoutRequest.options.headers['X-Site-Action'], '1');
  assert.equal(logoutRequest.options.credentials, 'same-origin');
  assert.equal(authenticated.document.documentElement.dataset.signedIn, 'false');
  assert.deepEqual(authenticated.redirects, ['/login/']);

  const failedLogout = await openPage(signedInSession, { logoutFails: true });
  await failedLogout.logout();
  assert.equal(failedLogout.document.documentElement.dataset.signedIn, 'true');
  assert.equal(failedLogout.status().dataset.error, 'true');
  assert.deepEqual(failedLogout.redirects, []);

  await anonymous.restore(signedInSession);
  assert.equal(anonymous.document.documentElement.dataset.signedIn, 'true');
  await anonymous.restore(null);
  assert.equal(anonymous.document.documentElement.dataset.signedIn, 'false');

  const unrelatedPage = await openPage(null, { noNavigation: true });
  assert.equal(unrelatedPage.requests.length, 0);
  console.log('PASS session navigation: anonymous and signed-in states, secure logout, failures, restored pages, unrelated pages');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
