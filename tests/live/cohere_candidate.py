#!/usr/bin/env python3
"""Bounded real Cohere test through an owned candidate Core and existing Lain.

No provider credentials or production changes. The candidate uses a synthetic
key to an owned loopback forwarder; only that forwarder holds a local inference
token in memory. Run under the shared verification flock. Default: prepare only.
"""
import argparse
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import subprocess
import sys
import threading
import uuid

import realistic_models as probe
import model_boundaries as boundaries

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'backend'))
from qualify import Run, CLIENT_KEY

MODEL='command-a-plus-05-2026'
SYNTHETIC_KEY='lain-synthetic-a'
ALIAS='cohere-candidate-qualified'


def source_contract():
    paths=[Path(__file__),Path(probe.__file__),Path(boundaries.__file__),
           Path(__file__).resolve().parents[1]/'backend/qualify.py']
    return {str(p.resolve().relative_to(Path(__file__).resolve().parents[1])):probe.sha(p.read_bytes()) for p in paths}


def body(tools, inputs=None):
    request=probe.payload({'alias':ALIAS,'reasoningEffort':'none'},tools,inputs,mode=probe.DEFAULT_MODE)
    request.pop('max_output_tokens')  # Deliberate native-default diagnostic scenario.
    request.update(parallel_tool_calls=True,include=['reasoning.encrypted_content'],
                   client_metadata={'fixture':'lain-cohere-qualification'},
                   prompt_cache_key='lain-cohere-qualification-synthetic')
    return request


class Forwarder:
    def __init__(self, base, token, account, conversation):
        self.base,self.token,self.account,self.conversation=base,token,account,conversation
        self.observations=[]; self.lock=threading.Lock()
        owner=self
        class Handler(BaseHTTPRequestHandler):
            def log_message(self,*_):pass
            def do_POST(self):
                connection=None
                try:
                    if self.path!='/v1/chat/completions' or self.headers.get('Authorization')!='Bearer '+SYNTHETIC_KEY:
                        self.send_error(400,'owned fixture boundary');return
                    size=int(self.headers.get('Content-Length','0'))
                    if not 0<size<=512*1024:self.send_error(400,'fixture size');return
                    raw=self.rfile.read(size); payload=json.loads(raw)
                    if payload.get('model')!=MODEL:self.send_error(400,'fixture model pin');return
                    with owner.lock:
                        if len(owner.observations)>=2:self.send_error(400,'fixture call budget');return
                        number=len(owner.observations)+1
                        row={'step':number,'requestBytes':len(raw),'requestSha256':probe.sha(raw),
                             'fields':sorted(payload),'tools':len(payload.get('tools',[])),
                             'toolChoiceAbsent':'tool_choice' not in payload,
                             'parallelControlAbsent':'parallel_tool_calls' not in payload}
                        owner.observations.append(row)
                    # Deliberately pin the existing route/account; no arbitrary URL forwarding.
                    from urllib.parse import urlsplit
                    endpoint=urlsplit(owner.base)
                    connection=http.client.HTTPConnection(endpoint.hostname,endpoint.port,timeout=45)
                    connection.request('POST','/v1/chat/completions',raw,headers={
                        'Authorization':'Bearer '+owner.token,'Content-Type':'application/json',
                        'User-Agent':'lain-cohere-candidate-qualification/0.1',
                        'X-Lain-Account-ID':owner.account,
                        'X-Codex-Turn-Metadata':json.dumps({'thread_id':owner.conversation,'turn_id':'cohere-step-'+str(number)})})
                    response=connection.getresponse();row['statusCode']=response.status
                    self.send_response(response.status)
                    self.send_header('Content-Type',response.getheader('Content-Type','application/json'))
                    self.send_header('Connection','close');self.end_headers()
                    count=0
                    while True:
                        data=response.read1(16384)
                        if not data:break
                        count+=len(data)
                        if count>4*1024*1024:raise ValueError('forward response size')
                        self.wfile.write(data);self.wfile.flush()
                    row['responseBytes']=count
                except Exception as error:
                    with owner.lock:owner.observations.append({'forwarderError':type(error).__name__})
                    self.close_connection=True
                finally:
                    if connection:connection.close()
        self.server=ThreadingHTTPServer(('127.0.0.1',0),Handler);self.server.daemon_threads=True
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True)
    @property
    def url(self):return 'http://127.0.0.1:'+str(self.server.server_port)+'/v1'
    def __enter__(self):self.thread.start();return self
    def __exit__(self,*_):self.server.shutdown();self.server.server_close();self.thread.join(timeout=3)


def prepare(args):
    source=args.source_run.resolve(); raw=(source/'plan.json').read_bytes();binding=json.loads(raw)
    boundaries.verify_binding(binding)
    entry=next(e for e in binding['entries'] if e['alias']==MODEL)
    tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
    frozen={'schemaVersion':2,'kind':'cohere-candidate-live-qualification','binding':binding,
        'entry':entry,'sourceRun':str(source),'core':str(args.core.resolve(strict=True)),
        'coreSha256':probe.sha(args.core.read_bytes()),'nativeToolsSha256':probe.sha(probe.TOOLS_FILE.read_bytes()),
        'sourcesSha256':source_contract(),
        'firstRequestSha256':probe.sha(probe.encoded(body(tools))),
        'continuationTemplateSha256':probe.sha(probe.encoded(body(tools,[]))),
        'maximumExistingGatewayRequests':2,
        'conversationId':'lain-cohere-candidate-'+uuid.uuid4().hex}
    args.output.mkdir(mode=0o700,parents=True,exist_ok=False);probe.save(args.output/'plan.json',frozen)
    print(json.dumps({'planSha256':probe.sha((args.output/'plan.json').read_bytes()),'maximumRequests':2}))


def execute(args):
    raw=(args.output/'plan.json').read_bytes()
    if probe.sha(raw)!=args.expect_plan_sha256:raise ValueError('Frozen plan hash mismatch')
    plan=json.loads(raw)
    if (args.output/'report.json').exists():raise ValueError('Existing live evidence must not be retried/overwritten')
    if plan.get('schemaVersion')!=2 or plan.get('sourcesSha256')!=source_contract():
        raise ValueError('Frozen source contract changed; preserve evidence and prepare a distinct run')
    if plan.get('maximumExistingGatewayRequests')!=2:raise ValueError('Unexpected inference budget')
    if probe.sha(Path(plan['core']).read_bytes())!=plan['coreSha256']:raise ValueError('Core changed')
    if probe.sha(probe.TOOLS_FILE.read_bytes())!=plan['nativeToolsSha256']:raise ValueError('Tools changed')
    boundaries.verify_binding(plan['binding']);tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
    initial=body(tools)
    if probe.sha(probe.encoded(initial))!=plan['firstRequestSha256']:raise ValueError('Prepared payload changed')
    if probe.sha(probe.encoded(body(tools,[])))!=plan['continuationTemplateSha256']:
        raise ValueError('Prepared continuation changed')
    credential=subprocess.run(['lain','auth','token','--audience','codex'],capture_output=True,text=True,timeout=20,check=True)
    token=credential.stdout.strip()
    if not token or '\n' in token:raise ValueError('Invalid local inference credential')
    report={'kind':plan['kind'],'planSha256':probe.sha(raw),'coreSha256':plan['coreSha256'],
            'conversationId':plan['conversationId'],'startedAt':probe.now(),'state':'running','calls':[]}
    def checkpoint():probe.save(args.output/'report.json',report)
    checkpoint(); run=None
    forward=Forwarder(plan['binding']['endpoint'],token,plan['entry']['accountId'],plan['conversationId'])
    try:
        run=Run(plan['core'],provider=forward)
        kind='openai-compatibility:cohere-openai-chat'
        credential_id=kind+':'+hashlib.sha256((kind+'\0'+SYNTHETIC_KEY+'\0'+forward.url+'\0').encode()).hexdigest()[:12]
        run.bindings={'accounts':{'account-a':credential_id},'routes':{ALIAS:{'id':'cohere-candidate-route',
            'model':MODEL,'provider':'cohere','upstreamProtocol':'openai','accounts':['account-a'],'selection':'strict'}}}
        run.write_bindings()
        (run.root/'core.yaml').write_text('host: "127.0.0.1"\nport: '+str(run.port)+'\nauth-dir: '+json.dumps(str(run.root/'auth'))+
            '\napi-keys: ['+json.dumps(CLIENT_KEY)+']\nrequest-retry: 0\nmax-retry-interval: 1\n'+
            'disable-codex-cloaking: true\nopenai-compatibility:\n  - name: cohere-openai-chat\n'+
            '    upstream-protocol: openai-chat\n    base-url: '+json.dumps(forward.url)+'\n'+
            '    api-key-entries:\n      - api-key: '+SYNTHETIC_KEY+'\n    models:\n      - name: '+MODEL+'\n        alias: '+MODEL+'\n')
        run.start();entry={'alias':ALIAS,'accountId':'account-a'}
        call={'step':'tool'};report['calls'].append(call);checkpoint()
        output,meta=probe.exchange(run.url+'/v1',CLIENT_KEY,entry,initial,call);call.update(meta,state='passed');checkpoint()
        calls=[i for i in output if i.get('type')=='function_call']
        if len(calls)!=1:raise probe.OutcomeError('expected_one_tool_call')
        fixture=Path(plan['sourceRun'])/'fixture/src/invoice.py'
        if probe.sha(fixture.read_bytes())!=plan['binding']['fixtureSha256']:raise ValueError('Fixture changed')
        result=probe.tool_result(calls[0],fixture,allow_native_options=True)
        boundaries.verify_binding(plan['binding'])
        follow=body(tools,initial['input']+output+[result])
        call={'step':'continuation'};report['calls'].append(call);checkpoint()
        output,meta=probe.exchange(run.url+'/v1',CLIENT_KEY,entry,follow,call);call.update(meta,state='passed')
        text=''.join(c.get('text','') for i in output if i.get('type')=='message' for c in i.get('content',[]) if c.get('type')=='output_text')
        assessment=probe.assess_answer(text)
        report.update(state='passed' if assessment['passed'] else 'answer_review_needed',quality=assessment,
            answerBytes=len(text.encode()),answerSha256=probe.sha(text.encode()))
    except probe.OutcomeError as error:report.update(state='failed',category=error.category,details=error.details)
    except Exception as error:report.update(state='harness_error',errorType=type(error).__name__)
    finally:
        if run:
            run.__exit__()
        else:forward.server.server_close()
        report.update(finishedAt=probe.now(),forwarded=forward.observations,
                      existingGatewayRequests=sum('statusCode' in o for o in forward.observations))
        checkpoint()
    print(json.dumps({'state':report['state'],'category':report.get('category'),'requests':report['existingGatewayRequests']}))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--core',type=Path);parser.add_argument('--source-run',type=Path)
    parser.add_argument('--output',type=Path,required=True);parser.add_argument('--execute',action='store_true')
    parser.add_argument('--expect-plan-sha256');args=parser.parse_args()
    if args.execute:
        if not args.expect_plan_sha256:parser.error('Execution needs frozen plan hash')
        execute(args)
    else:
        if not args.core or not args.source_run:parser.error('Preparation needs --core and --source-run')
        prepare(args)


if __name__=='__main__':main()
