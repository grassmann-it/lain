import test from 'node:test';
import assert from 'node:assert/strict';
import {loadLainModels} from '../../clients/vscode/lain-groups-query.js';

const base = {hostId: 'local', modelProvider: 'lain', modelCatalogPath: '/fixture', limit: 2};
test('all native pages are bound to one verified catalog; model records and reasoning survive', async () => {
  const model = id => ({id, model: id, supportedReasoningEfforts: [{reasoningEffort: 'high'}]});
  const calls = [];
  const groups = async params => {
    assert.deepEqual(params.models.map(x => x.model), ['a', 'b', 'c']);
    return {status: 'ready', generation: 'one', groups: [{id: 'free', name: 'Free', models: ['a', 'c']}]};
  };
  const result = await loadLainModels({...base, groups, request: async params => {
    calls.push(params);
    return params.cursor == null ? {data: [model('a'), model('b')], nextCursor: 'page-2'} : {data: [model('c')], nextCursor: null};
  }});
  assert.equal(calls.length, 2);
  assert.equal(result.data.length, 3);
  assert.equal(result.nextCursor, null);
  assert.deepEqual(result.data[0].supportedReasoningEfforts, [{reasoningEffort: 'high'}]);
  assert.equal(result.data[0]._lainGroups.generation, 'one');
  assert.equal((await result.data[0]._lainGroups.verify()).status, 'ready');
});
test('remote and other providers retain the native first-page behavior', async () => {
  for (const changed of [{hostId:'ssh'}, {modelProvider:'other'}]) {
    const page = {data: [{model:'a'}], nextCursor:'more'};
    assert.equal(await loadLainModels({...base, ...changed, request: async () => page, groups: () => {throw Error('should not read');}}), page);
  }
});
test('native backend failures are visible and pagination cycles are rejected', async () => {
  await assert.rejects(loadLainModels({...base, request: async () => {throw Error('native failure');}}), /native failure/);
  await assert.rejects(loadLainModels({...base, request: async () => ({data: [], nextCursor:'same'})}), /pagination/);
});
test('metadata outage leaves native models and an unavailable hint', async () => {
  const result = await loadLainModels({...base, request: async () => ({data:[{model:'a'}]}), groups: async () => {throw Error('offline');}});
  assert.equal(result.data[0]._lainGroups.status, 'native');
  assert.equal(result.data[0].model, 'a');
});
test('an aborted context cannot finish attaching a previous catalog', async () => {
  const controller = new AbortController(); controller.abort();
  await assert.rejects(loadLainModels({...base, signal:controller.signal, request: async () => ({data:[],nextCursor:'next'})}), /Aborted/);
});
test('a late metadata response is discarded after cancellation', async () => {
  const controller = new AbortController();
  await assert.rejects(loadLainModels({...base,signal:controller.signal,request:async()=>({data:[]}),groups:async()=>{
    controller.abort(); return {status:'ready',generation:'old',groups:[]};
  }}), /Aborted/);
});
