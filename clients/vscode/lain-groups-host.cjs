'use strict';

// Presentation metadata only. The caller owns native selection and stale UI responses.
// context: {request(method, params), vscode, cwd, signal?, timeoutMs?}.
// params: {hostId, modelProvider, modelCatalogPath, models: complete native records}.
// No configuration, paths, credentials, or raw model records cross this boundary.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const {createHash} = require('node:crypto');
const {TextDecoder} = require('node:util');

const MAX_BYTES = 16 * 1024 * 1024;
const MAX_MODELS = 10000;
const PAGE_SIZE = 100;
const MAX_PAGES = 100;
const TIMEOUT_MS = 10000;
const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const hash = value => createHash('sha256').update(value).digest('hex');
const stable = value => JSON.stringify(value, (_, item) => object(item)
  ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);

class Fallback extends Error {
  constructor(reason) { super(reason); this.reason = reason; }
}
const fail = reason => { throw new Fallback(reason); };
const native = reason => ({status: 'native', reason, generation: null, groups: []});
function protocol() {
  const error = new Error('Invalid native model groups response');
  error.code = 'LAIN_GROUPS_NATIVE_PROTOCOL';
  throw error;
}

function absolute(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 4096 &&
    !value.includes('\0') && path.isAbsolute(value) && path.resolve(value) === value;
}

function environment(context) {
  const {vscode} = context;
  if (!absolute(context.cwd) || !vscode?.env || !vscode.workspace ||
      vscode.env.remoteName != null) fail('unsupported-host');
  const folders = vscode.workspace.workspaceFolders ?? [];
  if (!Array.isArray(folders) || folders.length > 128) fail('unsupported-host');
  const directories = [];
  const scopes = [undefined];
  for (const folder of folders) {
    if (folder?.uri?.scheme !== 'file' || !absolute(folder.uri.fsPath)) fail('unsupported-host');
    directories.push(folder.uri.fsPath);
    scopes.push(folder.uri);
  }
  for (const scope of scopes) {
    const value = vscode.workspace.getConfiguration('chatgpt', scope).get('cliExecutable');
    if (value != null && (typeof value !== 'string' || value.trim() !== '')) fail('custom-cli');
  }
  return {cwd: context.cwd, directories};
}

function modelSnapshot(models, invalid) {
  if (!Array.isArray(models) || models.length > MAX_MODELS) return invalid();
  const ids = new Set(), slugs = new Set();
  return stable(models.map(model => {
    if (!object(model) || typeof model.id !== 'string' || !model.id ||
        typeof model.model !== 'string' || !model.model ||
        typeof model.displayName !== 'string' || typeof model.hidden !== 'boolean' ||
        typeof model.isDefault !== 'boolean' ||
        !(model.defaultReasoningEffort === null || typeof model.defaultReasoningEffort === 'string') ||
        !Array.isArray(model.supportedReasoningEfforts) ||
        model.supportedReasoningEfforts.some(effort => !object(effort) ||
          typeof effort.reasoningEffort !== 'string' || typeof effort.description !== 'string') ||
        ids.has(model.id) || slugs.has(model.model)) return invalid();
    ids.add(model.id);
    slugs.add(model.model);
    return {id: model.id, model: model.model, displayName: model.displayName,
      hidden: model.hidden, isDefault: model.isDefault,
      defaultReasoningEffort: model.defaultReasoningEffort,
      supportedReasoningEfforts: model.supportedReasoningEfforts};
  }));
}

function binding(response) {
  if (!object(response?.config) || !Array.isArray(response.layers)) protocol();
  const config = response.config;
  const users = response.layers.filter(layer => layer?.name?.type === 'user' && layer.disabledReason == null);
  const profiles = users.map(layer => layer.name.profile ?? null);
  if (profiles.some(profile => profile !== null && profile !== 'lain') ||
      new Set(profiles).size > 1 ||
      (config.profile != null && (config.profile !== 'lain' || !profiles.includes('lain')))) {
    fail('unsupported-profile');
  }
  const profile = profiles.includes('lain') ? 'lain' : null;
  const provider = config.model_providers?.lain;
  if (config.model_provider !== 'lain' || !absolute(config.model_catalog_json) ||
      !object(provider) || typeof provider.base_url !== 'string' || !provider.base_url.trim() ||
      typeof provider.wire_api !== 'string' || !provider.wire_api.trim()) fail('context-changed');
  // Compare the entire active provider internally, including URL/wire/auth overrides.
  return {provider: config.model_provider, catalog: config.model_catalog_json, profile, definition: provider};
}

async function configs(context, state, check) {
  const params = [{includeLayers: true}, ...[...new Set([state.cwd, ...state.directories])]
    .map(cwd => ({includeLayers: true, cwd}))];
  const responses = [];
  for (const param of params) {
    check();
    // Native failures deliberately propagate unchanged.
    responses.push(await context.request('config/read', param));
    check();
  }
  return responses;
}

async function models(context, check) {
  const records = [], cursors = new Set();
  let cursor = null;
  for (let page = 0; page < MAX_PAGES; page++) {
    check();
    const result = await context.request('model/list', {includeHidden: true, limit: PAGE_SIZE, cursor});
    check();
    if (!object(result) || !Array.isArray(result.data) || result.data.length > PAGE_SIZE ||
        records.length + result.data.length > MAX_MODELS || !own(result, 'nextCursor')) protocol();
    records.push(...result.data);
    if (result.nextCursor === null) return records;
    if (typeof result.nextCursor !== 'string' || !result.nextCursor || result.nextCursor.length > 4096 ||
        cursors.has(result.nextCursor) || result.data.length === 0) protocol();
    cursors.add(result.nextCursor);
    cursor = result.nextCursor;
  }
  protocol();
}

function identity(stat) {
  return [stat.dev, stat.ino, stat.mode, stat.uid, stat.gid, stat.size, stat.mtimeNs, stat.ctimeNs].join(':');
}

const directoryIdentity = stat => [stat.dev, stat.ino, stat.mode, stat.uid, stat.gid].join(':');

async function pathState(filename, check) {
  if (!absolute(filename) || typeof process.getuid !== 'function') fail('unsafe-catalog');
  const uid = BigInt(process.getuid()), home = path.resolve(os.homedir());
  // An unprivileged Linux user namespace can map the system root owner to
  // nobody. Anchor protected HOME ancestors to the actual filesystem root
  // owner; the catalog itself must still be owned by the current user.
  const systemOwner = (await fs.promises.lstat(path.parse(filename).root, {bigint: true})).uid;
  const pieces = filename.slice(path.parse(filename).root.length).split(path.sep);
  const state = [];
  let current = path.parse(filename).root;
  for (let index = -1; index < pieces.length; index++) {
    if (index >= 0) current = path.join(current, pieces[index]);
    check();
    const stat = await fs.promises.lstat(current, {bigint: true});
    check();
    const file = index === pieces.length - 1;
    const rootAncestor = stat.uid === systemOwner && (current === home || home.startsWith(current === path.sep ? current : current + path.sep));
    if (stat.isSymbolicLink() || (stat.mode & 0o022n) !== 0n ||
        (file ? !stat.isFile() || stat.uid !== uid : !stat.isDirectory() || (stat.uid !== uid && !rootAncestor))) {
      fail('unsafe-catalog');
    }
    if (file && stat.size > BigInt(MAX_BYTES)) fail('invalid-catalog');
    // Unrelated child creation must not invalidate the catalog; directory inode,
    // ownership and permissions still have to remain identical.
    state.push(file ? identity(stat) : directoryIdentity(stat));
  }
  return state;
}

async function catalogFile(filename, check) {
  let handle;
  try {
    const before = await pathState(filename, check);
    if (fs.constants.O_NOFOLLOW == null || fs.constants.O_NONBLOCK == null) fail('unsafe-catalog');
    check();
    // O_NONBLOCK also prevents a raced FIFO replacement from blocking the host.
    handle = await fs.promises.open(filename, fs.constants.O_RDONLY | fs.constants.O_NOFOLLOW | fs.constants.O_NONBLOCK);
    check();
    const stat = await handle.stat({bigint: true});
    if (!stat.isFile() || identity(stat) !== before.at(-1)) fail('catalog-changed');
    const size = Number(stat.size), bytes = Buffer.alloc(size + 1);
    let length = 0;
    while (length < bytes.length) {
      check();
      const result = await handle.read(bytes, length, bytes.length - length, length);
      check();
      if (result.bytesRead === 0) break;
      length += result.bytesRead;
    }
    const after = await handle.stat({bigint: true});
    const afterPath = await pathState(filename, check);
    if (length !== size || identity(after) !== identity(stat) || stable(before) !== stable(afterPath)) fail('catalog-changed');
    return {bytes: bytes.subarray(0, size), state: before};
  } catch (error) {
    if (error instanceof Fallback) throw error;
    fail(error?.code === 'ELOOP' ? 'unsafe-catalog' : 'catalog-unavailable');
  } finally {
    if (handle) await handle.close().catch(() => {});
  }
}

// JSON.parse alone silently accepts duplicate keys and unpaired Unicode escapes;
// serde rejects them. Walk tokens as well, with a bounded nesting depth.
function strictJson(text) {
  let position = 0;
  const whitespace = () => { while (/[\x20\t\r\n]/.test(text[position] ?? 'x')) position++; };
  function string() {
    const start = position++;
    while (position < text.length) {
      const char = text[position++];
      if (char === '\\') position++;
      else if (char === '"') {
        const value = JSON.parse(text.slice(start, position));
        if (!value.isWellFormed()) fail('invalid-catalog');
        return value;
      }
    }
    fail('invalid-catalog');
  }
  function value(depth) {
    if (depth > 64) fail('invalid-catalog');
    whitespace();
    const char = text[position];
    if (char === '"') { string(); return; }
    if (char === '{' || char === '[') {
      const keys = new Set(), end = char === '{' ? '}' : ']';
      position++;
      whitespace();
      if (text[position] === end) { position++; return; }
      while (position < text.length) {
        whitespace();
        if (char === '{') {
          if (text[position] !== '"') fail('invalid-catalog');
          const key = string();
          if (keys.has(key)) fail('invalid-catalog');
          keys.add(key);
          whitespace();
          if (text[position++] !== ':') fail('invalid-catalog');
        }
        value(depth + 1);
        whitespace();
        if (text[position] === end) { position++; return; }
        if (text[position++] !== ',') fail('invalid-catalog');
      }
      fail('invalid-catalog');
    }
    const start = position;
    while (position < text.length && !/[\x20\t\r\n,}\]]/.test(text[position])) position++;
    if (position === start) fail('invalid-catalog');
  }
  const result = JSON.parse(text);
  value(0);
  whitespace();
  if (position !== text.length) fail('invalid-catalog');
  return result;
}

function groups(bytes, nativeModels) {
  try {
    const catalog = strictJson(new TextDecoder('utf-8', {fatal: true, ignoreBOM: true}).decode(bytes));
    if (!object(catalog) || !Array.isArray(catalog.models) || catalog.models.length > MAX_MODELS) fail('invalid-catalog');
    const available = new Map(nativeModels.map(model => [model.model, model]));
    const slugs = new Set();
    for (const model of catalog.models) {
      if (!object(model) || typeof model.slug !== 'string' || !model.slug || slugs.has(model.slug)) fail('invalid-catalog');
      slugs.add(model.slug);
    }
    const picker = catalog.lain_model_picker;
    if (picker == null) fail('groups-unavailable');
    if (!object(picker) || Object.keys(picker).some(key => !['schema_version', 'groups'].includes(key)) ||
        picker.schema_version !== 1 || !Array.isArray(picker.groups) || picker.groups.length > 128) fail('invalid-catalog');
    const ids = new Set();
    const result = picker.groups.map(group => {
      if (!object(group) || Object.keys(group).some(key => !['id', 'name', 'models', 'router_model'].includes(key))) fail('invalid-catalog');
      for (const [key, maximum] of [['id', 256], ['name', 1024]]) {
        const label = group[key];
        if (typeof label !== 'string' || /^\p{White_Space}*$/u.test(label) ||
            Buffer.byteLength(label) > maximum || /\p{Cc}/u.test(label)) fail('invalid-catalog');
      }
      if (ids.has(group.id) || !Array.isArray(group.models)) fail('invalid-catalog');
      ids.add(group.id);
      const members = new Set();
      for (const slug of group.models) {
        if (typeof slug !== 'string' || !slugs.has(slug) || members.has(slug)) fail('invalid-catalog');
        members.add(slug);
      }
      if (group.router_model != null && (typeof group.router_model !== 'string' || !slugs.has(group.router_model))) fail('invalid-catalog');
      return {id: group.id, name: group.name,
        models: group.models.filter(slug => available.has(slug)),
        router_model: available.has(group.router_model) ? group.router_model : null};
    });
    // Alpha proof vscode-backend-r2: slug -> id/model, display_name ->
    // displayName, description -> description, visibility hide -> hidden;
    // explicit default_reasoning_level and supported_reasoning_levels map to
    // the corresponding native reasoning fields. Compare sets, not catalog
    // order (the native list is sorted by priority). Both lists can otherwise
    // agree on cached startup data while the file has already been replaced.
    if (slugs.size !== available.size) fail('catalog-models-changed');
    for (const model of catalog.models) {
      if (typeof model.display_name !== 'string' ||
          (model.description != null && typeof model.description !== 'string') ||
          !['list', 'hide'].includes(model.visibility) ||
          (model.default_reasoning_level != null && typeof model.default_reasoning_level !== 'string') ||
          !Array.isArray(model.supported_reasoning_levels) ||
          model.supported_reasoning_levels.some(effort => !object(effort) ||
            typeof effort.effort !== 'string' || typeof effort.description !== 'string')) fail('invalid-catalog');
      const current = available.get(model.slug);
      if (!current || current.id !== model.slug || current.displayName !== model.display_name ||
          current.description !== (model.description ?? '') || current.hidden !== (model.visibility === 'hide') ||
          // A null catalog default has no proven Alpha mapping; do not guess it.
          (model.default_reasoning_level != null && current.defaultReasoningEffort !== model.default_reasoning_level) ||
          stable(current.supportedReasoningEfforts) !== stable(model.supported_reasoning_levels.map(effort =>
            ({reasoningEffort: effort.effort, description: effort.description})))) fail('catalog-models-changed');
    }
    return result;
  } catch (error) {
    if (error instanceof Fallback) throw error;
    fail('invalid-catalog');
  }
}

async function verify(context, params, check) {
  check();
  if (!object(params) || params.hostId !== 'local') fail('unsupported-host');
  if (params.modelProvider !== 'lain' || !absolute(params.modelCatalogPath)) fail('context-changed');
  const hint = {provider: params.modelProvider, catalog: params.modelCatalogPath};
  const expectedModels = modelSnapshot(params.models, () => fail('invalid-request'));
  const state = environment(context), stateKey = stable(state);
  const before = await configs(context, state, check);
  const beforeKey = stable(before);
  const bindings = before.map(binding), bound = bindings[0], boundKey = stable(bound);
  if (bindings.some(item => stable(item) !== boundKey) ||
      bound.provider !== hint.provider || bound.catalog !== hint.catalog) fail('context-changed');
  const firstFile = await catalogFile(bound.catalog, check);
  const current = await models(context, check);
  if (modelSnapshot(current, protocol) !== expectedModels) fail('models-changed');
  const validatedGroups = groups(firstFile.bytes, current);
  check();
  const after = await configs(context, state, check);
  if (beforeKey !== stable(after)) fail('context-changed');
  const lastFile = await catalogFile(bound.catalog, check);
  if (stable(firstFile.state) !== stable(lastFile.state) || !firstFile.bytes.equals(lastFile.bytes)) fail('catalog-changed');
  try {
    if (stable(environment(context)) !== stateKey) fail('context-changed');
  } catch (error) {
    if (error instanceof Fallback) fail('context-changed');
    throw error;
  }
  check();
  return {status: 'ready', reason: 'ready',
    generation: hash(stable({binding: hash(boundKey), models: expectedModels, catalog: hash(firstFile.bytes), groups: validatedGroups})),
    groups: validatedGroups};
}

async function readGroups(context, params) {
  let timer, listener, stopped;
  const timeout = Number.isFinite(context.timeoutMs) ? Math.max(1, Math.min(TIMEOUT_MS, context.timeoutMs)) : TIMEOUT_MS;
  const deadline = Date.now() + timeout;
  const check = () => {
    if (stopped) fail(stopped);
    if (context.signal?.aborted) fail('aborted');
    if (Date.now() >= deadline) fail('timeout');
  };
  const interruption = new Promise((_, reject) => {
    const stop = reason => { stopped = reason; reject(new Fallback(reason)); };
    timer = setTimeout(() => stop('timeout'), timeout);
    if (context.signal) {
      listener = () => stop('aborted');
      context.signal.addEventListener('abort', listener, {once: true});
    }
  });
  try {
    // The bridge has no cancellation API: timeout stops further work but cannot
    // remove an in-flight native request from pendingAppServerRequests.
    return await Promise.race([verify(context, params, check), interruption]);
  } catch (error) {
    if (error instanceof Fallback) return native(error.reason);
    throw error;
  } finally {
    clearTimeout(timer);
    if (listener) context.signal.removeEventListener('abort', listener);
  }
}

module.exports = {readGroups};
