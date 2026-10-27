import test from 'node:test';
import assert from 'node:assert/strict';
import {LainRows, groupedRows} from '../../clients/vscode/lain-groups-ui.js';

test('first menu render is usable while group verification is pending', () => {
  const React = {useState: initial => [initial, () => {}], useRef: value => ({current:value}), useEffect: () => {}};
  const tree = LainRows({React, jsx:(type,props)=>({type,...props}), Menu:{},
    options:[{id:'a'}],renderOption:option=>option,metadata:{verify:async()=>({status:'ready',generation:'one',groups:[]})}});
  assert.equal(tree['data-lain-groups'],'checking');
  assert.equal(tree.children[0].children, 'Checking Lain groups …');
  assert.equal(tree.children[1].id,'a');
});
test('native contexts render precisely the original model options', () => {
  const options=[{id:'a'}, {id:'b',disabled:true}];
  assert.deepEqual(groupedRows({options,renderOption:x=>x}), options);
});

test('group navigation and router copy are English and preserve protocol IDs', () => {
  const metadata = {};
  const render = (result, selection = null) => {
    const states = [{metadata, result}, selection];
    const React = {useState: () => [states.shift(), () => {}], useRef: value => ({current:value}), useEffect: () => {}};
    return LainRows({React, jsx:(type,props)=>({type,...props}), Menu:{Item:'item'},
      options:[{id:'a'}], renderOption:option=>option, metadata});
  };
  const ready = {status:'ready', generation:'one', groups:[{id:'free',name:'Free',models:['a']}]};
  const root = render(ready);
  assert.equal(root.children[0]['data-lain-group-id'], 'group:free');
  assert.equal(root.children[0].SubText, '1 models');
  assert.equal(root.children[1].children, 'All');
  assert.equal(root.children[1]['data-lain-group-id'], 'all:');
  const members = render(ready, {id:'group:free',generation:'one'});
  assert.equal(members.children[0]['aria-label'], 'Back to groups');
  assert.equal(members.children[0].children, '← Groups');
  assert.equal(members.children[2].children, 'Automatic');
  assert.equal(members.children[2].SubText, 'No group router available');
  assert.equal(members.children[2].disabled, true);
  assert.equal(render({status:'native'}).children[0].children, 'Lain groups unavailable · native model picker');
});
