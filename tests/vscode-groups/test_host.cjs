'use strict';

const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const {readGroups} = require('../../clients/vscode/lain-groups-host.cjs');
const copy = value => JSON.parse(JSON.stringify(value));

function record(slug, extra = {}) {
  return {id: slug, model: slug, displayName: slug, description: 'Synthetic IDE fixture',
    hidden: slug === 'hidden', isDefault: slug === 'a',
    defaultReasoningEffort: 'low', supportedReasoningEfforts: [
      {reasoningEffort: 'low', description: 'Low'}, {reasoningEffort: 'high', description: 'High'}], ...extra};
}

async function fixture(t) {
  // /tmp is intentionally inadmissible (world writable); use a private user tree.
  const directory = await fs.promises.mkdtemp(path.join(os.homedir(), '.lain-groups-host-test-'));
  t.after(() => fs.promises.rm(directory, {recursive: true, force: true}));
  const filename = path.join(directory, 'models.json');
  const records = ['a', 'shared', 'router', 'hidden'].map(slug => record(slug));
  const catalog = {models: records.map(model => ({slug: model.model, display_name: model.displayName,
    description: model.description, visibility: model.hidden ? 'hide' : 'list',
    default_reasoning_level: model.defaultReasoningEffort,
    supported_reasoning_levels: model.supportedReasoningEfforts.map(effort =>
      ({effort: effort.reasoningEffort, description: effort.description}))})),
    lain_model_picker: {schema_version: 1, groups: [
      {id: 'free', name: 'Free', models: ['a', 'shared'], router_model: 'router'},
      {id: 'sec', name: 'IT Sec 🛡', models: ['shared', 'hidden']} ]}};
  const config = {config: {model_provider: 'lain', model_catalog_json: filename, profile: null,
    model_providers: {lain: {base_url: 'http://127.0.0.1:18575/v1', wire_api: 'responses',
      experimental_bearer_token: 'fixture-secret-do-not-return'}}},
    layers: [{name: {type: 'user', file: path.join(directory, 'config.toml'), profile: null}, version: 'v1'}]};
  const calls = [], settings = new Map();
  const vscode = {env: {remoteName: undefined}, workspace: {workspaceFolders: [],
    getConfiguration(section, uri) {
      assert.equal(section, 'chatgpt');
      return {get(key) { assert.equal(key, 'cliExecutable'); return settings.get(uri?.fsPath ?? 'global'); }};
    }}};
  const params = {hostId: 'local', modelProvider: 'lain', modelCatalogPath: filename, models: copy(records)};
  const f = {directory, filename, records, catalog, config, calls, settings, vscode, params,
    configFor: () => copy(config), pageFor: () => ({data: copy(records), nextCursor: null})};
  const context = {cwd: directory, vscode, request: async (method, requestParams) => {
    calls.push({method, params: copy(requestParams)});
    if (f.beforeRequest) await f.beforeRequest(method, requestParams);
    if (method === 'config/read') return f.configFor(requestParams);
    assert.equal(method, 'model/list');
    return f.pageFor(requestParams);
  }};
  f.context = context;
  f.write = async (value = f.catalog) => fs.promises.writeFile(filename, JSON.stringify(value), {mode: 0o600});
  f.run = () => readGroups(context, params);
  f.folder = name => ({uri: {scheme: 'file', fsPath: path.join(directory, name)}});
  await f.write();
  return f;
}

function fallback(result, reason) {
  assert.deepEqual(result, {status: 'native', reason, generation: null, groups: []});
}

test('ready: preserves order, overlap, Unicode and hidden models; verifies native membership', async t => {
  const f = await fixture(t), result = await f.run();
  assert.equal(result.status, 'ready');
  assert.equal(result.reason, 'ready');
  assert.match(result.generation, /^[a-f0-9]{64}$/);
  assert.deepEqual(result.groups, [
    {id: 'free', name: 'Free', models: ['a', 'shared'], router_model: 'router'},
    {id: 'sec', name: 'IT Sec 🛡', models: ['shared', 'hidden'], router_model: null}]);
  assert.equal((await f.run()).generation, result.generation);
  assert.deepEqual(Object.keys(result).sort(), ['generation', 'groups', 'reason', 'status']);
  for (const secret of [f.filename, f.directory, 'fixture-secret-do-not-return', 'base_url', 'displayName']) {
    assert.equal(JSON.stringify(result).includes(secret), false);
  }
  assert.deepEqual(f.calls.filter(call => call.method === 'model/list')[0].params,
    {includeHidden: true, limit: 100, cursor: null});
  assert.deepEqual(f.calls.filter(call => call.method === 'config/read').slice(0, 4).map(call => call.params), [
    {includeLayers: true}, {includeLayers: true, cwd: f.directory},
    {includeLayers: true}, {includeLayers: true, cwd: f.directory}]);
});

test('reads every model page and compares stable object keys', async t => {
  const f = await fixture(t);
  f.pageFor = params => params.cursor === null
    ? {data: copy(f.records.slice(0, 2)), nextCursor: 'second'}
    : {data: copy(f.records.slice(2)), nextCursor: null};
  f.params.models = f.params.models.map(model => Object.fromEntries(Object.entries(model).reverse()));
  assert.equal((await f.run()).status, 'ready');
  assert.deepEqual(f.calls.filter(call => call.method === 'model/list').map(call => call.params.cursor), [null, 'second']);
});

test('web model or reasoning differences return exactly models-changed', async t => {
  const f = await fixture(t);
  for (const change of [models => models.reverse(), models => models.pop(),
    models => { models[0].id = 'other'; }, models => { models[0].model = 'other'; },
    models => { models[0].displayName = 'Other'; }, models => { models[0].hidden = true; },
    models => { models[0].isDefault = false; }, models => { models[0].defaultReasoningEffort = 'high'; },
    models => { models[0].supportedReasoningEfforts.pop(); }]) {
    f.params.models = copy(f.records);
    change(f.params.models);
    fallback(await f.run(), 'models-changed');
  }
});

test('catalog must match cached native slugs, labels, visibility and explicit reasoning', async t => {
  const f = await fixture(t), original = copy(f.catalog);
  for (const change of [
    value => { value.models.pop(); value.lain_model_picker.groups[1].models.pop(); },
    value => { value.models.push({...value.models[0], slug: 'new-model'}); },
    value => { value.models[0].slug = 'renamed'; value.lain_model_picker.groups[0].models[0] = 'renamed'; },
    value => { value.models[0].display_name = 'New name'; },
    value => { value.models[0].description = 'New description'; },
    value => { value.models[0].visibility = 'hide'; },
    value => { value.models[0].default_reasoning_level = 'high'; },
    value => { value.models[0].supported_reasoning_levels = []; },
    value => { value.models[0].supported_reasoning_levels[0].description = 'New reasoning'; }
  ]) {
    const value = copy(original);
    change(value);
    await f.write(value);
    fallback(await f.run(), 'catalog-models-changed');
  }
  const reordered = copy(original);
  reordered.models.reverse();
  await f.write(reordered);
  assert.equal((await f.run()).status, 'ready');
});

test('an explicit none default with no supported reasoning remains valid when native agrees', async t => {
  const f = await fixture(t);
  f.catalog.models[0].default_reasoning_level = 'none';
  f.catalog.models[0].supported_reasoning_levels = [];
  f.records[0].defaultReasoningEffort = 'none';
  f.records[0].supportedReasoningEfforts = [];
  f.params.models = copy(f.records);
  await f.write();
  assert.equal((await f.run()).status, 'ready');
});

test('global hints must agree with startup config and every workspace provider definition', async t => {
  const f = await fixture(t);
  f.vscode.workspace.workspaceFolders = [f.folder('one'), f.folder('two')];
  for (const [cwd, change] of [[undefined, config => { config.model_catalog_json += '.other'; }],
    [f.directory, config => { config.model_provider = 'other'; }],
    [path.join(f.directory, 'two'), config => { config.model_providers.lain.base_url += '/other'; }],
    [path.join(f.directory, 'one'), config => { config.model_providers.lain.wire_api = 'other'; }]]) {
    f.calls.length = 0;
    f.configFor = params => {
      const result = copy(f.config);
      if (params.cwd === cwd) change(result.config);
      return result;
    };
    fallback(await f.run(), 'context-changed');
    assert.equal(f.calls.filter(call => call.method === 'config/read').length, 4);
  }
  f.configFor = () => copy(f.config);
  f.params.modelCatalogPath += '.wrong';
  fallback(await f.run(), 'context-changed');
});

test('rejects remote hosts, remote workspace folders, and custom executables before reading files', async t => {
  const f = await fixture(t);
  f.params.hostId = 'remote';
  fallback(await f.run(), 'unsupported-host');
  f.params.hostId = 'local';
  f.vscode.env.remoteName = 'ssh-remote';
  fallback(await f.run(), 'unsupported-host');
  f.vscode.env.remoteName = undefined;
  f.settings.set('global', '/custom/codex');
  fallback(await f.run(), 'custom-cli');
  f.settings.set('global', '   ');
  const folder = f.folder('project');
  f.vscode.workspace.workspaceFolders = [folder];
  f.settings.set(folder.uri.fsPath, '/workspace/codex');
  fallback(await f.run(), 'custom-cli');
  f.settings.delete(folder.uri.fsPath);
  folder.uri.scheme = 'vscode-remote';
  fallback(await f.run(), 'unsupported-host');
  assert.deepEqual(f.calls, []);
});

test('profile selection requires native provenance; does not guess from config profile or filenames', async t => {
  const f = await fixture(t);
  f.config.config.profile = 'other';
  fallback(await f.run(), 'unsupported-profile');
  f.config.config.profile = 'lain';
  fallback(await f.run(), 'unsupported-profile');
  f.config.layers[0].name.profile = 'lain';
  assert.equal((await f.run()).status, 'ready');
  f.config.config.profile = null;
  f.config.layers[0].name.profile = 'other';
  fallback(await f.run(), 'unsupported-profile');
});

test('metadata schema matches CLI group rules and rejects duplicate catalog slugs', async t => {
  const f = await fixture(t), original = copy(f.catalog);
  const changes = [
    value => { value.lain_model_picker.schema_version = 2; },
    value => { value.lain_model_picker.extra = true; },
    value => { value.lain_model_picker.groups[0].extra = true; },
    value => { value.lain_model_picker.groups[1].id = 'free'; },
    value => { value.lain_model_picker.groups[0].id = ' '; },
    value => { value.lain_model_picker.groups[0].name = 'line\nbreak'; },
    value => { value.lain_model_picker.groups[0].name = 'a'.repeat(1025); },
    value => { value.lain_model_picker.groups[0].models = ['a', 'a']; },
    value => { value.lain_model_picker.groups[0].models = ['invented']; },
    value => { value.lain_model_picker.groups[0].router_model = 'invented'; },
    value => { value.lain_model_picker.groups[0].router_model = 12; },
    value => { value.lain_model_picker.groups = Array.from({length: 129}, (_, index) =>
      ({id: `group-${index}`, name: 'Group', models: []})); },
    value => { value.lain_model_picker.groups[0].id = 'é'.repeat(129); },
    value => { value.lain_model_picker.groups[0].name = '\u0085'; },
    value => { value.lain_model_picker.groups[0].name = 'control\u007f'; },
    value => { delete value.lain_model_picker.groups[0].models; },
    value => { value.lain_model_picker.groups[0].models = [5]; },
    value => { value.models.push({slug: 'a'}); }
  ];
  for (const change of changes) {
    const value = copy(original);
    change(value);
    await f.write(value);
    fallback(await f.run(), 'invalid-catalog');
  }
  await fs.promises.writeFile(f.filename, '{"models":[],"models":[],"lain_model_picker":{"schema_version":1,"groups":[]}}');
  fallback(await f.run(), 'invalid-catalog');
});

test('rejects malformed JSON, escaped duplicate keys, invalid Unicode and excessive depth', async t => {
  const f = await fixture(t);
  for (const contents of ['{',
    '{"models":[],"\\u006dodels":[],"lain_model_picker":{"schema_version":1,"groups":[]}}',
    '{"models":[],"lain_model_picker":{"schema_version":1,"schema_version":1,"groups":[]}}',
    '{"models":[],"lain_model_picker":{"schema_version":1,"groups":[{"id":"a","id":"b","name":"B","models":[]}]}}',
    '{"models":[],"lain_model_picker":{"schema_version":1,"groups":[{"id":"a","name":"\\ud800","models":[]}]}}',
    '['.repeat(65) + 'null' + ']'.repeat(65),
    '\ufeff' + JSON.stringify(f.catalog),
    Buffer.from([0xc3, 0x28])]) {
    await fs.promises.writeFile(f.filename, contents);
    fallback(await f.run(), 'invalid-catalog');
  }
  await f.write({models: f.catalog.models});
  fallback(await f.run(), 'groups-unavailable');
});

test('file validation rejects symlinks, writable modes, oversize files and missing catalogs', async t => {
  const f = await fixture(t);
  await fs.promises.chmod(f.filename, 0o620);
  fallback(await f.run(), 'unsafe-catalog');
  await fs.promises.chmod(f.filename, 0o600);
  await fs.promises.chmod(f.directory, 0o720);
  fallback(await f.run(), 'unsafe-catalog');
  await fs.promises.chmod(f.directory, 0o700);
  const target = path.join(f.directory, 'target.json');
  await fs.promises.rename(f.filename, target);
  await fs.promises.symlink(target, f.filename);
  fallback(await f.run(), 'unsafe-catalog');
  await fs.promises.unlink(f.filename);
  await fs.promises.rename(target, f.filename);
  await fs.promises.truncate(f.filename, 16 * 1024 * 1024 + 1);
  fallback(await f.run(), 'invalid-catalog');
  await fs.promises.unlink(f.filename);
  fallback(await f.run(), 'catalog-unavailable');
});

test('rejects symlinked ancestors, special files, foreign ownership and unsafe file reads', async t => {
  const f = await fixture(t);
  const targetDirectory = path.join(f.directory, 'real');
  const linkedDirectory = path.join(f.directory, 'linked');
  await fs.promises.mkdir(targetDirectory);
  await fs.promises.writeFile(path.join(targetDirectory, 'models.json'), JSON.stringify(f.catalog));
  await fs.promises.symlink(targetDirectory, linkedDirectory);
  f.config.config.model_catalog_json = f.params.modelCatalogPath = path.join(linkedDirectory, 'models.json');
  fallback(await f.run(), 'unsafe-catalog');
  f.config.config.model_catalog_json = f.params.modelCatalogPath = targetDirectory;
  fallback(await f.run(), 'unsafe-catalog');
  f.config.config.model_catalog_json = f.params.modelCatalogPath = f.filename;
  const lstat = fs.promises.lstat;
  t.mock.method(fs.promises, 'lstat', async (filename, options) => {
    const stat = await lstat(filename, options);
    if (filename === f.filename) stat.uid += 1n;
    return stat;
  });
  fallback(await f.run(), 'unsafe-catalog');
  t.mock.restoreAll();
  t.mock.method(fs.promises, 'open', async () => { throw new Error('private catalog path/token'); });
  fallback(await f.run(), 'catalog-unavailable');
});

test('rejects catalog changes during fd reads and inode replacement before open', async t => {
  const f = await fixture(t), open = fs.promises.open;
  t.mock.method(fs.promises, 'open', async (...args) => {
    const handle = await open(...args), read = handle.read.bind(handle);
    let changed = false;
    handle.read = async (...readArgs) => {
      const result = await read(...readArgs);
      if (!changed) {
        changed = true;
        await fs.promises.appendFile(f.filename, ' ');
      }
      return result;
    };
    return handle;
  });
  fallback(await f.run(), 'catalog-changed');
  t.mock.restoreAll();
  await f.write();
  t.mock.method(fs.promises, 'open', async (...args) => {
    const replacement = path.join(f.directory, 'replacement.json');
    await fs.promises.writeFile(replacement, JSON.stringify(f.catalog));
    await fs.promises.rename(replacement, f.filename);
    return open(...args);
  });
  fallback(await f.run(), 'catalog-changed');
});

test('checks config and catalog again before returning groups', async t => {
  const f = await fixture(t);
  f.beforeRequest = async method => {
    if (method === 'model/list') f.config.layers[0].version = 'v2';
  };
  fallback(await f.run(), 'context-changed');
  f.beforeRequest = async method => {
    if (method === 'model/list') {
      f.catalog.lain_model_picker.groups[0].name += '!';
      await f.write();
    }
  };
  fallback(await f.run(), 'catalog-changed');
});

test('workspace and executable changes in flight invalidate a previously proven context', async t => {
  const f = await fixture(t);
  f.beforeRequest = method => {
    if (method === 'model/list') f.vscode.workspace.workspaceFolders.push(f.folder('new-project'));
  };
  fallback(await f.run(), 'context-changed');
  f.vscode.workspace.workspaceFolders = [];
  f.beforeRequest = method => { if (method === 'model/list') f.settings.set('global', '/new/codex'); };
  fallback(await f.run(), 'context-changed');
});

test('generation changes for new valid metadata and unchanged model records', async t => {
  const f = await fixture(t), first = await f.run();
  f.catalog.lain_model_picker.groups[0].name = 'Free revised';
  await f.write();
  const second = await f.run();
  assert.equal(second.status, 'ready');
  assert.notEqual(first.generation, second.generation);
});

test('malformed native pages and duplicate models propagate fixed protocol errors', async t => {
  const f = await fixture(t);
  for (const page of [{data: f.records}, {data: f.records, nextCursor: ''},
    {data: [], nextCursor: 'empty'}, {data: f.records, nextCursor: 'loop'},
    {data: [...f.records, f.records[0]], nextCursor: null},
    {data: [{...f.records[0], hidden: 'false'}], nextCursor: null},
    {data: Array.from({length: 101}, () => f.records[0]), nextCursor: null}]) {
    f.pageFor = () => page;
    await assert.rejects(f.run(), error => error.code === 'LAIN_GROUPS_NATIVE_PROTOCOL' &&
      error.message === 'Invalid native model groups response');
  }
  f.configFor = () => ({config: {}});
  await assert.rejects(f.run(), error => error.code === 'LAIN_GROUPS_NATIVE_PROTOCOL');
});

test('native pagination is bounded to 100 pages and oversized web lists are rejected early', async t => {
  const f = await fixture(t);
  f.pageFor = params => ({data: [record(`page-${params.cursor ?? 'first'}`)],
    nextCursor: String(Number(params.cursor ?? 0) + 1)});
  await assert.rejects(f.run(), error => error.code === 'LAIN_GROUPS_NATIVE_PROTOCOL');
  assert.equal(f.calls.filter(call => call.method === 'model/list').length, 100);
  f.calls.length = 0;
  f.params.models = Array.from({length: 10001}, (_, index) => record(`model-${index}`));
  fallback(await f.run(), 'invalid-request');
  assert.equal(f.calls.length, 0);
});

test('native config and model errors propagate unchanged', async t => {
  const f = await fixture(t), error = new Error('native failure');
  for (const method of ['config/read', 'model/list']) {
    f.beforeRequest = current => { if (current === method) throw error; };
    await assert.rejects(f.run(), caught => caught === error);
  }
});

test('deadline and abort bound pending bridge requests without starting further work', async t => {
  const f = await fixture(t);
  let settle;
  f.configFor = () => new Promise(resolve => { settle = resolve; });
  f.context.timeoutMs = 20;
  fallback(await f.run(), 'timeout');
  settle(copy(f.config));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.length, 1);
  f.calls.length = 0;
  const controller = new AbortController();
  f.context.signal = controller.signal;
  f.context.timeoutMs = 1000;
  const pending = f.run();
  controller.abort();
  fallback(await pending, 'aborted');
  settle(copy(f.config));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.calls.length, 1);
});
