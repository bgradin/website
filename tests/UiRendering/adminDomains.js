const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function element() {
  return {
    children: [], dataset: {}, value: '',
    append(...children) { this.children.push(...children); },
    replaceChildren() { this.children = []; this.textContent = ''; },
    setAttribute() {},
    addEventListener(name, handler) { this[name] = handler; },
  };
}

(async () => {
  const elements = Object.fromEntries(['users', 'session-name', 'redirect-domains', 'redirect-domain-form',
    'redirect-domain', 'user-count', 'page-number', 'previous', 'next', 'clear-cache', 'status'].map(id => [id, element()]));
  const button = element();
  elements['redirect-domain-form'].querySelector = () => button;
  elements['redirect-domain-form'].reset = () => { elements['redirect-domain'].value = ''; };
  const requests = [];
  let domains = ['dnd.gradinware.com'];
  let fail = false;
  const context = {
    document: {
      documentElement: { dataset: {} },
      getElementById: id => elements[id], querySelector: () => null,
      createElement: element, addEventListener() {},
    },
    window: { addEventListener() {} },
    fetch: async (path, options) => {
      requests.push({ path, options });
      let body;
      if (path === '/api/v1/site/session') body = { isAdmin: true, user: { firstName: 'Site', lastName: 'Admin' } };
      else if (path.startsWith('/api/v1/admin/users')) body = { users: [], total: 0 };
      else {
        assert.equal(path, '/api/v1/admin/redirect-domains');
        if (options.method) {
          if (fail) return { ok: false, status: 400, json: async () => ({ error: 'Invalid domain' }) };
          const domain = JSON.parse(options.body).domain;
          domains = options.method === 'POST' ? [...new Set([...domains, domain])] : domains.filter(value => value !== domain);
        }
        body = { domains };
      }
      return { ok: true, json: async () => body };
    },
  };
  vm.runInNewContext(fs.readFileSync('src/ui/public/assets/account.js', 'utf8'), context);
  for (let turn = 0; turn < 10; turn++) await new Promise(setImmediate);
  const list = elements['redirect-domains'];
  assert.equal(list.children[0].children[0].textContent, 'dnd.gradinware.com ');
  elements['redirect-domain'].value = ' app.example.test ';
  await elements['redirect-domain-form'].submit({ preventDefault() {} });
  assert.equal(list.children.length, 2);
  assert.equal(elements['redirect-domain'].value, '');
  await list.children[1].children[1].onclick();
  assert.equal(list.children.length, 1);
  fail = true;
  await list.children[0].children[1].onclick();
  assert.equal(list.children.length, 1);
  assert.equal(list.children[0].children[1].disabled, false);
  assert.equal(elements.status.textContent, 'Invalid domain');
  fail = false;
  await list.children[0].children[1].onclick();
  assert.equal(list.textContent, 'No external domains are allowed.');
  for (const { options } of requests.filter(request => request.options.method)) {
    assert.equal(options.headers['X-Site-Action'], '1');
    assert.equal(options.credentials, 'same-origin');
  }
  console.log('PASS admin domain list, add/remove controls, empty state, failures, and authenticated requests');
})().catch(error => { console.error(error); process.exitCode = 1; });
