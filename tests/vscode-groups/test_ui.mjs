import test from 'node:test';
import assert from 'node:assert/strict';
import {LainRows, groupedRows} from '../../clients/vscode/lain-groups-ui.js';

test('first menu render is usable while group verification is pending', () => {
  const React = {useState: initial => [initial, () => {}], useRef: value => ({current:value}), useEffect: () => {}};
  const tree = LainRows({React, jsx:(type,props)=>({type,...props}), Menu:{},
    options:[{id:'a'}],renderOption:option=>option,metadata:{verify:async()=>({status:'ready',generation:'one',groups:[]})}});
  assert.equal(tree['data-lain-groups'],'checking');
  assert.equal(tree.children[1].id,'a');
});
test('native contexts render precisely the original model options', () => {
  const options=[{id:'a'}, {id:'b',disabled:true}];
  assert.deepEqual(groupedRows({options,renderOption:x=>x}), options);
});
