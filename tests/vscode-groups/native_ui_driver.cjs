const fs=require('fs');
const {chromium}=require('/mnt/node_modules/playwright');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 let browser;
 for(let i=0;i<120;i++){
  try{browser=await chromium.connectOverCDP('http://127.0.0.1:19277');break;}catch{await pause(500);}
 }
 if(!browser)throw Error('No native CDP connection');
 const context=browser.contexts()[0];
 for(let i=0;i<120&&context.pages().length===0;i++)await pause(500);
 let page=context.pages()[0];
 if(!page)throw Error('No native workbench page');
 page.on('console',m=>{if(m.type()==='error')fs.appendFileSync('/mnt/browser-console.log',m.text()+'\n');});
 page.on('pageerror',e=>fs.appendFileSync('/mnt/browser-console.log',String(e.stack)+'\n'));
 await page.waitForTimeout(8000);
 const snapshot=async()=>{
  const pages=await Promise.all(context.pages().map(async p=>({url:p.url(),frames:await Promise.all(p.frames().map(async f=>({url:f.url(),text:await f.locator('body').innerText().catch(()=>''),buttons:await f.locator('button').evaluateAll(xs=>xs.map(x=>({text:x.innerText,aria:x.getAttribute('aria-label'),title:x.getAttribute('title')}))).catch(()=>[])})))})));
  fs.writeFileSync('/mnt/snapshot.json',JSON.stringify(pages,null,2));
  await page.screenshot({path:'/mnt/screen.png'});
  return pages;
 };
 await snapshot();
 fs.writeFileSync('/mnt/driver-ready.json',JSON.stringify({status:'ready',native:true}));
 let seen=0;
 for(let i=0;i<1600;i++){
  if(fs.existsSync('/mnt/stop'))break;
  if(fs.existsSync('/mnt/command.json')){
   const command=JSON.parse(fs.readFileSync('/mnt/command.json'));
   if(command.id>seen){seen=command.id;
    fs.mkdirSync('/mnt/commands',{recursive:true});
    fs.writeFileSync(`/mnt/commands/command-${seen}.json`,JSON.stringify(command,null,2));
    let record;try{
    const result=await new (Object.getPrototypeOf(async function(){}).constructor)('browser','context','page','fs','snapshot','require',command.code)(browser,context,page,fs,snapshot,require);
    record={id:seen,result};
   }catch(e){record={id:seen,error:String(e.stack)};}
    fs.writeFileSync('/mnt/command-result.json',JSON.stringify(record,null,2));
    fs.writeFileSync(`/mnt/commands/result-${seen}.json`,JSON.stringify(record,null,2));
   }
  }
  await pause(500);
 }
 await browser.close();
})().catch(e=>{fs.writeFileSync('/mnt/driver-error.txt',String(e.stack));process.exit(1);});
