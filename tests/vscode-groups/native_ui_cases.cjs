const assert=require('node:assert/strict');
const fs=require('node:fs');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function until(check, timeout=60000){
 const end=Date.now()+timeout;
 while(Date.now()<end){if(await check())return;await pause(200);}
 throw Error('Native UI condition timed out');
}
async function frame(page){
 let found;
 await until(async()=>{found=page.frames().find(f=>f.url().includes('/fake.html'));return found&&await found.getByRole('button',{name:'Select model',exact:true}).count();});
 return found;
}
async function models(page){
 const f=await frame(page);
 if(await f.locator('[data-model-picker-view=advanced] [data-active=true] [data-lain-groups]').isVisible()) return f;
 if(!await f.getByRole('menuitem',{name:'Select model',exact:true}).isVisible())
  await f.getByRole('button',{name:'Select model',exact:true}).click();
 if(await f.getByRole('menuitem',{name:'Select model',exact:true}).isVisible())
  await f.getByRole('menuitem',{name:'Select model',exact:true}).click();
 await f.locator('[data-model-picker-view=advanced]').waitFor();
 await pause(400);
 return f;
}
async function send(page){
 const f=await frame(page),before=fs.existsSync('/mnt/provider-requests.json')?JSON.parse(fs.readFileSync('/mnt/provider-requests.json')).length:0;
 const answers=await f.getByText('Synthetic VS Code response.',{exact:true}).count();
 const editor=f.locator('[contenteditable=true]').first();
 await editor.fill('LAIN_VSCODE_SYNTHETIC_TURN');
 await f.getByRole('button',{name:'Send',exact:true}).click();
 await until(()=>fs.existsSync('/mnt/provider-requests.json')&&JSON.parse(fs.readFileSync('/mnt/provider-requests.json')).length>before);
 await until(async()=>(await f.getByText('Synthetic VS Code response.',{exact:true}).count())>answers);
 return JSON.parse(fs.readFileSync('/mnt/provider-requests.json')).at(-1);
}
async function groups(page){
 const checks={};
 let f=await models(page);
 await f.getByRole('menuitem',{name:'Free',exact:true}).waitFor();
 const initial=fs.readFileSync('/home/tomka/.codex/config.toml','utf8');
 await f.getByRole('menuitem',{name:'Free',exact:true}).click();
 assert.equal(await f.getByRole('menuitemradio',{name:'fixture-shared',exact:true}).count(),1);
 assert.equal(await f.getByRole('menuitemradio',{name:'Automatisch',exact:true}).getAttribute('aria-disabled'),null);
 await f.getByRole('menuitem',{name:'Zurück zu Gruppen',exact:true}).press('Escape');
 await f.getByRole('menuitem',{name:'Free',exact:true}).waitFor();
 await until(async()=>await f.evaluate(()=>document.activeElement.getAttribute('aria-label'))==='Free');
 checks.escapeAndFocus=true;
 await f.getByRole('menuitem',{name:'Free',exact:true}).press('ArrowDown');
 await until(async()=>await f.evaluate(()=>document.activeElement.getAttribute('aria-label'))==='IT Sec 🛡');
 await f.getByRole('menuitem',{name:'IT Sec 🛡',exact:true}).press('Enter');
 assert.equal(await f.getByRole('menuitem',{name:/Automatisch/}).getAttribute('aria-disabled'),'true');
 assert.equal(await f.getByRole('menuitemradio',{name:'fixture-shared',exact:true}).count(),1);
 assert.equal(await f.getByRole('menuitemradio',{name:'fixture-hidden',exact:true}).count(),0);
 checks.keyboardOverlapHiddenAndMissingRouter=true;
 await f.getByRole('menuitem',{name:'Zurück zu Gruppen',exact:true}).click();
 await f.getByRole('menuitem',{name:'Alle',exact:true}).click();
 const all=await f.getByRole('menuitemradio').allTextContents();
 assert.deepEqual(all,['fixture-a','fixture-shared','fixture-router']);
 assert.equal(fs.readFileSync('/home/tomka/.codex/config.toml','utf8'),initial);
 checks.allUniqueAndNavigationReadOnly=true;
 await f.getByRole('menuitemradio',{name:'fixture-shared',exact:true}).click();
 await f.getByRole('menuitem',{name:'Power',exact:true}).press('ArrowRight');
 await until(()=>/model = "fixture-shared"/.test(fs.readFileSync('/home/tomka/.codex/config.toml','utf8'))&&/model_reasoning_effort = "high"/.test(fs.readFileSync('/home/tomka/.codex/config.toml','utf8')));
 await page.keyboard.press('Escape');
 assert.equal(await f.getByRole('menuitem',{name:'Power',exact:true}).isVisible(),false);
 checks.defaultModelAndReasoningPersisted=true;
 assert.deepEqual(await send(page),{model:'fixture-shared',effort:'high'});
 checks.syntheticNewThread=true;
 f=await models(page);
 await f.getByRole('menuitem',{name:'Alle',exact:true}).click();
 await f.getByRole('menuitemradio',{name:'fixture-a',exact:true}).click();
 await f.getByRole('menuitem',{name:'Power',exact:true}).press('ArrowLeft');
 await page.keyboard.press('Escape');
 assert.deepEqual(await send(page),{model:'fixture-a',effort:'low'});
 checks.existingThreadModelAndReasoning=true;
 f=await models(page);
 await f.getByRole('menuitem',{name:'Free',exact:true}).waitFor();
 const catalog=fs.readFileSync('/home/tomka/.codex/models.json');
 try{
  fs.writeFileSync('/home/tomka/.codex/models.json','{"lain_model_picker":');
  await f.locator('[data-lain-groups=native]').waitFor({timeout:15000});
  assert.equal(await f.getByRole('menuitemradio').count(),3);
  checks.invalidMetadataNativeFallback=true;
 }finally{fs.writeFileSync('/home/tomka/.codex/models.json',catalog);}
 await f.getByRole('menuitem',{name:'Free',exact:true}).waitFor({timeout:15000});
 const updated=JSON.parse(catalog);updated.lain_model_picker.groups[0].name='Free aktualisiert';
 try{
  fs.writeFileSync('/home/tomka/.codex/models.json',JSON.stringify(updated));
  await f.getByRole('menuitem',{name:'Free aktualisiert',exact:true}).waitFor({timeout:15000});
  checks.freshGeneration=true;
 }finally{fs.writeFileSync('/home/tomka/.codex/models.json',catalog);}
 await f.getByRole('menuitem',{name:'Free',exact:true}).waitFor({timeout:15000});
 const width=await f.locator('[data-lain-groups=groups]').evaluate(el=>el.getBoundingClientRect().width);
 assert(width>0&&width<300);
 checks.narrowSidebar=true;
 await f.getByRole('menuitem',{name:'Free',exact:true}).click();
 await f.getByRole('menuitemradio',{name:'Automatisch',exact:true}).click();
 await page.keyboard.press('Escape');
 assert.deepEqual(await send(page),{model:'fixture-router',effort:'low'});
 checks.routerUsesNativeAlias=true;
 return checks;
}
module.exports={frame,models,groups,send,until};
