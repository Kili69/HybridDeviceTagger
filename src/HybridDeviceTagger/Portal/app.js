/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 * Licensed under the Apache License, Version 2.0. SPDX-License-Identifier: Apache-2.0
 *
 * HybridDeviceTagger configuration portal.
 * Signs in with the OAuth 2.0 authorization code flow with PKCE (no client secret, no external
 * libraries) and calls the management API with the resulting access token. All server data is
 * rendered through textContent so it can never be interpreted as markup.
 */
'use strict';

(() => {
  const TEXT = {
    de: {
      title: 'HybridDeviceTagger – Konfiguration', language: 'Sprache', signOut: 'Abmelden',
      tabActivity: 'Aktivitäten', tabRules: 'Regeln', tabLdap: 'LDAP', tabCertificates: 'Zertifikate',
      timeRange: 'Zeitraum', hour1: '1 Stunde', hours24: '24 Stunden', days7: '7 Tage', days30: '30 Tage',
      category: 'Kategorie', all: 'Alle', catConfiguration: 'Konfiguration', catTagging: 'Tagging',
      catPassword: 'Kennwort/LDAP', catAccess: 'Zugriff', outcome: 'Ergebnis', outSucceeded: 'Erfolgreich',
      outFailed: 'Fehlgeschlagen', outDenied: 'Verweigert', filter: 'Filter', refresh: 'Aktualisieren',
      activityDelay: 'Neue Einträge erscheinen nach wenigen Minuten (Log-Analytics-Verzögerung).',
      colTime: 'Zeit', colAction: 'Aktion', colActor: 'Ausgeführt von', colTarget: 'Ziel', colDetails: 'Details',
      ruleAdd: 'Regel hinzufügen', ruleEdit: 'Regel bearbeiten', colName: 'Name', colDescription: 'Beschreibung',
      colBaseDn: 'Base DN', colAttributes: 'Attribute', colGroups: 'Gruppen', colAdminUnits: 'Verwaltungseinheiten',
      attributeAdd: 'ExtensionAttribute hinzufügen', searchGroups: 'Sicherheitsgruppe in Entra suchen (mind. 2 Zeichen) …',
      searchUnits: 'Verwaltungseinheit in Entra suchen (mind. 2 Zeichen) …', notFound: 'nicht gefunden', none: 'keine',
      runTagging: 'Tagging jetzt ausführen', confirmTagging: 'Tagging jetzt für alle Regeln ausführen?',
      taggingQueued: 'Tagging angefordert (Aufruf {0}). Status wird abgefragt …', taggingNone: 'Noch kein Tagging-Lauf protokolliert.',
      taggingState: 'Letzter Tagging-Lauf: {0} ({1}){2}', taggingCounts: '{0} gefunden, {1} geändert, {2} unverändert, {3} ohne Entra-Gerät',
      save: 'Speichern', cancel: 'Abbrechen',
      edit: 'Bearbeiten', remove: 'Löschen', replace: 'Ersetzen', value: 'Wert',
      forest: 'Gesamtstruktur', domain: 'Forest-DNS-Name', validateTitle: 'LDAP-Zugriff prüfen',
      validateHint: 'Prüft DNS, TLS-Zertifikat und Anmeldung mit dem gespeicherten Kennwort.', validate: 'Jetzt prüfen',
      initTitle: 'Initiales Kennwort setzen', initHint: 'Nur möglich, solange noch kein Kennwort gespeichert ist.',
      password: 'Kennwort', accountTitle: 'LDAP-Benutzer ändern',
      accountHint: 'Das Kennwort des Kontos wird vor dem Speichern per LDAP geprüft. Mit demselben Namen wird nur das Kennwort ersetzt.',
      username: 'Benutzer (UPN)', distinguishedName: 'Distinguished Name (optional)',
      rotationTitle: 'Kennwortwechsel', rotate: 'Kennwortwechsel auslösen',
      certHint: 'Vertrauensanker für LDAPS/StartTLS. Nur öffentliche Zertifikate (.cer/.pem) hochladen; private Schlüssel werden abgelehnt. Das letzte Zertifikat kann nicht gelöscht werden.',
      certAdd: 'Zertifikat hinzufügen', colSubject: 'Antragsteller', colIssuer: 'Aussteller', colThumbprint: 'Fingerabdruck',
      colValidTo: 'Gültig bis', colCa: 'CA', colAddedBy: 'Hinzugefügt von', yes: 'Ja', no: 'Nein',
      signingIn: 'Anmeldung wird vorbereitet …', loading: 'Wird geladen …', noEntries: 'Keine Einträge.',
      saved: 'Gespeichert.', deniedPortal: 'Zugriff verweigert. Die Konfigurationsseite ist nur für Besitzer (Owner) und Mitwirkende (Contributor) der Function App verfügbar.',
      confirmRuleDelete: 'Regel „{0}“ wirklich löschen?', confirmCertDelete: 'Zertifikat „{0}“ wirklich löschen?',
      confirmRotate: 'Kennwort des LDAP-Benutzers jetzt wechseln?', confirmAccount: 'LDAP-Benutzer auf „{0}“ umstellen?',
      confirmDomain: 'Forest-DNS-Name auf „{0}“ ändern?',
      sourceBlob: 'Quelle: verwaltete Konfiguration', sourceAppSetting: 'Quelle: App-Einstellung',
      sourcePackage: 'Quelle: Installationspaket', sourceOs: 'Quelle: Betriebssystem-Zertifikatsspeicher',
      account: 'Benutzer: {0}', accountDn: 'DN: {0}', dnResolved: 'wird aus dem UPN ermittelt',
      validationOk: 'LDAP-Zugriff erfolgreich über {0}.', validationFailed: 'LDAP-Zugriff fehlgeschlagen: {0}',
      attempt: '{0}:{1} ({2}) – {3}', ok: 'OK', rotationQueued: 'Kennwortwechsel angefordert (Aufruf {0}). Status wird abgefragt …',
      rotationState: 'Letzter Lauf: {0} ({1}){2}', rotationNone: 'Noch kein Kennwortwechsel protokolliert.', lastChange: 'letzte Änderung {0}',
      activityUnavailable: 'Das Aktivitätsprotokoll ist nicht konfiguriert.',
      attributeRequired: 'Mindestens ein Attribut mit Wert ist erforderlich.', changedConcurrently: 'Die Daten wurden zwischenzeitlich geändert. Bitte neu laden.',
      tabSchedule: 'Zeitpläne', taggingInterval: 'Tagging (LDAP-Abfrage) alle … Minuten',
      taggingIntervalHint: '5 bis 1440 Minuten. Der Zeitgeber prüft alle 5 Minuten, ob das Intervall abgelaufen ist.',
      rotationInterval: 'Kennwortwechsel alle … Tage',
      rotationIntervalHint: '1 bis {0} Tage. Das Kennwortalter wird stündlich geprüft; ein manuell ausgelöster Wechsel setzt das Alter zurück.',
      confirmSchedule: 'Tagging alle {0} Minuten und Kennwortwechsel alle {1} Tage speichern?',
      protectedBy: 'Geschützt durch Microsoft Entra ID', author: 'Autor: Andreas Lucas (Kili)', activityUpdated: 'Stand: {0}.',
      deferredMinimumAge: 'Kennwortwechsel noch nicht möglich: Die Kennwortrichtlinie (minimales Kennwortalter) erlaubt ihn frühestens ab {0}',
      outcomeNotDue: 'noch nicht fällig', outcomeRotated: 'Kennwort gewechselt', outcomeRecoveredFromNext: 'aus vorbereitetem Kennwort wiederhergestellt',
      outcomeDeferredNoSecureConnection: 'zurückgestellt, keine sichere LDAP-Verbindung',
      stateQueued: 'In Warteschlange', stateRunning: 'Läuft', stateSucceeded: 'Erfolgreich', stateFailed: 'Fehlgeschlagen'
    },
    en: {
      title: 'HybridDeviceTagger – Configuration', language: 'Language', signOut: 'Sign out',
      tabActivity: 'Activity', tabRules: 'Rules', tabLdap: 'LDAP', tabCertificates: 'Certificates',
      timeRange: 'Time range', hour1: '1 hour', hours24: '24 hours', days7: '7 days', days30: '30 days',
      category: 'Category', all: 'All', catConfiguration: 'Configuration', catTagging: 'Tagging',
      catPassword: 'Password/LDAP', catAccess: 'Access', outcome: 'Outcome', outSucceeded: 'Succeeded',
      outFailed: 'Failed', outDenied: 'Denied', filter: 'Filter', refresh: 'Refresh',
      activityDelay: 'New entries appear after a few minutes (Log Analytics ingestion delay).',
      colTime: 'Time', colAction: 'Action', colActor: 'Performed by', colTarget: 'Target', colDetails: 'Details',
      ruleAdd: 'Add rule', ruleEdit: 'Edit rule', colName: 'Name', colDescription: 'Description',
      colBaseDn: 'Base DN', colAttributes: 'Attributes', colGroups: 'Groups', colAdminUnits: 'Administrative units',
      attributeAdd: 'Add ExtensionAttribute', searchGroups: 'Search Entra security groups (at least 2 characters) …',
      searchUnits: 'Search Entra administrative units (at least 2 characters) …', notFound: 'not found', none: 'none',
      runTagging: 'Run tagging now', confirmTagging: 'Run tagging for all rules now?',
      taggingQueued: 'Tagging requested (invocation {0}). Polling status …', taggingNone: 'No tagging run recorded yet.',
      taggingState: 'Last tagging run: {0} ({1}){2}', taggingCounts: '{0} found, {1} changed, {2} unchanged, {3} without Entra device',
      save: 'Save', cancel: 'Cancel',
      edit: 'Edit', remove: 'Delete', replace: 'Replace', value: 'Value',
      forest: 'Forest', domain: 'Forest DNS name', validateTitle: 'Validate LDAP access',
      validateHint: 'Checks DNS, the TLS certificate, and sign-in with the stored password.', validate: 'Validate now',
      initTitle: 'Set initial password', initHint: 'Only possible while no password is stored yet.',
      password: 'Password', accountTitle: 'Change LDAP user',
      accountHint: 'The account password is validated through LDAP before it is stored. Using the same name only replaces the password.',
      username: 'User (UPN)', distinguishedName: 'Distinguished name (optional)',
      rotationTitle: 'Password change', rotate: 'Change password now',
      certHint: 'Trust anchors for LDAPS/StartTLS. Upload public certificates (.cer/.pem) only; private keys are rejected. The last certificate cannot be deleted.',
      certAdd: 'Add certificate', colSubject: 'Subject', colIssuer: 'Issuer', colThumbprint: 'Thumbprint',
      colValidTo: 'Valid until', colCa: 'CA', colAddedBy: 'Added by', yes: 'Yes', no: 'No',
      signingIn: 'Preparing sign-in …', loading: 'Loading …', noEntries: 'No entries.',
      saved: 'Saved.', deniedPortal: 'Access denied. The configuration page is available only to Owners and Contributors of the Function App.',
      confirmRuleDelete: 'Delete rule "{0}"?', confirmCertDelete: 'Delete certificate "{0}"?',
      confirmRotate: 'Change the LDAP user password now?', confirmAccount: 'Switch the LDAP user to "{0}"?',
      confirmDomain: 'Change the forest DNS name to "{0}"?',
      sourceBlob: 'Source: managed configuration', sourceAppSetting: 'Source: app setting',
      sourcePackage: 'Source: installation package', sourceOs: 'Source: operating-system certificate store',
      account: 'User: {0}', accountDn: 'DN: {0}', dnResolved: 'resolved from the UPN',
      validationOk: 'LDAP access succeeded through {0}.', validationFailed: 'LDAP access failed: {0}',
      attempt: '{0}:{1} ({2}) – {3}', ok: 'OK', rotationQueued: 'Password change requested (invocation {0}). Polling status …',
      rotationState: 'Last run: {0} ({1}){2}', rotationNone: 'No password change recorded yet.', lastChange: 'last change {0}',
      activityUnavailable: 'The activity log is not configured.',
      attributeRequired: 'At least one attribute with a value is required.', changedConcurrently: 'The data changed in the meantime. Please reload.',
      tabSchedule: 'Schedules', taggingInterval: 'Tagging (LDAP query) every … minutes',
      taggingIntervalHint: '5 to 1440 minutes. The timer checks every 5 minutes whether the interval has elapsed.',
      rotationInterval: 'Password change every … days',
      rotationIntervalHint: '1 to {0} days. The password age is checked hourly; a manually triggered change resets the age.',
      confirmSchedule: 'Save tagging every {0} minutes and password change every {1} days?',
      protectedBy: 'Protected by Microsoft Entra ID', author: 'Author: Andreas Lucas (Kili)', activityUpdated: 'Updated {0}.',
      deferredMinimumAge: 'Password change not possible yet: the password policy (minimum password age) allows it from {0}',
      outcomeNotDue: 'not due', outcomeRotated: 'password changed', outcomeRecoveredFromNext: 'recovered from the staged password',
      outcomeDeferredNoSecureConnection: 'deferred, no secure LDAP connection',
      stateQueued: 'Queued', stateRunning: 'Running', stateSucceeded: 'Succeeded', stateFailed: 'Failed'
    }
  };

  const STORAGE = { token: 'hdt.token', verifier: 'hdt.pkce', state: 'hdt.state', language: 'hdt.language' };
  const ATTRIBUTES = Array.from({ length: 15 }, (_, index) => `extensionAttribute${index + 1}`);
  const $ = (id) => document.getElementById(id);

  let config;
  let language = localStorage.getItem(STORAGE.language) || (navigator.language || 'de').slice(0, 2);
  if (!TEXT[language]) { language = 'en'; }
  const state = {
    rules: [], rulesETag: null, editingRule: null, selectedRule: null, directory: new Map(),
    certificates: [], certETag: null, domainETag: null, activity: []
  };

  const t = (key, ...args) => (TEXT[language][key] || TEXT.en[key] || key)
    .replace(/\{(\d)\}/g, (_, index) => String(args[Number(index)] ?? ''));

  function applyLanguage() {
    document.documentElement.lang = language;
    $('language').value = language;
    document.querySelectorAll('[data-i18n]').forEach((element) => { element.textContent = t(element.dataset.i18n); });
    document.querySelectorAll('[data-i18n-placeholder]').forEach((element) => { element.placeholder = t(element.dataset.i18nPlaceholder); });
    document.title = t('title');
  }

  function showStatus(message, kind) {
    const banner = $('status');
    banner.textContent = message;
    banner.className = `banner ${kind || ''}`;
    banner.hidden = !message;
  }

  function cell(text, className) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    if (className) { td.className = className; }
    return td;
  }

  function button(label, className, handler) {
    const element = document.createElement('button');
    element.type = 'button';
    element.textContent = label;
    if (className) { element.className = className; }
    element.addEventListener('click', handler);
    return element;
  }

  function formatTime(value) {
    return value ? new Date(value).toLocaleString(language === 'de' ? 'de-DE' : 'en-GB') : '';
  }

  async function busy(element, action) {
    element.disabled = true;
    try { await action(); } catch (error) { reportError(error); } finally { element.disabled = false; }
  }

  function reportError(error) {
    if (error && error.status === 409 && /changed concurrently/i.test(error.message)) {
      showStatus(t('changedConcurrently'), 'error');
      return;
    }
    let message = error && error.message ? error.message : String(error);
    if (error && error.data && Array.isArray(error.data.errors)) { message += `\n${error.data.errors.join('\n')}`; }
    if (error && error.data && Array.isArray(error.data.attempts)) {
      message += `\n${error.data.attempts.map((item) => t('attempt', item.server, item.port, item.transport, item.error || t('ok'))).join('\n')}`;
    }
    showStatus(message, 'error');
  }

  // ---------- Sign-in (authorization code flow with PKCE) ----------

  const base64Url = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes)))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const randomString = () => base64Url(crypto.getRandomValues(new Uint8Array(32)));
  const redirectUri = () => `${location.origin}/api/portal`;
  const authority = () => `${config.authorityHost}/${config.tenantId}/oauth2/v2.0`;

  async function signIn() {
    const verifier = randomString();
    const stateValue = randomString();
    sessionStorage.setItem(STORAGE.verifier, verifier);
    sessionStorage.setItem(STORAGE.state, stateValue);
    const challenge = base64Url(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier)));
    const query = new URLSearchParams({
      client_id: config.clientId,
      response_type: 'code',
      redirect_uri: redirectUri(),
      response_mode: 'query',
      scope: config.scope,
      state: stateValue,
      code_challenge: challenge,
      code_challenge_method: 'S256'
    });
    location.assign(`${authority()}/authorize?${query}`);
  }

  async function completeSignIn(params) {
    const expectedState = sessionStorage.getItem(STORAGE.state);
    const verifier = sessionStorage.getItem(STORAGE.verifier);
    sessionStorage.removeItem(STORAGE.state);
    sessionStorage.removeItem(STORAGE.verifier);
    history.replaceState(null, '', '/api/portal');
    if (!expectedState || params.get('state') !== expectedState || !verifier) {
      throw new Error('The sign-in response does not match this browser session. Reload the page.');
    }
    const response = await fetch(`${authority()}/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        client_id: config.clientId,
        grant_type: 'authorization_code',
        code: params.get('code'),
        redirect_uri: redirectUri(),
        code_verifier: verifier,
        scope: config.scope
      })
    });
    const data = await response.json();
    if (!response.ok) { throw new Error(data.error_description || data.error || 'Token request failed.'); }
    sessionStorage.setItem(STORAGE.token, JSON.stringify({
      accessToken: data.access_token,
      expiresAt: Date.now() + (Number(data.expires_in) - 120) * 1000
    }));
  }

  function currentToken() {
    try {
      const token = JSON.parse(sessionStorage.getItem(STORAGE.token) || 'null');
      return token && token.expiresAt > Date.now() ? token.accessToken : null;
    } catch { return null; }
  }

  function signOut() {
    sessionStorage.clear();
    location.assign(`${authority()}/logout?${new URLSearchParams({ post_logout_redirect_uri: redirectUri() })}`);
  }

  // ---------- API ----------

  async function api(method, path, body, etag) {
    const token = currentToken();
    if (!token) { await signIn(); throw new Error(t('signingIn')); }
    const headers = { Authorization: `Bearer ${token}` };
    if (body !== undefined) { headers['Content-Type'] = 'application/json'; }
    if (etag) { headers['If-Match'] = etag; }
    const response = await fetch(`/api/management/${path}`, {
      method, headers, body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store'
    });
    if (response.status === 401) {
      sessionStorage.removeItem(STORAGE.token);
      await signIn();
      throw new Error(t('signingIn'));
    }
    const text = await response.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = { error: text }; }
    if (!response.ok) {
      const error = new Error((data && data.error) || `HTTP ${response.status}`);
      error.status = response.status;
      error.data = data;
      throw error;
    }
    return { data, etag: response.headers.get('ETag') };
  }

  // ---------- Activity ----------

  async function loadActivity() {
    showStatus('');
    const query = new URLSearchParams({ hours: $('activityHours').value, take: '500' });
    if ($('activityCategory').value) { query.append('category', $('activityCategory').value); }
    if ($('activityOutcome').value) { query.append('outcome', $('activityOutcome').value); }
    try {
      const { data } = await api('GET', `portal/activity?${query}`);
      state.activity = data.records || [];
    } catch (error) {
      state.activity = [];
      if (error.status === 503) { showStatus(t('activityUnavailable'), 'error'); } else { throw error; }
    }
    renderActivity();
    $('activityUpdated').textContent = t('activityUpdated', new Date().toLocaleTimeString(language === 'de' ? 'de-DE' : 'en-GB'));
  }

  function renderActivity() {
    const filter = $('activityFilter').value.trim().toLowerCase();
    const rows = $('activityRows');
    rows.replaceChildren();
    const records = state.activity.filter((record) => !filter ||
      [record.action, record.actor, record.target, record.details].some((value) => (value || '').toLowerCase().includes(filter)));
    if (records.length === 0) {
      const tr = document.createElement('tr');
      const td = cell(t('noEntries'));
      td.colSpan = 7;
      tr.append(td);
      rows.append(tr);
      return;
    }
    for (const record of records) {
      const tr = document.createElement('tr');
      tr.append(
        cell(formatTime(record.timestampUtc), 'nowrap'),
        cell(t(`cat${record.category}`)),
        cell(record.action),
        cell(t(`out${record.outcome}`), `outcome-${record.outcome}`),
        cell(record.actor, 'wrap'),
        cell(record.target, 'wrap'),
        cell(record.details, 'wrap'));
      rows.append(tr);
    }
  }

  // ---------- Rules ----------

  async function loadRules() {
    const { data, etag } = await api('GET', 'tagging-rules');
    state.rules = data.rules || [];
    state.rulesETag = etag || data.eTag;
    $('rulesSource').textContent = data.source === 'blob' ? t('sourceBlob') : t('sourceAppSetting');
    renderRules();
    const selected = state.rules.find((rule) => rule.name === state.selectedRule);
    if (selected) { await showRule(selected); } else { state.selectedRule = null; $('ruleDetail').hidden = true; }
    await loadTaggingStatus();
  }

  function renderRules() {
    const rows = $('ruleRows');
    rows.replaceChildren();
    if (state.rules.length === 0) {
      const tr = document.createElement('tr');
      const td = cell(t('noEntries'));
      td.colSpan = 3;
      tr.append(td);
      rows.append(tr);
      return;
    }
    for (const rule of state.rules) {
      const tr = document.createElement('tr');
      tr.tabIndex = 0;
      tr.classList.toggle('selected', rule.name === state.selectedRule);
      tr.append(
        cell(rule.name),
        cell(rule.baseDn, 'mono'),
        cell(Object.keys(rule.extensionAttributes || {}).join(', ')));
      const select = () => showRule(rule).catch(reportError);
      tr.addEventListener('click', select);
      tr.addEventListener('keydown', (event) => { if (event.key === 'Enter') { select(); } });
      rows.append(tr);
    }
  }

  async function resolveNames(groupIds, unitIds) {
    const missing = (ids) => ids.filter((id) => !state.directory.has(id.toLowerCase()));
    const groups = missing(groupIds);
    const units = missing(unitIds);
    if (groups.length + units.length === 0) { return; }
    const { data } = await api('POST', 'portal/directory/resolve', { groupIds: groups, administrativeUnitIds: units });
    for (const item of data || []) { state.directory.set(item.id.toLowerCase(), item); }
  }

  function directoryLabel(id) {
    const item = state.directory.get(id.toLowerCase());
    return item ? `${item.displayName} (${id})` : `${id} – ${t('notFound')}`;
  }

  function fillList(id, values) {
    const list = $(id);
    list.replaceChildren();
    if (values.length === 0) {
      const item = document.createElement('li');
      item.className = 'hint';
      item.textContent = t('none');
      list.append(item);
    }
    for (const value of values) {
      const item = document.createElement('li');
      item.textContent = value;
      list.append(item);
    }
  }

  async function showRule(rule) {
    state.selectedRule = rule.name;
    renderRules();
    $('ruleForm').hidden = true;
    $('ruleDetailName').textContent = rule.name;
    $('ruleDetailDescription').textContent = rule.description;
    $('ruleDetailBaseDn').textContent = rule.baseDn;
    fillList('ruleDetailAttributes', Object.entries(rule.extensionAttributes || {}).map(([name, value]) => `${name} = ${value}`));
    const groups = rule.entraGroupIds || [];
    const units = rule.administrativeUnitIds || [];
    fillList('ruleDetailGroups', groups);
    fillList('ruleDetailUnits', units);
    $('ruleDetail').hidden = false;
    await resolveNames(groups, units);
    if (state.selectedRule === rule.name) {
      fillList('ruleDetailGroups', groups.map(directoryLabel));
      fillList('ruleDetailUnits', units.map(directoryLabel));
    }
  }

  function selectedRule() { return state.rules.find((rule) => rule.name === state.selectedRule); }

  // ExtensionAttribute rows: every attribute may be used only once per rule.
  function usedAttributes(exceptSelect) {
    return Array.from($('ruleAttributes').querySelectorAll('select'))
      .filter((select) => select !== exceptSelect)
      .map((select) => select.value);
  }

  function refreshAttributeOptions() {
    for (const select of $('ruleAttributes').querySelectorAll('select')) {
      const used = usedAttributes(select);
      const current = select.value;
      select.replaceChildren();
      for (const attribute of ATTRIBUTES.filter((name) => name === current || !used.includes(name))) {
        const option = document.createElement('option');
        option.value = attribute;
        option.textContent = attribute;
        select.append(option);
      }
      select.value = current;
    }
    $('ruleAttributeAdd').disabled = $('ruleAttributes').children.length >= ATTRIBUTES.length;
  }

  function addAttributeRow(name, value) {
    const free = ATTRIBUTES.find((attribute) => !usedAttributes(null).includes(attribute));
    if (!name && !free) { return; }
    const row = document.createElement('div');
    row.className = 'attribute-row';
    const select = document.createElement('select');
    const option = document.createElement('option');
    option.value = name || free;
    select.append(option);
    select.value = name || free;
    select.addEventListener('change', refreshAttributeOptions);
    const input = document.createElement('input');
    input.maxLength = 1024;
    input.placeholder = t('value');
    input.value = value || '';
    row.append(select, input, button('×', 'danger', () => { row.remove(); refreshAttributeOptions(); }));
    $('ruleAttributes').append(row);
    refreshAttributeOptions();
  }

  // Group and administrative-unit pickers backed by an Entra search.
  const pickers = {
    groups: { kind: 'groups', selected: 'ruleGroupsSelected', search: 'ruleGroupsSearch', results: 'ruleGroupsResults', ids: [] },
    units: { kind: 'administrative-units', selected: 'ruleUnitsSelected', search: 'ruleUnitsSearch', results: 'ruleUnitsResults', ids: [] }
  };

  function renderPicker(picker) {
    const list = $(picker.selected);
    list.replaceChildren();
    for (const id of picker.ids) {
      const item = document.createElement('li');
      const label = document.createElement('span');
      label.textContent = directoryLabel(id);
      item.append(label, button('×', 'danger', () => {
        picker.ids = picker.ids.filter((value) => value !== id);
        renderPicker(picker);
      }));
      list.append(item);
    }
  }

  async function searchPicker(picker) {
    const text = $(picker.search).value.trim();
    const results = $(picker.results);
    if (text.length < 2) { results.replaceChildren(); return; }
    const { data } = await api('GET', `portal/directory/${picker.kind}?${new URLSearchParams({ search: text })}`);
    if ($(picker.search).value.trim() !== text) { return; }
    results.replaceChildren();
    if (!data || data.length === 0) {
      const item = document.createElement('li');
      item.className = 'hint';
      item.textContent = t('noEntries');
      results.append(item);
      return;
    }
    for (const entry of data) {
      state.directory.set(entry.id.toLowerCase(), entry);
      const item = document.createElement('li');
      const choose = button(entry.displayName, 'link', () => {
        if (!picker.ids.some((id) => id.toLowerCase() === entry.id.toLowerCase())) { picker.ids.push(entry.id); }
        $(picker.search).value = '';
        results.replaceChildren();
        renderPicker(picker);
      });
      choose.disabled = !entry.assignable || picker.ids.some((id) => id.toLowerCase() === entry.id.toLowerCase());
      const meta = document.createElement('span');
      meta.className = 'hint';
      meta.textContent = [entry.description, entry.note].filter(Boolean).join(' · ');
      item.append(choose, document.createTextNode(' '), meta);
      results.append(item);
    }
  }

  function wirePicker(picker) {
    let timer;
    $(picker.search).addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(() => searchPicker(picker).catch(reportError), 300);
    });
  }

  async function openRuleForm(rule) {
    state.editingRule = rule ? rule.name : null;
    $('ruleDetail').hidden = true;
    $('ruleFormTitle').textContent = rule ? t('ruleEdit') : t('ruleAdd');
    $('ruleName').value = rule ? rule.name : '';
    $('ruleName').readOnly = Boolean(rule);
    $('ruleDescription').value = rule ? rule.description : '';
    $('ruleBaseDn').value = rule ? rule.baseDn : '';
    $('ruleAttributes').replaceChildren();
    const entries = rule ? Object.entries(rule.extensionAttributes || {}) : [];
    if (entries.length === 0) { addAttributeRow(); } else { entries.forEach(([name, value]) => addAttributeRow(name, value)); }
    pickers.groups.ids = rule ? [...(rule.entraGroupIds || [])] : [];
    pickers.units.ids = rule ? [...(rule.administrativeUnitIds || [])] : [];
    for (const picker of Object.values(pickers)) {
      $(picker.search).value = '';
      $(picker.results).replaceChildren();
    }
    $('ruleForm').hidden = false;
    $('ruleName').focus();
    await resolveNames(pickers.groups.ids, pickers.units.ids);
    renderPicker(pickers.groups);
    renderPicker(pickers.units);
  }

  async function saveRule(event) {
    event.preventDefault();
    const extensionAttributes = {};
    for (const row of $('ruleAttributes').querySelectorAll('.attribute-row')) {
      const [select, input] = row.querySelectorAll('select, input');
      if (input.value.trim()) { extensionAttributes[select.value] = input.value.trim(); }
    }
    if (Object.keys(extensionAttributes).length === 0) { throw new Error(t('attributeRequired')); }
    const rule = {
      name: $('ruleName').value.trim(),
      description: $('ruleDescription').value.trim(),
      baseDn: $('ruleBaseDn').value.trim(),
      extensionAttributes,
      entraGroupIds: pickers.groups.ids,
      administrativeUnitIds: pickers.units.ids
    };
    await api('PUT', `tagging-rules/${encodeURIComponent(rule.name)}`, rule, state.rulesETag);
    $('ruleForm').hidden = true;
    state.selectedRule = rule.name;
    showStatus(t('saved'), 'success');
    await loadRules();
  }

  async function deleteRule(name) {
    if (!confirm(t('confirmRuleDelete', name))) { return; }
    await api('DELETE', `tagging-rules/${encodeURIComponent(name)}`, undefined, state.rulesETag);
    state.selectedRule = null;
    showStatus(t('saved'), 'success');
    await loadRules();
  }

  // Immediate tagging run.
  function describeTaggingStatus(status) {
    const result = status.result || {};
    const counts = status.state === 'Succeeded'
      ? ` – ${t('taggingCounts', result.discovered ?? 0, result.updated ?? 0, result.unchanged ?? 0, result.notFound ?? 0)}`
      : (status.errorMessage ? ` – ${status.errorMessage}` : '');
    return t('taggingState', t(`state${status.state}`), formatTime(status.completedAtUtc || status.startedAtUtc), counts);
  }

  async function loadTaggingStatus() {
    try {
      const { data } = await api('GET', 'status/device-tagging');
      $('taggingStatus').textContent = describeTaggingStatus(data);
      return data;
    } catch (error) {
      if (error.status === 404) { $('taggingStatus').textContent = t('taggingNone'); return null; }
      throw error;
    }
  }

  async function runTagging() {
    if (!confirm(t('confirmTagging'))) { return; }
    const { data } = await api('POST', 'actions/device-tagging');
    showStatus(t('taggingQueued', data.invocationId), 'success');
    for (let attempt = 0; attempt < 120; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 5000));
      const status = await loadTaggingStatus();
      if (status && status.invocationId === data.invocationId && ['Succeeded', 'Failed'].includes(status.state)) {
        showStatus($('taggingStatus').textContent, status.state === 'Succeeded' ? 'success' : 'error');
        return;
      }
    }
  }
  // ---------- LDAP ----------

  async function loadLdap() {
    const { data, etag } = await api('GET', 'portal/active-directory');
    state.domainETag = etag || data.eTag;
    $('domain').value = data.domain;
    $('accountUsername').value = data.username || '';
    $('accountUsername').dataset.loaded = data.username || '';
    $('accountDn').value = data.distinguishedName || '';
    $('accountInfo').textContent = `${t('account', data.username)} · ${t('accountDn', data.distinguishedName || t('dnResolved'))} · ${data.accountSource === 'blob' ? t('sourceBlob') : t('sourceAppSetting')}`;
    await loadRotationStatus();
  }

  async function loadRotationStatus() {
    try {
      const { data } = await api('GET', 'status/password-rotation');
      const result = data.result || {};
      const outcome = result.outcome === 'DeferredMinimumPasswordAge'
        ? ` – ${t('deferredMinimumAge', formatTime(result.earliestChangeUtc))}`
        : (result.outcome ? ` – ${t(`outcome${result.outcome}`)}` : '');
      const detail = data.errorMessage ? ` – ${data.errorMessage}` : outcome;
      const lastChange = data.lastPasswordChangeAtUtc ? ` · ${t('lastChange', formatTime(data.lastPasswordChangeAtUtc))}` : '';
      $('rotationStatus').textContent = t('rotationState', t(`state${data.state}`), formatTime(data.completedAtUtc || data.startedAtUtc), detail) + lastChange;
      return data;
    } catch (error) {
      if (error.status === 404) { $('rotationStatus').textContent = t('rotationNone'); return null; }
      throw error;
    }
  }

  async function saveDomain(event) {
    event.preventDefault();
    const domain = $('domain').value.trim();
    if (!confirm(t('confirmDomain', domain))) { return; }
    await api('PUT', 'active-directory', { domain }, state.domainETag);
    showStatus(t('saved'), 'success');
    await loadLdap();
  }

  function renderValidation(result) {
    const container = $('validateResult');
    container.replaceChildren();
    const summary = document.createElement('p');
    summary.className = `result outcome-${result.succeeded ? 'Succeeded' : 'Failed'}`;
    const last = result.attempts && result.attempts.length ? result.attempts[result.attempts.length - 1] : null;
    summary.textContent = result.succeeded
      ? t('validationOk', last ? `${last.server}:${last.port} (${last.transport})` : '')
      : t('validationFailed', result.error || '');
    const list = document.createElement('ul');
    for (const attempt of result.attempts || []) {
      const item = document.createElement('li');
      item.className = 'mono';
      item.textContent = t('attempt', attempt.server, attempt.port, attempt.transport, attempt.succeeded ? t('ok') : attempt.error);
      list.append(item);
    }
    container.append(summary, list);
  }

  async function validate() {
    $('validateResult').textContent = t('loading');
    try {
      const { data } = await api('POST', 'portal/active-directory/validate');
      renderValidation(data);
    } catch (error) {
      $('validateResult').replaceChildren();
      throw error;
    }
  }

  async function initializePassword(event) {
    event.preventDefault();
    const input = $('initPassword');
    try {
      await api('POST', 'actions/initialize-ad-credential', { password: input.value });
    } finally {
      input.value = '';
    }
    showStatus(t('saved'), 'success');
  }

  async function changeAccount(event) {
    event.preventDefault();
    const username = $('accountUsername').value.trim();
    if (!confirm(t('confirmAccount', username))) { $('accountPassword').value = ''; return; }
    try {
      await api('PUT', 'portal/active-directory/account', {
        username,
        password: $('accountPassword').value,
        distinguishedName: $('accountDn').value.trim()
      });
    } finally {
      $('accountPassword').value = '';
    }
    showStatus(t('saved'), 'success');
    await loadLdap();
  }

  async function rotate() {
    if (!confirm(t('confirmRotate'))) { return; }
    const { data } = await api('POST', 'actions/password-rotation');
    showStatus(t('rotationQueued', data.invocationId), 'success');
    for (let attempt = 0; attempt < 30; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 5000));
      const status = await loadRotationStatus();
      if (status && status.invocationId === data.invocationId && ['Succeeded', 'Failed'].includes(status.state)) {
        const deferred = status.result && String(status.result.outcome || '').startsWith('Deferred');
        showStatus($('rotationStatus').textContent, status.state === 'Succeeded' && !deferred ? 'success' : 'error');
        return;
      }
    }
  }

  // ---------- Certificates ----------

  async function loadCertificates() {
    const { data, etag } = await api('GET', 'portal/certificates');
    state.certificates = data.certificates || [];
    state.certETag = etag || data.eTag;
    $('certSource').textContent = { blob: t('sourceBlob'), package: t('sourcePackage') }[data.source] || t('sourceOs');
    const rows = $('certRows');
    rows.replaceChildren();
    for (const certificate of state.certificates) {
      const tr = document.createElement('tr');
      const actions = document.createElement('td');
      actions.className = 'nowrap';
      const replaceInput = document.createElement('input');
      replaceInput.type = 'file';
      replaceInput.accept = '.cer,.crt,.pem,.der';
      replaceInput.hidden = true;
      replaceInput.addEventListener('change', () => busy(replaceInput, async () => {
        const body = { certificate: await readCertificate(replaceInput.files[0]) };
        replaceInput.value = '';
        await api('PUT', `portal/certificates/${encodeURIComponent(certificate.thumbprint)}`, body, state.certETag);
        showStatus(t('saved'), 'success');
        await loadCertificates();
      }));
      actions.append(
        replaceInput,
        button(t('replace'), 'secondary', () => replaceInput.click()),
        document.createTextNode(' '),
        button(t('remove'), 'danger', (event) => busy(event.currentTarget, async () => {
          if (!confirm(t('confirmCertDelete', certificate.subject))) { return; }
          await api('DELETE', `portal/certificates/${encodeURIComponent(certificate.thumbprint)}`, undefined, state.certETag);
          showStatus(t('saved'), 'success');
          await loadCertificates();
        })));
      tr.append(
        cell(certificate.subject, 'wrap'),
        cell(certificate.issuer, 'wrap'),
        cell(certificate.thumbprint, 'mono'),
        cell(formatTime(certificate.notAfterUtc), 'nowrap'),
        cell(certificate.isCertificateAuthority ? t('yes') : t('no')),
        cell(`${certificate.addedBy}${certificate.addedUtc ? ` · ${formatTime(certificate.addedUtc)}` : ''}`, 'wrap'),
        actions);
      rows.append(tr);
    }
  }

  async function readCertificate(file) {
    const bytes = new Uint8Array(await file.arrayBuffer());
    const text = new TextDecoder('ascii').decode(bytes);
    if (text.includes('-----BEGIN')) { return text; }
    let binary = '';
    for (const value of bytes) { binary += String.fromCharCode(value); }
    return btoa(binary);
  }

  async function addCertificate() {
    const input = $('certAdd');
    if (!input.files.length) { return; }
    const certificate = await readCertificate(input.files[0]);
    input.value = '';
    await api('POST', 'portal/certificates', { certificate }, state.certETag);
    showStatus(t('saved'), 'success');
    await loadCertificates();
  }

  // ---------- Navigation ----------

  // ---------- Schedules ----------

  async function loadSchedule() {
    const { data, etag } = await api('GET', 'portal/schedule');
    state.scheduleETag = etag || data.eTag;
    $('taggingInterval').value = data.taggingIntervalMinutes;
    $('rotationInterval').value = data.passwordRotationDays;
    $('rotationInterval').max = data.maximumPasswordRotationDays;
    $('rotationIntervalHint').textContent = t('rotationIntervalHint', data.maximumPasswordRotationDays);
    $('scheduleSource').textContent = data.source === 'blob' ? t('sourceBlob') : t('sourceAppSetting');
  }

  async function saveSchedule(event) {
    event.preventDefault();
    const taggingIntervalMinutes = Number($('taggingInterval').value);
    const passwordRotationDays = Number($('rotationInterval').value);
    if (!confirm(t('confirmSchedule', taggingIntervalMinutes, passwordRotationDays))) { return; }
    await api('PUT', 'portal/schedule', { taggingIntervalMinutes, passwordRotationDays }, state.scheduleETag);
    showStatus(t('saved'), 'success');
    await loadSchedule();
  }

  const loaders = { activity: loadActivity, rules: loadRules, ldap: loadLdap, certificates: loadCertificates, schedule: loadSchedule };
  let currentTab = 'activity';

  async function openTab(name) {
    currentTab = name;
    document.querySelectorAll('.tabs button').forEach((tab) => tab.classList.toggle('active', tab.dataset.tab === name));
    document.querySelectorAll('.tab').forEach((section) => { section.hidden = section.id !== `tab-${name}`; });
    showStatus('');
    try { await loaders[name](); } catch (error) { reportError(error); }
  }

  function wireEvents() {
    $('language').addEventListener('change', () => {
      language = $('language').value;
      localStorage.setItem(STORAGE.language, language);
      applyLanguage();
      if (!$('app').hidden) {
        $('validateResult').replaceChildren();
        loaders[currentTab]().catch(reportError);
      }
    });
    $('signOut').addEventListener('click', signOut);
    document.querySelectorAll('.tabs button').forEach((tab) => tab.addEventListener('click', () => openTab(tab.dataset.tab)));
    $('activityRefresh').addEventListener('click', (event) => busy(event.currentTarget, loadActivity));
    ['activityHours', 'activityCategory', 'activityOutcome'].forEach((id) =>
      $(id).addEventListener('change', () => loadActivity().catch(reportError)));
    $('activityFilter').addEventListener('input', renderActivity);
    $('ruleAdd').addEventListener('click', () => openRuleForm(null).catch(reportError));
    $('rulesRefresh').addEventListener('click', (event) => busy(event.currentTarget, loadRules));
    $('ruleAttributeAdd').addEventListener('click', () => addAttributeRow());
    $('ruleCancel').addEventListener('click', () => {
      $('ruleForm').hidden = true;
      const rule = selectedRule();
      if (rule) { showRule(rule).catch(reportError); }
    });
    $('ruleEdit').addEventListener('click', () => { const rule = selectedRule(); if (rule) { openRuleForm(rule).catch(reportError); } });
    $('ruleDelete').addEventListener('click', (event) => busy(event.currentTarget, async () => {
      const rule = selectedRule();
      if (rule) { await deleteRule(rule.name); }
    }));
    $('runTagging').addEventListener('click', (event) => busy(event.currentTarget, runTagging));
    Object.values(pickers).forEach(wirePicker);
    $('ruleForm').addEventListener('submit', (event) => busy(event.submitter || $('ruleForm'), () => saveRule(event)));
    $('domainForm').addEventListener('submit', (event) => busy(event.submitter || $('domainForm'), () => saveDomain(event)));
    $('validate').addEventListener('click', (event) => busy(event.currentTarget, validate));
    $('initForm').addEventListener('submit', (event) => busy(event.submitter || $('initForm'), () => initializePassword(event)));
    $('accountForm').addEventListener('submit', (event) => busy(event.submitter || $('accountForm'), () => changeAccount(event)));
    // The DN of the previous account must never be submitted with a different user name.
    $('accountUsername').addEventListener('input', () => {
      if ($('accountUsername').value.trim() !== $('accountUsername').dataset.loaded) { $('accountDn').value = ''; }
    });
    $('rotate').addEventListener('click', (event) => busy(event.currentTarget, rotate));
    $('certAdd').addEventListener('change', (event) => busy(event.currentTarget, addCertificate));
    $('certRefresh').addEventListener('click', (event) => busy(event.currentTarget, loadCertificates));
    $('scheduleForm').addEventListener('submit', (event) => busy(event.submitter || $('scheduleForm'), () => saveSchedule(event)));
  }

  async function start() {
    applyLanguage();
    wireEvents();
    showStatus(t('signingIn'));
    const response = await fetch('/api/portal/config.json', { cache: 'no-store' });
    config = await response.json();
    if (!response.ok) { throw new Error(config.error || `HTTP ${response.status}`); }
    $('version').textContent = config.version ? `· v${config.version}${config.branch ? ` (${config.branch})` : ''}` : '';

    const params = new URLSearchParams(location.search);
    if (params.get('error')) {
      history.replaceState(null, '', '/api/portal');
      throw new Error(params.get('error_description') || params.get('error'));
    }
    if (params.get('code')) { await completeSignIn(params); }
    if (!currentToken()) { await signIn(); return; }

    try {
      const { data } = await api('GET', 'portal/session');
      $('user').textContent = data.displayName || data.user;
    } catch (error) {
      if (error.status === 403) { showStatus(t('deniedPortal'), 'error'); $('signOut').hidden = false; return; }
      throw error;
    }
    $('signOut').hidden = false;
    $('app').hidden = false;
    await openTab('activity');
    // The activity log receives new entries with a delay, so it refreshes itself while visible.
    setInterval(() => {
      if (currentTab === 'activity' && !document.hidden && !$('activityRefresh').disabled) {
        loadActivity().catch(reportError);
      }
    }, 60000);
  }

  document.addEventListener('DOMContentLoaded', () => start().catch(reportError));
})();
