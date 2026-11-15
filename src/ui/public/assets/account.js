(() => {
  'use strict';

  const getElement = (id) => document.getElementById(id);

  function showStatus(message, isError = false) {
    let status = getElement('status');
    if (!status) {
      status = document.createElement('p');
      status.id = 'status';
      status.setAttribute('role', 'status');
      status.setAttribute('aria-live', 'polite');
      (document.querySelector('main') || document.body).append(status);
    }
    status.textContent = message;
    status.dataset.error = String(isError);
  }

  function requestError(statusCode) {
    if (statusCode === 403) {
      return 'An administrator account is required.';
    }
    if (statusCode === 429) {
      return 'Too many attempts. Please wait a minute.';
    }
    return 'Unable to complete this request. Check your details and try again.';
  }

  async function api(path, options = {}, allowAnonymous = false) {
    const response = await fetch('/api/v1/' + path, {
      credentials: 'same-origin',
      ...options,
      headers: {
        'Content-Type': 'application/json',
        'X-Site-Action': '1',
        ...options.headers,
      },
    });
    if (allowAnonymous && response.status === 401) return null;
    const body = await response.json().catch(() => ({}));
    if (!response.ok) {
      const details = Object.values(body.errors || {}).flat().join(' ');
      const error = new Error(body.error || details || requestError(response.status));
      error.errors = body.errors;
      throw error;
    }
    return body;
  }

  function updateSessionNavigation(session) {
    document.documentElement.dataset.signedIn = String(Boolean(session));
  }

  async function refreshSessionNavigation() {
    if (!document.querySelector('[data-session-visibility]')) return;
    const session = await api('site/session', {}, true);
    updateSessionNavigation(session);
  }

  function initLogoutLinks() {
    let loggingOut = false;
    document.addEventListener('click', async (event) => {
      const link = event.target.closest('[data-site-logout]');
      if (!link) return;
      event.preventDefault();
      if (loggingOut) return;
      loggingOut = true;
      link.setAttribute('aria-busy', 'true');

      try {
        await api('site/logout', { method: 'POST' });
        updateSessionNavigation(null);
        location.assign('/login/');
      } catch (error) {
        showStatus(error.message, true);
      } finally {
        loggingOut = false;
        link.removeAttribute('aria-busy');
      }
    });

    // A restored history entry may have a different session than when it was cached.
    window.addEventListener('pageshow', (event) => {
      if (event.persisted) {
        refreshSessionNavigation().catch(error => showStatus(error.message, true));
      }
    });
  }

  function returnDestination(session, action) {
    const value = new URLSearchParams(location.search).get('returnUrl');
    if (value) {
      try {
        const url = new URL(value, location.origin);
        if (url.origin === location.origin && ['http:', 'https:'].includes(url.protocol)) {
          url.searchParams.set('action', action);
          return url.pathname + url.search + url.hash;
        }
      } catch (_) { /* Fall back when the destination is invalid. */ }
    }
    return session.isAdmin ? '/admin/' : '/account/';
  }

  function showSignedIn(session, form) {
    updateSessionNavigation(session);
    location.assign(returnDestination(session, form.id === 'register-form' ? 'register' : 'login'));
  }

  function loadCaptcha(siteKey) {
    const submitButton = getElement('register-submit');
    const verificationError = 'Verification could not load. Please try again.';

    window.siteCaptchaReady = () => {
      window.grecaptcha.render('captcha', {
        sitekey: siteKey,
        callback: () => {
          submitButton.disabled = false;
        },
        'expired-callback': () => {
          submitButton.disabled = true;
        },
        'error-callback': () => {
          submitButton.disabled = true;
          showStatus(verificationError, true);
        },
      });
    };

    const script = document.createElement('script');
    script.src = 'https://www.google.com/recaptcha/api.js?onload=siteCaptchaReady&render=explicit';
    script.async = true;
    script.defer = true;
    script.onerror = () => showStatus(verificationError, true);
    document.head.append(script);
  }

  async function initAccountForm() {
    const loginForm = getElement('login-form');
    const registrationForm = getElement('register-form');

    if (registrationForm) {
      const config = await api('site/config');
      if (!config.captchaSiteKey) {
        showStatus('Registration is temporarily unavailable.', true);
        return;
      }
      loadCaptcha(config.captchaSiteKey);
    }

    const form = loginForm || registrationForm;
    const isRegistration = form.id === 'register-form';
    const returnUrl = new URLSearchParams(location.search).get('returnUrl');
    if (returnUrl) document.querySelectorAll('a[href="/login/"], a[href="/register/"]:not([data-site-logout])').forEach(link => {
      if (!link.hasAttribute('data-site-logout')) link.href += '?returnUrl=' + encodeURIComponent(returnUrl);
    });
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const submitButton = form.querySelector('button[type="submit"]');
      submitButton.disabled = true;

      try {
        const data = Object.fromEntries(new FormData(form));
        if (isRegistration) {
          data.captchaToken = window.grecaptcha.getResponse();
          delete data['g-recaptcha-response'];
        }
        const endpoint = isRegistration ? 'users' : 'users/sessions';
        await api(endpoint, { method: 'POST', body: JSON.stringify(data) });
        const session = await api('site/session');
        showSignedIn(session, form);
      } catch (error) {
        showStatus(error.message, true);
        if (window.grecaptcha) window.grecaptcha.reset();
      } finally {
        submitButton.disabled = isRegistration && !window.grecaptcha?.getResponse();
      }
    });
  }

  // Use XMLHttpRequest so saving never navigates away from the editor.
  function saveProfile(data) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('PUT', '/api/v1/users/me');
      xhr.setRequestHeader('Content-Type', 'application/json');
      xhr.setRequestHeader('X-Site-Action', '1');
      xhr.timeout = 30000;
      xhr.onload = () => {
        let body = {};
        try { body = JSON.parse(xhr.responseText || '{}'); } catch (_) {}
        if (xhr.status >= 200 && xhr.status < 300) return resolve();
        const error = new Error(body.error || (xhr.status === 401
          ? 'Your session has expired. Sign in again before saving.'
          : Object.values(body.errors || {}).flat().join(' ') || requestError(xhr.status)));
        error.errors = body.errors;
        reject(error);
      };
      xhr.onerror = () => reject(new Error('Could not connect. Check your connection and try saving again. Your changes are still here.'));
      xhr.ontimeout = () => reject(new Error('Saving timed out. Please try again. Your changes are still here.'));
      xhr.send(JSON.stringify(data));
    });
  }

  async function initProfile() {
    const form = getElement('profile-form');
    const session = await api('site/session', {}, true);
    if (!session) {
      location.assign('/login/?returnUrl=' + encodeURIComponent(location.pathname + location.search));
      return;
    }
    let user = session.user;
    const names = ['firstName', 'lastName', 'email', 'password'];
    const summary = getElement('account-summary');
    const edit = getElement('edit-account');
    const clearErrors = () => names.forEach(name => {
      getElement(name + '-error').textContent = '';
      getElement(name).removeAttribute('aria-invalid');
    });
    const fieldError = (name, message) => {
      getElement(name + '-error').textContent = message;
      getElement(name).setAttribute('aria-invalid', 'true');
    };
    const display = () => {
      names.filter(name => name !== 'password').forEach(name => {
        getElement('summary-' + name).textContent = user[name];
      });
      getElement('password').value = '';
      form.hidden = true;
      summary.hidden = false;
    };
    edit.onclick = () => {
      clearErrors();
      names.forEach(name => { getElement(name).value = user[name] || ''; });
      summary.hidden = true;
      form.hidden = false;
      showStatus('');
      getElement('firstName').focus();
    };
    getElement('cancel-edit').onclick = () => {
      display();
      showStatus('Changes canceled.');
      edit.focus();
    };
    form.addEventListener('submit', async event => {
      event.preventDefault();
      clearErrors();
      const data = Object.fromEntries(new FormData(form));
      ['firstName', 'lastName', 'email'].forEach(name => { data[name] = data[name].trim(); });
      for (const name of ['firstName', 'lastName']) {
        if (data[name].length < 2 || data[name].length > 35) {
          fieldError(name, (name === 'firstName' ? 'First' : 'Last') + ' name must contain 2 to 35 characters.');
        }
      }
      getElement('email').value = data.email;
      if (!data.email || data.email.length > 320 || !getElement('email').validity.valid) {
        fieldError('email', 'Enter a valid email address of no more than 320 characters.');
      }
      if (data.password && data.password.length < 12) fieldError('password', 'Use at least 12 characters for your new password.');
      const invalid = names.find(name => getElement(name).getAttribute('aria-invalid') === 'true');
      if (invalid) {
        showStatus('Please correct the highlighted fields. Your changes have not been saved.', true);
        getElement(invalid).focus();
        return;
      }
      if (!data.password) delete data.password;
      const buttons = form.querySelectorAll('button');
      buttons.forEach(button => { button.disabled = true; });
      showStatus('Saving your account…');
      try {
        await saveProfile(data);
        user = { ...user, firstName: data.firstName, lastName: data.lastName, email: data.email };
        display();
        showStatus('Your account has been saved.');
        edit.focus();
      } catch (error) {
        for (const [key, messages] of Object.entries(error.errors || {})) {
          const name = names.find(name => name.toLowerCase() === key.toLowerCase());
          if (name) fieldError(name, [].concat(messages).join(' '));
        }
        showStatus(error.message, true);
      } finally {
        buttons.forEach(button => { button.disabled = false; });
      }
    });
    display();
    showStatus('');
  }

  function formatDate(value) {
    const utcValue = value.endsWith('Z') ? value : value + 'Z';
    return new Date(utcValue).toLocaleDateString();
  }

  async function initAdministration() {
    const session = await api('site/session');
    if (!session.isAdmin) throw new Error('An administrator account is required.');

    getElement('session-name').textContent =
      'Signed in as ' + session.user.firstName + ' ' + session.user.lastName;
    let page = 1;

    async function loadUsers() {
      const result = await api('admin/users?page=' + page);
      const users = getElement('users');
      users.replaceChildren();

      for (const user of result.users) {
        const row = document.createElement('tr');
        const values = [
          user.firstName + ' ' + user.lastName + ' · #' + user.id,
          user.email,
          formatDate(user.dateCreated),
          formatDate(user.dateLastLogin),
        ];
        for (const value of values) {
          const cell = document.createElement('td');
          cell.textContent = value;
          row.append(cell);
        }

        const actionCell = document.createElement('td');
        const deleteButton = document.createElement('button');
        const isCurrentUser = user.id === session.user.id;
        deleteButton.textContent = isCurrentUser ? 'Your account' : 'Delete';
        deleteButton.disabled = isCurrentUser;
        deleteButton.setAttribute('aria-label', 'Delete ' + user.email);
        deleteButton.onclick = async () => {
          const message = 'Permanently delete ' + user.email + '? This removes their account and sessions.';
          if (!confirm(message)) return;
          deleteButton.disabled = true;

          try {
            await api('admin/users/' + user.id, { method: 'DELETE' });
            if (result.users.length === 1 && page > 1) page--;
            await loadUsers();
            showStatus('User deleted.');
          } catch (error) {
            showStatus(error.message, true);
            deleteButton.disabled = false;
          }
        };
        actionCell.append(deleteButton);
        row.append(actionCell);
        users.append(row);
      }

      getElement('user-count').textContent = result.total + ' users';
      getElement('page-number').textContent = 'Page ' + page;
      getElement('previous').disabled = page === 1;
      getElement('next').disabled = page * 50 >= result.total;
      if (!result.total) showStatus('No users found.');
    }

    getElement('previous').onclick = () => {
      page--;
      loadUsers().catch(error => showStatus(error.message, true));
    };
    getElement('next').onclick = () => {
      page++;
      loadUsers().catch(error => showStatus(error.message, true));
    };

    const clearCacheButton = getElement('clear-cache');
    clearCacheButton.onclick = async () => {
      if (!confirm('Clear the full site page cache?')) return;
      clearCacheButton.disabled = true;
      try {
        const result = await api('admin/cache/clear', { method: 'POST' });
        showStatus(result.message);
      } catch (error) {
        showStatus(error.message, true);
      } finally {
        clearCacheButton.disabled = false;
      }
    };

    await loadUsers();
  }

  async function init() {
    initLogoutLinks();
    await refreshSessionNavigation().catch(error => showStatus(error.message, true));
    if (getElement('login-form') || getElement('register-form')) {
      await initAccountForm();
    }
    if (getElement('profile-form')) await initProfile();
    if (getElement('users')) {
      await initAdministration();
    }
  }

  init().catch(error => showStatus(error.message, true));
})();
