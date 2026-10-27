#!/usr/bin/env python3
"""Explicit follow-up scenarios, never an automatic retry of the model sweep."""
import argparse
import copy
import json
from pathlib import Path
import subprocess
import uuid

import realistic_models as probe


SCENARIOS = [
    {'id':'cohere-native-auto-output-default','alias':'command-a-plus-05-2026',
     'toolChoice':'auto','outputBudget':None,'tools':7,'maximumRequests':2},
    {'id':'kimi-auto-choice','alias':'k3',
     'toolChoice':'auto','outputBudget':2048,'tools':7,'maximumRequests':2},
    {'id':'beam-128-tools','alias':'cmd-beam-free',
     'toolChoice':'forced','outputBudget':2048,'tools':128,'maximumRequests':1},
    {'id':'beam-129-tools','alias':'cmd-beam-free',
     'toolChoice':'forced','outputBudget':2048,'tools':129,'maximumRequests':1},
]


def body_for(entry, tools, scenario, inputs=None):
    body=probe.payload(entry,tools,inputs)
    for number in range(len(tools),scenario['tools']):
        body['tools'].append({'type':'function','name':f'qualification_unused_{number:03d}',
            'description':'Synthetic unused tool for an explicit tool-count boundary test. Do not call.',
            'parameters':{'type':'object','properties':{},'additionalProperties':False}})
    if scenario['outputBudget'] is None:body.pop('max_output_tokens')
    else:body['max_output_tokens']=scenario['outputBudget']
    if scenario['toolChoice']=='auto':body['tool_choice']='auto'
    return body


def verify_binding(plan):
    current=probe.cli('status')
    if (not current.get('proxyReady') or current['revision']!=plan['revision'] or
        current['appliedRevision']!=plan['revision'] or
        current['activeProfileId']!=plan['profileId'] or
        probe.cli('models')!=plan['modelInventory'] or
        probe.sha(Path(plan['catalogPath']).read_bytes())!=plan['catalogSha256'] or
        probe.sha(probe.TOOLS_FILE.read_bytes())!=plan['clientToolFixtureSha256']):
        raise ValueError('Frozen model/profile/catalog/tool binding changed')
    probe.local_base(plan['endpoint'])


def prepare(source, output):
    source=source.resolve()
    raw=(source/'plan.json').read_bytes(); plan=json.loads(raw)
    report=json.loads((source/'report.json').read_bytes())
    if report['state']!='finished' or report['planSha256']!=probe.sha(raw):
        raise ValueError('Follow-up needs a completed, source-bound sweep')
    verify_binding(plan)
    fixture=source/'fixture/src/invoice.py'
    if probe.sha(fixture.read_bytes())!=plan['fixtureSha256']:raise ValueError('Fixture changed')
    entries={e['alias']:e for e in plan['entries']}
    scenarios=[]
    tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
    for scenario in SCENARIOS:
        entry=copy.deepcopy(entries[scenario['alias']])
        scenarios.append({**scenario,'entry':entry,
            'firstRequestSha256':probe.sha(probe.encoded(body_for(entry,tools,scenario)))})
    output.mkdir(mode=0o700,parents=True,exist_ok=False)
    frozen={'schemaVersion':1,'kind':'lain-model-boundary-diagnostics','createdAt':probe.now(),
        'sourceRun':str(source),'sourcePlanSha256':probe.sha(raw),
        'sourceReportSha256':probe.sha((source/'report.json').read_bytes()),
        'binding':plan,'scenarios':scenarios,
        'maximumRequests':sum(s['maximumRequests'] for s in scenarios),
        'conversationId':'lain-model-boundaries-'+uuid.uuid4().hex}
    probe.save(output/'plan.json',frozen)
    print(json.dumps({'plan':str(output/'plan.json'),'maximumRequests':frozen['maximumRequests'],
                      'planSha256':probe.sha((output/'plan.json').read_bytes())}))


def execute(output, expected):
    raw=(output/'plan.json').read_bytes()
    if probe.sha(raw)!=expected:raise ValueError('Frozen diagnostic plan hash mismatch')
    frozen=json.loads(raw); plan=frozen['binding']; source=Path(frozen['sourceRun'])
    if (output/'report.json').exists():raise ValueError('Existing evidence must not be overwritten or retried')
    if (probe.sha((source/'plan.json').read_bytes())!=frozen['sourcePlanSha256'] or
        probe.sha((source/'report.json').read_bytes())!=frozen['sourceReportSha256']):
        raise ValueError('Source evidence changed')
    verify_binding(plan)
    fixture=source/'fixture/src/invoice.py'
    if probe.sha(fixture.read_bytes())!=plan['fixtureSha256']:raise ValueError('Fixture changed')
    tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
    result=subprocess.run(['lain','auth','token','--audience','codex'],
                          capture_output=True,text=True,timeout=20,check=True)
    token=result.stdout.strip()
    if not token or '\n' in token:raise ValueError('Invalid local inference credential')
    report={'schemaVersion':1,'kind':frozen['kind'],'planSha256':probe.sha(raw),
            'conversationId':frozen['conversationId'],'startedAt':probe.now(),'rows':[]}
    def checkpoint():probe.save(output/'report.json',report)
    checkpoint()
    for scenario in frozen['scenarios']:
        if (output/'stop').exists():report['state']='cancelled';break
        verify_binding(plan)
        entry={**scenario['entry'],'conversationId':frozen['conversationId'],'turnId':scenario['id']}
        row={**scenario,'state':'running','calls':[]}; report['rows'].append(row);checkpoint()
        try:
            body=body_for(entry,tools,scenario)
            if probe.sha(probe.encoded(body))!=scenario['firstRequestSha256']:
                raise ValueError('Scenario payload changed since preparation')
            call={'step':'tool'};row['calls'].append(call);checkpoint()
            output_items,meta=probe.exchange(plan['endpoint'],token,entry,body,call)
            call.update(meta,state='passed');checkpoint()
            if scenario['maximumRequests']==1:
                row.update(state='schema_accepted',toolCount= len(body['tools']))
            else:
                calls=[i for i in output_items if i.get('type') in ('function_call','custom_tool_call')]
                if len(calls)!=1:raise probe.OutcomeError('expected_one_tool_call')
                # Record argument names only, never model-supplied command text.
                try:row['toolArgumentKeys']=sorted(json.loads(calls[0]['arguments']))
                except (ValueError,KeyError,TypeError):pass
                tool_output=probe.tool_result(calls[0],fixture)
                verify_binding(plan)
                follow=body_for(entry,tools,scenario,body['input']+output_items+[tool_output])
                call={'step':'continuation'};row['calls'].append(call);checkpoint()
                final,meta=probe.exchange(plan['endpoint'],token,entry,follow,call)
                call.update(meta,state='passed')
                text=''.join(c.get('text','') for i in final if i.get('type')=='message'
                            for c in i.get('content',[]) if c.get('type')=='output_text')
                if any(i.get('type') in ('function_call','custom_tool_call') for i in final):
                    raise probe.OutcomeError('unexpected_continuation_tool')
                if not text.strip():raise probe.OutcomeError('empty_final_answer')
                row.update(state='tool_roundtrip_completed',answerBytes=len(text.encode()),
                           answerSha256=probe.sha(text.encode()))
        except probe.OutcomeError as error:
            row.update(state='failed',category=error.category,details=error.details)
            if row['calls']:row['calls'][-1].setdefault('state','failed')
        except Exception as error:
            row.update(state='harness_error',errorType=type(error).__name__)
        row['finishedAt']=probe.now();checkpoint()
        print(json.dumps({'scenario':scenario['id'],'state':row['state'],
                          'category':row.get('category'),'calls':len(row['calls'])}),flush=True)
    else:report['state']='finished'
    report.update(finishedAt=probe.now(),inferenceHttpRequests=sum(len(r['calls']) for r in report['rows']))
    report['statusAfter']=probe.cli('status');checkpoint()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-run',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--execute',action='store_true')
    parser.add_argument('--expect-plan-sha256')
    args=parser.parse_args()
    if args.execute:
        if not args.expect_plan_sha256:parser.error('--execute requires --expect-plan-sha256')
        execute(args.output,args.expect_plan_sha256)
    else:
        if not args.source_run:parser.error('preparation requires --source-run')
        prepare(args.source_run,args.output)


if __name__=='__main__':main()
