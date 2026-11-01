#!/usr/bin/env python3
"""Real isolated Host+Core -> loopback fake provider; never installed state/providers.
Requires explicit artifact paths. All keys/content are synthetic. No cloud or telemetry.
"""
import argparse,hashlib,json,os,socket,subprocess,tempfile,threading,time,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer

def need(ok,text):
 if not ok:raise AssertionError(text)
def free_port():
 with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]

def main():
 ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--host',type=Path,required=True);ap.add_argument('--core',type=Path,required=True);ap.add_argument('--output',type=Path,required=True);ap.add_argument('--latency-rounds',type=int,default=12);a=ap.parse_args();need(1<=a.latency_rounds<=100,'bounded synthetic latency rounds required')
 host=a.host.resolve(strict=True);core=a.core.resolve(strict=True);a.output.mkdir(parents=True,exist_ok=True)
 opener=urllib.request.build_opener(urllib.request.ProxyHandler({}));checks=[];seen=[];api_errors=[];canary='sk-recording-fixture-canary-123456789';key='synthetic-upstream-key-no-real-account'
 class Provider(BaseHTTPRequestHandler):
  def log_message(self,*_):pass
  def do_GET(self):
   body=json.dumps({'object':'list','data':[{'id':'fixture-model-a','object':'model'},{'id':'fixture-model-b','object':'model'}]}).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(body)
  def do_POST(self):
   raw=self.rfile.read(int(self.headers.get('Content-Length',0)));data=json.loads(raw);messages=data.get('messages',[])
   reported=any(m.get('role')=='tool' for m in messages)
   tool=bool(data.get('tools')) and not reported
   seen.append({'path':self.path,'model':data.get('model'),'authenticated':self.headers.get('Authorization')=='Bearer '+key,'reportedToolResult':reported,'bodyHash':hashlib.sha256(raw).hexdigest()})
   self.send_response(200)
   if data.get('stream'):
    self.send_header('Content-Type','text/event-stream');self.send_header('Connection','close');self.end_headers()
    def send(delta,finish=None,usage=None):
     x={'id':'chat-fixture','object':'chat.completion.chunk','created':1,'model':data['model'],'choices':[{'index':0,'delta':delta,'finish_reason':finish}]}
     if usage is not None:x['usage']=usage
     self.wfile.write(('data: '+json.dumps(x)+'\n\n').encode());self.wfile.flush()
    if tool:
     text=json.dumps({'value':canary});cut=len(text)//2
     send({'role':'assistant','tool_calls':[{'index':0,'id':'call-fixture','type':'function','function':{'name':'fixture_echo','arguments':text[:cut]}}]})
     send({'tool_calls':[{'index':0,'function':{'arguments':text[cut:]}}]})
     send({},'tool_calls',{'prompt_tokens':7,'completion_tokens':3,'total_tokens':10})
    else:send({'role':'assistant','content':'Synthetic final response.'},'stop',{'prompt_tokens':9,'completion_tokens':2,'total_tokens':11})
    self.wfile.write(b'data: [DONE]\n\n');self.wfile.flush()
   else:
    self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(json.dumps({'id':'chat-fixture','object':'chat.completion','created':1,'model':data['model'],'choices':[{'index':0,'message':{'role':'assistant','content':'Synthetic final response.'},'finish_reason':'stop'}],'usage':{'prompt_tokens':9,'completion_tokens':2,'total_tokens':11}}).encode())
 with ThreadingHTTPServer(('127.0.0.1',0),Provider) as provider, tempfile.TemporaryDirectory(prefix='lain-rec-e2e-',dir=os.environ.get('TMPDIR')) as temp:
  thread=threading.Thread(target=provider.serve_forever,daemon=True);thread.start();root=Path(temp);state=root/'state';state.mkdir();home=root/'home';home.mkdir();bin=root/'core';bin.mkdir()
  import shutil
  shutil.copy2(core,bin/'lain-proxy');digest=hashlib.sha256((bin/'lain-proxy').read_bytes()).hexdigest();(bin/'binary-manifest.json').write_text(json.dumps({'sha256':digest}));(bin/'qualification.json').write_text(json.dumps({'sha256':digest,'passed':False,'syntheticPassed':True}))
  hp,cp=free_port(),free_port()
  while cp==hp:cp=free_port()
  env=dict(os.environ,HOME=str(home),XDG_CONFIG_HOME=str(home/'config'),XDG_DATA_HOME=str(home/'data'),LAIN_HOME=str(state),LAIN_PROXY_PATH=str(bin/'lain-proxy'),LAIN_REFERENCE_QUALIFICATION='1',LAIN_REFERENCE_DISABLE_QUOTA='1',LAIN_REFERENCE_HOST_PORT=str(hp),LAIN_REFERENCE_PROXY_PORT=str(cp))
  stdout=(a.output/'host-stdout.log').open('w');stderr=(a.output/'host-stderr.log').open('w');process=None
  def start():return subprocess.Popen(['dotnet',str(host)],env=env,stdout=stdout,stderr=stderr)
  def api(path,method='GET',value=None):
   token=(state/'credentials/host.token').read_text().strip();headers={'Authorization':'Bearer '+token,'Content-Type':'application/json'}
   req=urllib.request.Request(f'http://127.0.0.1:{hp}/api/v1'+path,method=method,headers=headers,data=None if value is None else json.dumps(value).encode())
   try:res=opener.open(req,timeout=30)
   except urllib.error.HTTPError as e:res=e
   with res:
    body=res.read();result=json.loads(body) if body else None
    need(token.encode() not in body,'Host token leaked in API')
    if res.status>=400:api_errors.append({'method':method,'path':path,'status':res.status,'code':result.get('code') if isinstance(result,dict) else None})
    return res.status,result
  def wait(fn,label,seconds=35):
   until=time.monotonic()+seconds
   while time.monotonic()<until:
    try:
     result=fn()
     if result:return result
    except (OSError,FileNotFoundError,urllib.error.URLError,ValueError):pass
    if process.poll() is not None:raise AssertionError('Host exited during '+label)
    time.sleep(.15)
   raise AssertionError('Timeout: '+label)
  def llm(model='fixture-a',stream=False,tools=False,reported=False,worker=None):
   conv=worker or conversation
   inp=[{'role':'user','content':[{'type':'input_text','text':'Synthetic request: API_KEY='+canary}]}]
   if reported:inp += [{'type':'function_call','call_id':'call-fixture','name':'fixture_echo','arguments':'{}'},{'type':'function_call_output','call_id':'call-fixture','output':'Synthetic result supplied by client, not an executed tool'}]
   data={'model':model,'input':inp,'stream':stream,'store':False}
   if tools:data['tools']=[{'type':'function','name':'fixture_echo','description':'Synthetic declared tool','parameters':{'type':'object','properties':{'value':{'type':'string'}}}}]
   headers={'Authorization':'Bearer '+(state/'credentials/inference.token').read_text().strip(),'Content-Type':'application/json','Thread-Id':conv}
   if worker:headers['X-Codex-Turn-Metadata']=json.dumps({'thread_id':worker,'parent_thread_id':conversation,'subagent_kind':'thread_spawn','turn_id':str(uuid.uuid4())})
   req=urllib.request.Request(f'http://127.0.0.1:{cp}/v1/responses',headers=headers,data=json.dumps(data).encode())
   with opener.open(req,timeout=30) as res:body=res.read();return res.status,body
  conversation=str(uuid.uuid4())
  try:
   process=start();wait(lambda:(state/'credentials/host.token').exists() and api('/status')[0]==200,'Host boot')
   c=api('/configuration')[1];code,c=api('/accounts','POST',{'revision':c['revision'],'value':{'name':'Synthetic fixture','provider':'fixture','apiKey':key,'baseUrl':f'http://127.0.0.1:{provider.server_port}/v1'}});need(code==200,'account fixture create failed');acct=c['accounts'][0]
   for id,alias,model in [('a','fixture-a','fixture-model-a'),('b','fixture-b','fixture-model-b')]:
    code,c=api('/routes/'+id,'PUT',{'revision':c['revision'],'value':{'id':id,'alias':alias,'upstreamModel':model,'provider':acct['provider'],'accountIds':[acct['id']]}});need(code==200,'route create failed')
   code,c=api('/profiles/p','PUT',{'revision':c['revision'],'value':{'id':'p','name':'Fixture','routeIds':['a','b'],'defaultRouteId':'a'}});need(code==200,'profile create failed')
   code,c=api('/profiles/p/apply','POST',{'revision':c['revision']});need(code==200,'profile apply failed');wait(lambda:api('/status')[1]['proxyReady'],'Core ready')
   status,body=llm();need(status==200,'disabled request failed');need(api('/recording/status')[1]['storage']['recordCount']==0,'default off retained content');checks.append('default-off real request, no retained records')
   policy=api('/recording/policy')[1];code,policy=api('/recording/policy','PUT',{'expectedRevision':policy['revision'],'enabled':True,'mode':'content'});need(code==200,'enable failed')
   wait(lambda:api('/recording/status')[1]['state'] in ['recording','collecting','partial'],'source policy acknowledged')
   status,streambody=llm(stream=True,tools=True);need(status==200 and b'response.completed' in streambody,'tool stream terminal missing');need(canary.encode() in streambody,'recorder changed forwarded tool arguments');checks.append('real Core stream/tools pass through unredacted to synthetic client')
   need(llm(model='fixture-b',stream=True,reported=True)[0]==200,'tool result continuation failed')
   worker=str(uuid.uuid4());need(llm(worker=worker)[0]==200,'worker request failed')
   def saved_conversations(minimum=0):
    code,result=api('/recording/conversations?limit=20')
    if code in (400,409) and result.get('code')=='stale_cursor':return None
    need(code==200,'conversation query failed: HTTP '+str(code)+' '+str(result.get('code')))
    need(isinstance(result.get('items'),list),'conversation query schema mismatch')
    return result if len(result['items'])>=minimum else None
   page=wait(lambda:saved_conversations(2),'saved root and worker conversations')
   events=api('/recording/events?limit=100')[1];decoded=json.dumps(events)
   event_shapes=[]
   for item in events['records']:
    observation=item['observation'];shape={k:observation.get(k) for k in ['sequence','kind','boundary','protocol','complete','gapReason','redactionState']}
    if observation.get('body'):
     document=json.loads(observation['body']);shape['bodyKeys']=list(document) if isinstance(document,dict) else []
     shape['inputTypes']=[entry.get('type','message') for entry in document.get('input',[]) if isinstance(entry,dict)] if isinstance(document,dict) and isinstance(document.get('input'),list) else []
    event_shapes.append(shape)
   (a.output/'source-shapes.json').write_text(json.dumps(event_shapes,indent=2)+'\n')
   need(canary not in decoded and key not in decoded,'secret reached decoded archive');need('fixture_echo' in decoded,'declared/observed tool missing');checks.append('redaction before journal/index read; no upstream credentials')
   # Exact source lookup is exercised separately from the archive-wide event list.
   # Reassemble small pages and compare against the selected durable sanitized body.
   selected=next(item for item in events['records'] if item['observation']['kind']=='request' and item['observation'].get('body'))
   observation=selected['observation'];expected_body=observation['body']
   selected_ref={name:observation[name] for name in ['sourceEpoch','sequence','eventId']}
   selected_path='/recording/sources/'+urllib.parse.quote(selected_ref['sourceEpoch'],safe='')+'/'+str(selected_ref['sequence'])
   def source_page(offset=0,characters=64,event_id=None,path=None,extra=''):
    query=urllib.parse.urlencode({'eventId':selected_ref['eventId'] if event_id is None else event_id,'offset':offset,'characters':characters})
    code,result=api((selected_path if path is None else path)+'?'+query+extra)
    encoded=json.dumps(result)
    private_values=[canary,key]+[(state/'credentials'/name).read_text().strip() for name in ['host.token','inference.token','management.token']]
    need(all(secret not in encoded for secret in private_values),'selected source leaked synthetic content canary or local/upstream credential')
    return code,result
   def utf16_length(text):return len(text.encode('utf-16-le'))//2
   offset=0;fragments=[];page_count=0
   while True:
    code,source=source_page(offset)
    need(code==200,'selected source page failed: HTTP '+str(code))
    need(source['source']==selected_ref,'source page returned a different event identity')
    need(source['boundary']==observation['boundary'] and source['protocol']==observation['protocol'],'source page attribution changed')
    need(source['contentHash']==selected['contentHash'] and source['redactionState']==observation['redactionState'],'source page durable/redaction metadata changed')
    need(source['offset']==offset and source['totalCharacters']==utf16_length(expected_body),'source page offset/total mismatch')
    text=source['text'];need(isinstance(text,str) and 0<utf16_length(text)<=64,'source page failed its content bound or progress')
    fragments.append(text);offset+=utf16_length(text);page_count+=1
    need(source['hasMore']==(offset<source['totalCharacters']),'source continuation flag incorrect')
    need(page_count<=128,'source paging exceeded bounded fixture size')
    if not source['hasMore']:break
   reassembled=''.join(fragments)
   need(page_count>=2 and reassembled==expected_body,'selected source pages skipped/mixed retained content')
   need('[REDACTED' in reassembled,'selected secret-bearing source lacks explicit redaction')
   code,end=source_page(offset);need(code==200 and end['text']=='' and not end['hasMore'],'exact end offset should produce an empty terminal page')
   code,single=source_page(0,1);need(code==200 and 1<=utf16_length(single['text'])<=2 and single['hasMore'],'single-unit source page failed progress')
   invalid_cases=[(-1,64,400),(offset+1,64,409),(0,0,400),(0,32769,400)]
   for invalid_offset,invalid_size,expected_status in invalid_cases:
    code,_=source_page(invalid_offset,invalid_size);need(code==expected_status,'source bounds accepted invalid offset/size')
   code,_=source_page(extra='&offset=1');need(code==400,'duplicate source offset accepted')
   code,_=api(selected_path+'?offset=0&characters=64');need(code==400,'source lookup accepted missing event identity')
   other=next(item['observation'] for item in events['records'] if item['observation']['eventId']!=selected_ref['eventId'])
   code,_=source_page(event_id=other['eventId']);need(code==404,'source lookup accepted a different existing eventId')
   other_path='/recording/sources/'+urllib.parse.quote(other['sourceEpoch'],safe='')+'/'+str(other['sequence'])
   code,_=source_page(path=other_path);need(code==404,'source lookup accepted mismatched existing sequence/eventId')
   missing_path='/recording/sources/synthetic-missing-epoch/'+str(selected_ref['sequence'])
   code,_=source_page(path=missing_path);need(code==404,'source lookup exposed selected body under another source epoch')
   source_paging={'source':selected_ref,'pages':page_count,'characters':offset,'redactionVerified':True,'wrongIdentityRejected':True,'invalidBoundsRejected':True}
   checks.append('selected-source exact-identity paging, bounded offsets, redacted reconstruction and credential exclusion')
   details=[api('/recording/conversations/'+x['id']+'?limit=100')[1] for x in page['items']]
   rootdetails=[d for d in details if len(d['conversation']['models'])>=2];need(len(rootdetails)==1,'model switch split or mixed root conversation')
   records=rootdetails[0]['records']
   (a.output/'projection-shapes.json').write_text(json.dumps([{'protocol':r.get('protocol'),'gapReason':r.get('gapReason'),'model':r.get('model'),'requestedModel':r.get('requestedModel'),'parts':[{'kind':part.get('kind'),'hasArguments':bool(part.get('arguments')),'hasResult':bool(part.get('result')),'toolCallId':part.get('toolCallId')} for message in r['messages'] for part in message['parts']]} for r in records],indent=2)+'\n')
   parts=[p for r in records for m in r['messages'] for p in m['parts']]
   need(any(p.get('toolCallId')=='call-fixture' and p.get('arguments') for p in parts),'assembled tool call missing');need(any(p.get('result') for p in parts),'reported tool result missing');checks.append('cross-request root/model-switch/tool result plus separate evidenced worker')
   latency={};load=[]
   import statistics
   def core_status():
    management=(state/'credentials/management.token').read_text().strip()
    request=urllib.request.Request(f'http://127.0.0.1:{cp}/lain/recording/status',headers={'Authorization':'Bearer '+management})
    with opener.open(request,timeout=10) as response:return json.load(response)
   def memory_snapshot():
    def rss(pid):
     try:
      for line in Path('/proc/'+str(pid)+'/status').read_text().splitlines():
       if line.startswith('VmRSS:'):return int(line.split()[1])
     except OSError:pass
     return None
    core_rss=None
    try:
     children=Path('/proc/'+str(process.pid)+'/task/'+str(process.pid)+'/children').read_text().split()
     values=[rss(pid) for pid in children if Path('/proc/'+pid+'/comm').read_text().strip()=='lain-proxy']
     values=[value for value in values if value is not None]
     if values:core_rss=sum(values)
    except OSError:pass
    return {'hostRssKiB':rss(process.pid),'coreRssKiB':core_rss}
   def capture_snapshot():
    status=api('/recording/status')[1];source=core_status()
    return {'hostState':status['state'],'hostErrorCode':status.get('errorCode'),'storage':status['storage'],
     'hostDroppedRecords':status['droppedRecords'],'hostUnknownLoss':status['unknownLoss'],
     'sourceEpoch':source['sourceEpoch'],'sourceLatestSequence':source['latestSequence'],
     'sourceDroppedEvents':source['droppedEvents'],'sourceUnknownCrashLoss':source['unknownCrashLoss'],
     'sourceQueuedBytes':source['queuedBytes'],'sourceQueuedEvents':source['queuedEvents']}
   def await_durable_source(after_cursor,target):
    position=after_cursor;observed_sequence=0
    def read_more():
     nonlocal position,observed_sequence
     code,retained=api('/recording/events?after='+str(position)+'&limit=100')
     need(code==200,'load durable cursor query failed')
     for entry in retained['records']:
      value=entry['observation']
      if value['sourceEpoch']==target['sourceEpoch']:observed_sequence=max(observed_sequence,value['sequence'])
     position=retained['nextCursor']
     return observed_sequence>=target['latestSequence']
    wait(read_more,'load durable source catch-up',seconds=25)
    return {'targetSourceSequence':target['latestSequence'],'observedSourceSequence':observed_sequence,'lastJournalCursor':position}
   for enabled,label in [(False,'off'),(True,'content')]:
    policy=api('/recording/policy')[1];code,policy=api('/recording/policy','PUT',{'expectedRevision':policy['revision'],'enabled':enabled,'mode':'content'});need(code==200,'latency mode switch failed');time.sleep(1.2)
    for concurrency in (1,4,8):
     before=capture_snapshot();memory_before=memory_snapshot();peak=dict(memory_before);stop_sampling=threading.Event()
     def sample_memory():
      while not stop_sampling.wait(.025):
       for metric,value in memory_snapshot().items():
        if value is not None:peak[metric]=max(peak.get(metric) or 0,value)
     sampler=threading.Thread(target=sample_memory,daemon=True);sampler.start()
     sample_count=max(a.latency_rounds,concurrency);batch_started=time.perf_counter()
     def measure(_):
      request_started=time.perf_counter();status,_=llm();elapsed=(time.perf_counter()-request_started)*1000
      need(status==200,'synthetic concurrent latency request failed')
      return elapsed
     try:
      with ThreadPoolExecutor(max_workers=concurrency) as executor:measured=list(executor.map(measure,range(sample_count)))
     finally:stop_sampling.set();sampler.join(timeout=2)
     elapsed_seconds=time.perf_counter()-batch_started;source_after=core_status()
     durable=await_durable_source(before['storage']['highWatermark'],source_after) if enabled else None
     after=capture_snapshot();memory_after=memory_snapshot()
     if not enabled:need(after['storage']['recordCount']==before['storage']['recordCount'],'disabled concurrent load retained new records')
     ordered=sorted(measured);sample={'mode':label,'concurrency':concurrency,'samples':len(measured),'statuses':{'200':len(measured)},
      'elapsedMs':elapsed_seconds*1000,'requestsPerSecond':len(measured)/elapsed_seconds,
      'p50Ms':statistics.median(measured),'p95Ms':ordered[min(len(ordered)-1,int(len(ordered)*.95))],'maximumMs':max(measured),
      'captureBefore':before,'captureAfter':after,'durableCatchUp':durable,
      'memoryBefore':memory_before,'sampledPeakMemory':peak,'memoryAfter':memory_after,
      'memorySamplingWindow':'Sampled during HTTP requests; memoryAfter is after asynchronous durable catch-up',
      'measurementScope':'Synthetic loopback full HTTP response latency; sampled process RSS, not TTFT or an overhead/performance qualification'}
     load.append(sample)
     if concurrency==1:latency[label]={name:sample[name] for name in ['samples','p50Ms','p95Ms','maximumMs']}
   checks.append('real off/content HTTP load at concurrency 1/4/8 with latency, status, RSS, storage/drop observations and durable source catch-up')
   stored=api('/recording/status')[1]['storage']['recordCount'];revision=policy['revision'];policy=api('/recording/policy','PUT',{'expectedRevision':revision,'enabled':False,'mode':'content'})[1];time.sleep(1.2)
   stored=api('/recording/status')[1]['storage']['recordCount'];need(llm()[0]==200,'off broke inference');time.sleep(1.2);need(api('/recording/status')[1]['storage']['recordCount']==stored,'capture off retained extra data');checks.append('stop independent of inference')
   process.terminate();process.wait(timeout=20);process=start();wait(lambda:api('/status')[0]==200,'restart');after=api('/recording/conversations?limit=20')[1];need(len(after['items'])==len(page['items']),'archive lost after restart');checks.append('journal and SQLite survive real Host restart/UI absent')
   need(not list((state/'proxy').rglob('*.log')),'SDK error/body logger created files')
   code,_=api('/recording/archive?expectedRevision='+str(policy['revision']),'DELETE');need(code==200,'archive delete failed');need(api('/recording/conversations?limit=20')[1]['items']==[],'deleted conversation replayed');checks.append('disabled archive clear invalidates index');need(source_page()[0]==404,'cleared selected source remained accessible');checks.append('cleared selected source cannot be fetched or replayed')
   report={'result':'PASS','scope':'Real isolated candidate Host/Core, loopback fake provider only, no production state or actual local tool execution','hostSha256':hashlib.sha256(host.read_bytes()).hexdigest(),'coreSha256':digest,'checks':checks,'providerRequests':seen,'recordCountBeforeRestart':stored,'syntheticHttpLatency':latency,'sourcePaging':source_paging,'syntheticConcurrentHttpLoad':load}
   (a.output/'evidence.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'result':report['result'],'checks':report['checks'],'hostSha256':report['hostSha256'],'coreSha256':report['coreSha256'],'evidence':str(a.output/'evidence.json'),'load':[{'mode':item['mode'],'concurrency':item['concurrency'],'samples':item['samples'],'p95Ms':item['p95Ms'],'sourceDropDelta':item['captureAfter']['sourceDroppedEvents']-item['captureBefore']['sourceDroppedEvents'],'hostUnknownLoss':item['captureAfter']['hostUnknownLoss']} for item in load]},indent=2))
  except Exception as failure:
   report={'result':'FAIL','failureType':type(failure).__name__,'failure':str(failure),'checksCompleted':checks,'apiErrors':api_errors,'providerRequests':seen,'hostSha256':hashlib.sha256(host.read_bytes()).hexdigest(),'coreSha256':digest}
   try:report['recordingStatus']=api('/recording/status')[1]
   except Exception:pass
   (a.output/'failure.json').write_text(json.dumps(report,indent=2)+'\n')
   print(json.dumps({'result':'FAIL','failure':str(failure),'apiErrors':api_errors},indent=2))
   raise
  finally:
   if process and process.poll() is None:process.terminate();process.wait(timeout=20)
   provider.shutdown();thread.join(timeout=3);stdout.close();stderr.close()
if __name__=='__main__':main()
