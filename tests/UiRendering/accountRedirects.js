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
