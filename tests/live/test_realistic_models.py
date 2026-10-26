import copy
import io
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

import realistic_models as probe
import model_boundaries as boundaries


class LiveQualificationTests(unittest.TestCase):
    def test_neutral_native_metadata_never_executes_model_shell_or_privilege_options(self):
        item={'type':'function_call','name':'exec_command','call_id':'call','arguments':json.dumps({
            'cmd':probe.COMMAND,'workdir':probe.WORKDIR,'login':False,'tty':False,
            'sandbox_permissions':'use_default','max_output_tokens':2000,'yield_time_ms':10000})}
        result=probe.subprocess.CompletedProcess([],0,stdout=b'fixture contents')
        with patch.object(probe.subprocess,'run',return_value=result) as run:
            probe.tool_result(item,Path('/fixed/fixture.py'),allow_native_options=True)
        self.assertEqual(run.call_args.args[0],['/usr/bin/cat','/fixed/fixture.py'])
        for change in [{'sandbox_permissions':'require_escalated'},{'max_output_tokens':True},
                       {'max_output_tokens':10001},{'login':True},{'shell':'/bin/sh'},
                       {'cmd':probe.COMMAND+'; touch unwanted'}]:
            arguments=json.loads(item['arguments']);arguments.update(change)
            with patch.object(probe.subprocess,'run') as run,self.assertRaises(probe.OutcomeError):
                probe.tool_result({**item,'arguments':json.dumps(arguments)},Path('/fixed/fixture.py'),allow_native_options=True)
            run.assert_not_called()

    def test_regression_selection_keeps_exact_route_identity_and_refuses_unknowns(self):
        entries=[{'alias':'k3','provider':'kimi'},{'alias':'cmd-kimi-k3','provider':'commandcode'}]
        self.assertEqual(probe.select_entries(entries,['cmd-kimi-k3']),[entries[1]])
        self.assertIs(probe.select_entries(entries,None),entries)
        for aliases in [[],['k3','k3'],['k3-unknown'],['lain-auto-group-router']]:
            with self.subTest(aliases=aliases),self.assertRaises(ValueError):probe.select_entries(entries,aliases)

    def review_answer(self, guard='type(quantity) is not int or quantity <= 0'):
        return {'finding': 'Quantity is used without validation, accepting invalid invoice lines.',
                'cases': dict(probe.QUALITY['cases']),
                'fix': {'reject_if': guard, 'placement': 'before_subtotal', 'exception': 'ValueError'}}

    def frozen_plan(self):
        entries=[{'alias':'example','reasoningEffort':'none'}]
        return {'schemaVersion':2,'kind':'lain-realistic-model-qualification',
                'entries':entries,'maximumRequests':2,
                'executionContract':probe.execution_contract(entries,[],probe.DEFAULT_MODE)}

    def test_only_explicit_local_inference_endpoint(self):
        self.assertEqual(probe.local_base('http://127.0.0.1:18417/v1'),'http://127.0.0.1:18417/v1')
        for url in ['https://api.openai.com/v1','http://localhost:18417/v1',
                    'http://token@127.0.0.1:18417/v1','http://127.0.0.1:18417/v1?key=x',
                    'http://127.0.0.1:18417/v1#x','http://127.0.0.1:18417/v1/other']:
            with self.subTest(url=url),self.assertRaises(ValueError):probe.local_base(url)

    def inventory(self):
        models=[{'id':'same','provider':'a','accountIds':['account-a']},
                {'id':'same','provider':'b','accountIds':['account-b']}]
        catalog={'models':[{'slug':'same','default_reasoning_level':'low'},
                           {'slug':'other-same','default_reasoning_level':'none'},
                           {'slug':'lain-auto-group-router'}],
                 'lain_model_picker':{'groups':[{'id':'provider:a','models':['same']},
                                                 {'id':'provider:b','models':['other-same']}]}}
        return models,catalog,{'routeIds':['route-a','route-b']}

    def test_equal_upstream_names_remain_distinct_provider_routes(self):
        rows=probe.routes(*self.inventory())
        self.assertEqual([(r['alias'],r['accountId'],r['reasoningEffort']) for r in rows],
                         [('same','account-a','low'),('other-same','account-b','none')])

    def test_incomplete_catalog_and_ambiguous_bindings_refused(self):
        for mutation in ['missing','duplicate','accounts','profile','conflict']:
            models,catalog,profile=self.inventory()
            if mutation=='missing':catalog['models'].pop(1)
            elif mutation=='duplicate':catalog['models'][1]['slug']='same'
            elif mutation=='accounts':models[0]['accountIds'].append('other')
            elif mutation=='profile':profile['routeIds'].pop()
            else:catalog['lain_model_picker']['groups'][1]['models'].append('same')
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):
                probe.routes(models,catalog,profile)

    def test_sse_complete_without_waiting_for_eof_and_item_fallback(self):
        stream=io.BytesIO(b'data: {"type":"response.output_item.done","item":{"type":"function_call","name":"exec_command"}}\n\n'
                          b'data: {"type":"response.completed","response":{"status":"completed","usage":{"input_tokens":12}}}\n\n'
                          b'not read after terminal event')
        output,meta=probe.sse(stream)
        self.assertEqual(output[0]['name'],'exec_command')
        self.assertEqual(meta['sseEvents'],2)
        self.assertEqual(meta['usage'],{'input_tokens':12,'output_tokens':None,'total_tokens':None})
        self.assertTrue(stream.read().startswith(b'not read'))

    def test_stream_failure_and_incomplete_are_failures(self):
        for raw,category in [(b'data: {"type":"response.failed","response":{"error":{"code":"context_length_exceeded"}}}\n\n','context_limit'),
                             (b'data: [DONE]\n\n','incomplete_stream'),
                             (b'data: nope\n\n','invalid_sse')]:
            with self.subTest(category=category),self.assertRaises(probe.OutcomeError) as raised:
                probe.sse(io.BytesIO(raw))
            self.assertEqual(raised.exception.category,category)

    def test_diagnostics_are_classified_not_echoed(self):
        for message,category in [('The upstream context limit was exceeded.','context_limit'),
                                 ('this bound model accepts at most 128 transmitted top-level tools','tool_limit'),
                                 ("Invalid value for 'tools': maximum number of items is 128.",'tool_limit'),
                                 ('Unsupported value: \'none\' in reasoning.effort','reasoning_configuration'),
                                 ('provider-defined tool unsupported','tool_schema'),
                                 ('insufficient_quota','account_or_capacity')]:
            self.assertEqual(probe.classify(message.encode()),category)

    def test_bounded_diagnostics_remove_credentials_and_html(self):
        raw=json.dumps({'error':{'message':json.dumps({'error':{'code':'invalid_request',
            'message':'Synthetic Bearer private-token and person@example.com '+('x'*2000)}})}}).encode()
        result=probe.safe_error(raw,'private-token')
        self.assertEqual(result['code'],'invalid_request')
        self.assertNotIn('private-token',str(result))
        self.assertNotIn('person@example.com',str(result))
        self.assertLessEqual(len(result['message']),800)
        self.assertNotIn('message',probe.safe_error(b'<html>secret</html>','secret'))
        self.assertEqual(probe.safe_error(b'{"error":{"message":"context limit"}}','')['message'],
                         'context limit')
        nested={'error':{'message':json.dumps({'error':{'message':json.dumps({
            'error':{'message':"Invalid value for 'tools': maximum number of items is 128."}})}})}}
        self.assertEqual(probe.safe_error(json.dumps(nested).encode(),'token')['message'],
                         "Invalid value for 'tools': maximum number of items is 128.")
        with self.assertRaises(probe.OutcomeError) as error:
            probe.sse(io.BytesIO(b'data: {"type":"response.failed","response":{"error":{"message":"Bearer private-token","code":"invalid_request"}}}\n\n'),'private-token')
        self.assertNotIn('private-token',str(error.exception.details))
        self.assertEqual(error.exception.details['diagnostic']['code'],'invalid_request')

    def test_diagnostic_scenarios_preserve_schemas_reasoning_and_explicit_budgets(self):
        tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
        entry={'alias':'example','reasoningEffort':'none'}
        self.assertEqual(sum(s['maximumRequests'] for s in boundaries.SCENARIOS),6)
        for scenario in boundaries.SCENARIOS:
            body=boundaries.body_for(entry,tools,scenario)
            self.assertEqual(len(body['tools']),scenario['tools'])
            self.assertEqual(body['tools'][:7],tools)
            self.assertEqual(body['reasoning'],{'effort':'none'})
            if scenario['outputBudget'] is None:self.assertNotIn('max_output_tokens',body)
            else:self.assertEqual(body['max_output_tokens'],2048)
            if scenario['toolChoice']=='auto':self.assertEqual(body['tool_choice'],'auto')
        self.assertEqual(len(tools),7)

    def test_model_shell_text_is_never_executed(self):
        item={'type':'function_call','name':'exec_command','call_id':'call',
              'arguments':json.dumps({'cmd':'cat src/invoice.py; touch unwanted','workdir':probe.WORKDIR})}
        with patch.object(probe.subprocess,'run') as run,self.assertRaises(probe.OutcomeError):
            probe.tool_result(item,Path('/irrelevant'))
        run.assert_not_called()

    def test_successful_two_request_roundtrip_and_no_retry(self):
        entry={'alias':'alias','provider':'provider','accountId':'account','reasoningEffort':'low'}
        first=[{'type':'function_call','name':'exec_command','call_id':'call',
                'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}]
        final=[{'type':'message','content':[{'type':'output_text','text':json.dumps(self.review_answer())}]}]
        with tempfile.TemporaryDirectory() as temp:
            file=Path(temp)/'invoice.py';file.write_text(probe.FIXTURE)
            row={**entry,'calls':[]}
            with patch.object(probe,'exchange',side_effect=[(first,{}),(final,{})]) as exchange:
                probe.qualify(entry,[],None,None,file,row,lambda:None,threading.Event())
            self.assertEqual(exchange.call_count,2)
            for call in exchange.call_args_list:
                self.assertEqual(call.args[3]['tool_choice'],'auto')
                self.assertEqual(call.args[3]['max_output_tokens'],2048)
            self.assertEqual(exchange.call_args_list[1].args[3]['input'][-1]['type'],'function_call_output')
            self.assertEqual(row['state'],'passed')
            self.assertTrue(row['toolRoundTrip'])
            self.assertTrue(row['answerAssessment']['passed'])
            self.assertEqual(row['payloadMode'],probe.DEFAULT_MODE)
            self.assertNotIn('answer',row)
            self.assertEqual(file.read_text(),probe.FIXTURE)
            row={**entry,'calls':[]}
            with patch.object(probe,'exchange',side_effect=probe.OutcomeError('context_limit')) as exchange:
                probe.qualify(entry,[],None,None,file,row,lambda:None,threading.Event())
            self.assertEqual(exchange.call_count,1)
            self.assertEqual(row['state'],'failed')

    def test_context_budget_and_catalog_reasoning_are_explicit(self):
        context=probe.project_context()
        self.assertGreater(len(context.encode()),20000)
        entry={'alias':'model','reasoningEffort':'none'}
        body=probe.payload(entry,[],mode=probe.DEFAULT_MODE)
        self.assertEqual(body['reasoning']['effort'],'none')
        self.assertEqual(body['max_output_tokens'],2048)
        self.assertEqual(body['tool_choice'],'auto')
        self.assertFalse(body['parallel_tool_calls'])
        self.assertIn('Return one JSON object',body['input'][-1]['content'][0]['text'])
        self.assertNotIn('First call',body['input'][-1]['content'][0]['text'])
        entry['reasoningEffort']=None
        self.assertNotIn('reasoning',probe.payload(entry,[],mode=probe.DEFAULT_MODE))
        self.assertEqual(probe.payload(entry,[],[],mode=probe.DEFAULT_MODE)['tool_choice'],'auto')

    def test_legacy_diagnostic_payloads_are_not_silently_changed(self):
        # Hashes of the unmodified v1 helper, captured before the v2 change.
        entry={'alias':'example','reasoningEffort':'none'}
        for inputs,expected in [(None,'ff0a687732be8525957edf9f445bd5462d7a1752c5b23f96f823e1728286ad82'),
                                ([], 'c7f76ccb942f00e5ee23a24d95f091cb217608df08df9435c603bc1aaf247497')]:
            with self.subTest(inputs=inputs):
                self.assertEqual(probe.sha(probe.encoded(probe.payload(entry,[],inputs))),expected)
        for scenario in boundaries.SCENARIOS:
            body=boundaries.body_for(entry,[],scenario)
            if scenario['toolChoice']=='forced':
                self.assertEqual(body['tool_choice'],{'type':'function','name':'exec_command'})
                self.assertEqual(boundaries.body_for(entry,[],scenario,[])['tool_choice'],'none')

    def test_new_forced_control_is_explicit_and_uses_the_new_quality_task(self):
        entry={'alias':'example','reasoningEffort':'none'}
        body=probe.payload(entry,[],mode='forced-exec-v2')
        self.assertEqual(body['tool_choice'],{'type':'function','name':'exec_command'})
        self.assertIn('Return one JSON object',body['input'][-1]['content'][0]['text'])
        self.assertEqual(probe.payload(entry,[],[],mode='forced-exec-v2')['tool_choice'],'none')
        for mode in ('invalid',probe.LEGACY_MODE):
            with self.subTest(mode=mode),self.assertRaises(ValueError):
                probe.execution_contract([entry],[],mode)

    def test_quality_checks_cases_and_a_concrete_fix_without_executing_model_code(self):
        guards=['type(quantity) is not int or quantity <= 0',
                'not (type(quantity) is int and quantity > 0)',
                'not isinstance(quantity, int) or isinstance(quantity, bool) or quantity <= 0',
                'not (isinstance(quantity, int) and not isinstance(quantity, bool) and 0 < quantity)']
        with patch.object(probe.subprocess,'run') as run:
            for guard in guards:
                with self.subTest(guard=guard):
                    answer=json.dumps(self.review_answer(guard))
                    for text in (answer,'```json\n'+answer+'\n```'):
                        assessment=probe.assess_answer(text)
                        self.assertTrue(assessment['passed'],assessment)
                        self.assertTrue(all(assessment['guardCases'].values()))
                        self.assertNotIn(guard,str(assessment))
            run.assert_not_called()

    def test_wrong_fixes_do_not_pass_on_positive_integer_or_bool_keywords(self):
        guards=['quantity <= 0',
                'not isinstance(quantity, int) or quantity <= 0',
                'type(quantity) is not int or quantity < 0',
                'type(quantity) is not int or quantity <= 1',
                'type(quantity) is not int or quantity <= 0 or quantity == 7',
                'type(quantity) is not int or quantity <= 0 or quantity > 99999',
                'type(quantity) is bool or quantity <= 0',
                'type(quantity) is int and quantity <= 0',
                'True','False','int(quantity) <= 0']
        for guard in guards:
            with self.subTest(guard=guard):
                assessment=probe.assess_answer(json.dumps(self.review_answer(guard)))
                self.assertFalse(assessment['passed'],assessment)
                self.assertFalse(assessment['checks']['guard_boundaries'])

    def test_quality_requires_each_boundary_case_and_correct_fix_placement(self):
        for case in probe.QUALITY['cases']:
            answer=self.review_answer()
            answer['cases'][case]='reject' if case=='positive' else 'accept'
            with self.subTest(case=case):
                self.assertFalse(probe.assess_answer(json.dumps(answer))['passed'])
        for mutation in ('missing_case','missing_fix','missing_finding','after_subtotal','wrong_exception'):
            answer=self.review_answer()
            if mutation=='missing_case':del answer['cases']['boolean']
            elif mutation=='missing_fix':del answer['fix']
            elif mutation=='missing_finding':answer['finding']=' '
            elif mutation=='after_subtotal':answer['fix']['placement']='after_subtotal'
            else:answer['fix']['exception']='TypeError'
            with self.subTest(mutation=mutation):
                self.assertFalse(probe.assess_answer(json.dumps(answer))['passed'])

    def test_markers_nonsense_and_unsafe_or_unbounded_guards_need_review(self):
        for text in ('FINDING: quantity negative bool integer positive\nFIX: all good',
                     'HTTP 200', '[]', 'null', '{broken', '{}'):
            with self.subTest(text=text):self.assertFalse(probe.assess_answer(text)['passed'])
        for guard in ("__import__('os').system('touch unwanted')", 'quantity.__class__ is not int',
                      '[quantity][0] <= 0', 'quantity ** 999999999', 'str(quantity)',
                      'type(quantity) is not int or __import__("os")',
                      'not '*301+'True', ' or '.join(['True']*64)):
            with self.subTest(guard=guard),patch.object(probe.subprocess,'run') as run:
                assessment=probe.assess_answer(json.dumps(self.review_answer(guard)))
                self.assertFalse(assessment['passed'])
                self.assertFalse(assessment['checks']['guard_supported'])
                run.assert_not_called()

    def test_http_success_with_superficial_answer_is_not_quality_success(self):
        entry={'alias':'example','provider':'provider','accountId':'fixture','reasoningEffort':'low'}
        tool={'type':'function_call','name':'exec_command','call_id':'fixture',
              'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}
        final=[{'type':'message','content':[{'type':'output_text',
                 'text':'FINDING: Negative quantities are accepted.\nFIX: Validate a positive integer quantity.'}]}]
        row={**entry,'calls':[]}
        with patch.object(probe,'exchange',side_effect=[([tool],{'statusCode':200}),(final,{'statusCode':200})]),\
             patch.object(probe,'tool_result',return_value={'type':'function_call_output','call_id':'fixture','output':probe.FIXTURE}):
            probe.qualify(entry,[],None,None,Path('/not-read'),row,lambda:None,threading.Event())
        self.assertEqual(row['state'],'answer_review_needed')
        self.assertTrue(row['toolRoundTrip'])
        self.assertFalse(row['answerAssessment']['passed'])
        self.assertNotIn('answer',row)

    def test_automatic_choice_does_not_allow_unread_answers_or_extra_tool_rounds(self):
        entry={'alias':'example','provider':'provider','accountId':'fixture','reasoningEffort':'low'}
        tool={'type':'function_call','name':'exec_command','call_id':'fixture',
              'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}
        direct=[{'type':'message','content':[{'type':'output_text','text':json.dumps(self.review_answer())}]}]
        for responses,expected in [([(direct,{})],'expected_one_tool_call'),
                                   ([([tool],{}),([tool],{})],'unexpected_continuation_tool')]:
            row={**entry,'calls':[]}
            with self.subTest(expected=expected),\
                 patch.object(probe,'exchange',side_effect=responses) as exchange,\
                 patch.object(probe,'tool_result',return_value={'type':'function_call_output','call_id':'fixture','output':probe.FIXTURE}):
                probe.qualify(entry,[],None,None,Path('/not-read'),row,lambda:None,threading.Event())
                self.assertEqual(exchange.call_count,len(responses))
                self.assertEqual(row['state'],'failed')
                self.assertEqual(row['category'],expected)

    def test_contract_pins_mode_quality_source_and_both_request_templates(self):
        plan=self.frozen_plan()
        contract=plan['executionContract']
        self.assertEqual(probe.verify_execution_contract(plan,[]),probe.DEFAULT_MODE)
        self.assertEqual(contract['toolChoice'],{'initial':'auto','continuation':'auto'})
        self.assertEqual(contract['quality'],probe.QUALITY)
        self.assertEqual(contract['harnessSha256'],probe.sha(Path(probe.__file__).read_bytes()))
        self.assertNotEqual(contract['requests'][0]['firstSha256'],contract['requests'][0]['continuationTemplateSha256'])
        contract['quality']['cases']['zero']='accept'
        self.assertEqual(probe.QUALITY['cases']['zero'],'reject')

    def test_old_or_drifted_plan_execute_stops_before_inspection_auth_or_network(self):
        original=self.frozen_plan()
        for mutation in ('v1','missing_contract','mode','quality','quality_type','source','first','continuation','budget'):
            plan=copy.deepcopy(original)
            if mutation=='v1':plan['schemaVersion']=1
            elif mutation=='missing_contract':del plan['executionContract']
            elif mutation=='mode':plan['executionContract']['payloadMode']='forced-exec-v2'
            elif mutation=='quality':plan['executionContract']['quality']['cases']['zero']='accept'
            elif mutation=='quality_type':plan['executionContract']['quality']['guardCases'][0]['reject']=0
            elif mutation=='source':plan['executionContract']['harnessSha256']='0'*64
            elif mutation=='first':plan['executionContract']['requests'][0]['firstSha256']='0'*64
            elif mutation=='continuation':plan['executionContract']['requests'][0]['continuationTemplateSha256']='0'*64
            else:plan['maximumRequests']=3
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as temp:
                output=Path(temp);raw=probe.encoded(plan);(output/'plan.json').write_bytes(raw)
                with patch.object(probe,'cli') as cli,patch.object(probe.subprocess,'run') as run,\
                     patch.object(probe,'exchange') as exchange,self.assertRaises(ValueError):
                    probe.execute(output,probe.sha(raw),1)
                cli.assert_not_called();run.assert_not_called();exchange.assert_not_called()
                self.assertEqual((output/'plan.json').read_bytes(),raw)
                self.assertFalse((output/'report.json').exists())

    def test_current_payload_and_quality_drift_are_also_rejected(self):
        plan=self.frozen_plan();original_payload=probe.payload
        def changed_payload(*args,**kwargs):
            body=original_payload(*args,**kwargs)
            body['instructions']+=' Changed task.'
            return body
        with patch.object(probe,'payload',side_effect=changed_payload),self.assertRaises(ValueError):
            probe.verify_execution_contract(plan,[])
        changed_quality=copy.deepcopy(probe.QUALITY);changed_quality['cases']['zero']='accept'
        with patch.object(probe,'QUALITY',changed_quality),self.assertRaises(ValueError):
            probe.verify_execution_contract(plan,[])
        with self.assertRaises(ValueError):
            probe.verify_execution_contract(plan,[{'type':'function','name':'changed_fixture'}])

    def test_existing_evidence_and_plan_hash_mismatch_are_preserved_without_inspection(self):
        with tempfile.TemporaryDirectory() as temp:
            output=Path(temp);raw=probe.encoded(self.frozen_plan());(output/'plan.json').write_bytes(raw)
            report=b'{"original":"do not regrade"}\n';(output/'report.json').write_bytes(report)
            for expected in (probe.sha(raw),'0'*64):
                with self.subTest(expected=expected),patch.object(probe,'cli') as cli,\
                     patch.object(probe.subprocess,'run') as run,self.assertRaises(ValueError):
                    probe.execute(output,expected,1)
                cli.assert_not_called();run.assert_not_called()
                self.assertEqual((output/'report.json').read_bytes(),report)
                self.assertEqual((output/'plan.json').read_bytes(),raw)

    def test_execute_uses_only_the_frozen_mode_and_preserves_its_contract_in_report(self):
        # All runtime inspection, credential acquisition and qualification are
        # mocked. Only synthetic files under this temporary run are opened.
        tools=json.loads(probe.TOOLS_FILE.read_bytes())['tools']
        entry={'alias':'example','reasoningEffort':'low','provider':'fixture','accountId':'fixture'}
        status={'proxyReady':True,'revision':1,'appliedRevision':1,'activeProfileId':'fixture'}
        for mode in probe.MODES:
            selected={**entry,'alias':probe.COHERE_ALIAS,'provider':'cohere'} if mode == probe.COHERE_MODE else entry
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as temp:
                output=Path(temp);catalog=output/'catalog.json';catalog.write_bytes(b'{}')
                fixture=output/'fixture/src/invoice.py';fixture.parent.mkdir(parents=True)
                fixture.write_text(probe.FIXTURE)
                plan={'schemaVersion':2,'kind':'lain-realistic-model-qualification',
                      'entries':[selected],'maximumRequests':2,'executionContract':probe.execution_contract([selected],tools,mode),
                      'maximumOutputTokens':None if mode == probe.COHERE_MODE else 2048,
                      'revision':1,'profileId':'fixture','modelInventory':{},'limits':['synthetic unit fixture'],
                      'catalogPath':str(catalog),'catalogSha256':probe.sha(catalog.read_bytes()),
                      'clientToolFixtureSha256':probe.sha(probe.TOOLS_FILE.read_bytes()),
                      'fixtureSha256':probe.sha(fixture.read_bytes()),'endpoint':'http://127.0.0.1:18417/v1'}
                raw=probe.encoded(plan);(output/'plan.json').write_bytes(raw)
                def qualified(*args,**kwargs):
                    self.assertEqual(kwargs['mode'],mode)
                    args[5]['state']='passed'
                token_result=probe.subprocess.CompletedProcess([],0,stdout='synthetic-fixture-only\n')
                with patch.object(probe,'cli',side_effect=lambda name: status if name=='status' else {}),\
                     patch.object(probe.subprocess,'run',return_value=token_result),\
                     patch.object(probe,'qualify',side_effect=qualified) as qualify,\
                     patch.object(probe,'exchange') as exchange,patch('sys.stdout',new_callable=io.StringIO):
                    probe.execute(output,probe.sha(raw),1)
                qualify.assert_called_once();exchange.assert_not_called()
                report=json.loads((output/'report.json').read_bytes())
                self.assertEqual(report['schemaVersion'],2)
                self.assertEqual(report['executionContract'],plan['executionContract'])
                self.assertEqual(report['counts'],{'passed':1})
                self.assertEqual((output/'plan.json').read_bytes(),raw)


class GeneratedContextTests(unittest.TestCase):
    def entry(self, window=262144, maximum=8192):
        entry = {'alias':'glm-5.3', 'provider':'zai', 'accountId':'fixture-account', 'reasoningEffort':None}
        entry['contextCapacity'] = {**{k:entry[k] for k in ('alias','provider','accountId')},
            'contextWindowTokens':window, 'maximumOutputTokens':maximum,
            'source':'https://example.invalid/reviewed-route-documentation', 'checkedAt':'2026-10-26T00:00:00Z'}
        return entry

    def plan(self, mode, tools=None):
        entry = self.entry()
        return {'schemaVersion':2, 'kind':'lain-realistic-model-qualification', 'entries':[entry],
            'maximumRequests':2, 'executionContract':probe.execution_contract(
                [entry], tools or [], probe.DEFAULT_MODE, mode)}

    def answer(self):
        return {**LiveQualificationTests().review_answer(), 'context_evidence':probe.CONTEXT_EVIDENCE.copy()}

    def test_fixed_generated_bytes_and_sentinels_at_beginning_middle_end_and_file(self):
        for mode,size in [('k1-32k',128000),('k2-110k',440000)]:
            with self.subTest(mode=mode),patch.object(probe.subprocess,'run') as run:
                text = probe.generated_context(mode)
                self.assertEqual(text,probe.generated_context(mode))
                self.assertEqual(len(text.encode()),size)
                self.assertLessEqual(size,probe.CONTEXT_SPECS[mode]['maximumContextBytes'])
                self.assertLess(text.index(probe.CONTEXT_EVIDENCE['beginning']),100)
                self.assertLess(abs(text.index(probe.CONTEXT_EVIDENCE['middle'])-size//2),100)
                self.assertLess(size-text.index(probe.CONTEXT_EVIDENCE['end']),200)
                self.assertNotIn(probe.CONTEXT_EVIDENCE['file'],text)
                self.assertIn(probe.CONTEXT_EVIDENCE['file'],probe.fixture_text(mode))
                self.assertTrue(probe.fixture_text(mode).endswith(probe.FIXTURE))
                run.assert_not_called()

    def test_context_payload_auto_semantics_and_byte_caps(self):
        tools = json.loads(probe.TOOLS_FILE.read_bytes())['tools']
        for mode in ('k1-32k','k2-110k'):
            body = probe.payload(self.entry(),tools,mode=probe.DEFAULT_MODE,context_mode=mode)
            self.assertEqual(body['input'][0]['content'][0]['text'],probe.generated_context(mode))
            self.assertNotIn(probe.CONTEXT_EVIDENCE['file'],json.dumps(body))
            self.assertEqual(body['tool_choice'],'auto')
            self.assertEqual(body['max_output_tokens'],2048)
            probe.check_context_request(body,mode)
            follow = probe.payload(self.entry(),tools,body['input'],mode=probe.DEFAULT_MODE,context_mode=mode)
            self.assertEqual(follow['input'],body['input'])
            self.assertEqual(follow['tool_choice'],'auto')
            probe.check_context_request(follow,mode,continuation=True)
            for continuation,key in [(False,'maximumInitialRequestBytes'),(True,'maximumContinuationRequestBytes')]:
                oversized = {'input':'x'*probe.CONTEXT_SPECS[mode][key]}
                with self.assertRaises(probe.OutcomeError):
                    probe.check_context_request(oversized,mode,continuation=continuation)

    def test_known_capacity_rejection_and_unknown_limits_are_not_invented(self):
        for window,output in [(32000,8192),(262144,2047)]:
            with self.subTest(window=window,output=output),self.assertRaises(ValueError):
                probe.context_contract([self.entry(window,output)],'k1-32k')
        c = probe.context_contract([self.entry(None,None)],'k2-110k')
        self.assertEqual(c['capacityStatus'],'unknown')
        self.assertIsNone(c['capacity']['contextWindowTokens'])
        self.assertEqual(c['outputReserveTokens'],2048)
        self.assertEqual(probe.context_contract([self.entry(34048,2048)],'k1-32k')['capacityStatus'],
                         'target_fits_not_tokenized')
        for field,value in [('alias','wrong'),('provider','wrong'),('accountId','wrong'),
                            ('contextWindowTokens',True),('maximumOutputTokens',-1),
                            ('source','file:///private'),('checkedAt','unknown')]:
            entry=self.entry();entry['contextCapacity'][field]=value
            with self.subTest(field=field),self.assertRaises(ValueError):
                probe.context_contract([entry],'k1-32k')
        with self.assertRaises(ValueError):probe.context_contract([self.entry(),self.entry()],'k1-32k')
        with self.assertRaises(ValueError):probe.execution_contract([self.entry()],[],'forced-exec-v2','k1-32k')

    def test_semantic_and_all_four_evidence_checks_are_jointly_required(self):
        answer = self.answer()
        self.assertTrue(probe.assess_answer(json.dumps(answer),'k1-32k')['passed'])
        for key in probe.CONTEXT_EVIDENCE:
            changed=copy.deepcopy(answer);changed['context_evidence'][key]='wrong'
            self.assertFalse(probe.assess_answer(json.dumps(changed),'k1-32k')['passed'])
        answer['fix']['reject_if']='quantity <= 0'
        assessment=probe.assess_answer(json.dumps(answer),'k2-110k')
        self.assertTrue(assessment['checks']['context_evidence'])
        self.assertFalse(assessment['passed'])
        answer=self.answer();answer['fix']['reject_if']='__import__("os").system("false")'
        with patch.object(probe.subprocess,'run') as run:
            self.assertFalse(probe.assess_answer(json.dumps(answer),'k2-110k')['passed'])
            run.assert_not_called()

    def test_context_contract_drift_fails_before_cli_auth_or_network(self):
        for mode in ('k1-32k','k2-110k'):
            original=self.plan(mode)
            self.assertEqual(probe.verify_execution_contract(original,[]),probe.DEFAULT_MODE)
            for field in ('contextSha256','fixtureSha256','targetInputTokens','maximumInitialRequestBytes',
                          'capacityStatus','maximumReplayBytes','requiredEvidence'):
                plan=copy.deepcopy(original);plan['executionContract']['context'][field]='changed'
                with tempfile.TemporaryDirectory() as temp:
                    output=Path(temp);raw=probe.encoded(plan);(output/'plan.json').write_bytes(raw)
                    with patch.object(probe,'cli') as cli,patch.object(probe.subprocess,'run') as run,\
                         patch.object(probe,'exchange') as exchange,self.assertRaises(ValueError):
                        probe.execute(output,probe.sha(raw),1)
                    cli.assert_not_called();run.assert_not_called();exchange.assert_not_called()
            changed=copy.deepcopy(original);changed['entries'][0]['contextCapacity']['contextWindowTokens']=123
            with self.assertRaises(ValueError):probe.verify_execution_contract(changed,[])
            with patch.object(probe,'generated_context',return_value='manual context'),self.assertRaises(ValueError):
                probe.verify_execution_contract(original,[])

    def test_hand_edited_generated_input_rejected_before_runtime_inspection(self):
        for changed in ('context','file'):
            with tempfile.TemporaryDirectory() as temp:
                output=Path(temp);plan=self.plan('k1-32k');raw=probe.encoded(plan)
                (output/'plan.json').write_bytes(raw)
                (output/'generated-context.txt').write_text(probe.generated_context('k1-32k')+('edit' if changed=='context' else ''))
                file=output/'fixture/src/invoice.py';file.parent.mkdir(parents=True)
                file.write_text(probe.fixture_text('k1-32k')+('edit' if changed=='file' else ''))
                with patch.object(probe,'TOOLS_FILE',output/'tools.json'):
                    (output/'tools.json').write_text('{"tools":[]}')
                    with patch.object(probe,'cli') as cli,patch.object(probe.subprocess,'run') as run,\
                         patch.object(probe,'exchange') as exchange,self.assertRaises(ValueError):
                        probe.execute(output,probe.sha(raw),1)
                    cli.assert_not_called();run.assert_not_called();exchange.assert_not_called()

    def roundtrip(self, mode, meta, answer=None, first_padding=''):
        entry=self.entry();row={**entry,'calls':[]}
        tool={'type':'function_call','name':'exec_command','call_id':'fixture',
              'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}
        if first_padding:tool['padding']=first_padding
        final=[{'type':'message','content':[{'type':'output_text','text':json.dumps(answer or self.answer())}]}]
        with tempfile.TemporaryDirectory() as temp:
            file=Path(temp)/'invoice.py';file.write_text(probe.fixture_text(mode))
            with patch.object(probe,'exchange',side_effect=[([tool],meta),(final,meta)]) as exchange:
                probe.qualify(entry,[],None,None,file,row,lambda:None,threading.Event(),context_mode=mode)
            return row,exchange.call_args_list

    def test_two_calls_replay_same_context_and_no_unobserved_token_success(self):
        for mode,target in [('k1-32k',32000),('k2-110k',110000)]:
            row,calls=self.roundtrip(mode,{'usage':{'input_tokens':target}})
            self.assertEqual(row['state'],'passed');self.assertEqual(len(calls),2)
            self.assertTrue(row['contextObservation']['targetReached'])
            first,second=[c.args[3] for c in calls]
            self.assertEqual(first['input'],second['input'][:2])
            self.assertEqual(second['input'][-1]['type'],'function_call_output')
            self.assertIn(probe.CONTEXT_EVIDENCE['file'],second['input'][-1]['output'])
            self.assertEqual(first['tool_choice'],second['tool_choice'])
            self.assertEqual(row['answerAssessment']['passed'],True)
        for meta,state in [({},'context_usage_unknown'),({'usage':{'input_tokens':7000}},'context_target_not_reached')]:
            row,calls=self.roundtrip('k1-32k',meta)
            self.assertEqual(len(calls),2);self.assertEqual(row['state'],state)
            self.assertTrue(row['answerAssessment']['passed'])

    def test_observed_window_overflow_and_replay_overflow_stop_after_first_call(self):
        for meta,padding,category in [({'usage':{'input_tokens':262144}},'','context_window_exceeded'),
                                      ({},'x'*probe.MAX_REPLAY_BYTES,'context_replay_byte_limit')]:
            row,calls=self.roundtrip('k1-32k',meta,first_padding=padding)
            self.assertEqual(len(calls),1);self.assertEqual(row['category'],category)
            self.assertEqual(row['state'],'failed')

    def test_generated_prepare_is_frozen_with_mocked_public_inspection_only(self):
        entry=self.entry();record=entry.pop('contextCapacity')
        status={'proxyReady':True,'revision':1,'appliedRevision':1,'activeProfileId':'fixture'}
        tools={'tools':[{'type':'function','name':str(i)} for i in range(7)],'provenance':{}}
        with tempfile.TemporaryDirectory() as temp:
            base=Path(temp);(base/'.codex').mkdir();catalog=base/'catalog.json';catalog.write_text('{}')
            (base/'.codex/config.toml').write_text('model_provider="lain"\nmodel_catalog_json='+json.dumps(str(catalog))+
                '\n[model_providers.lain]\nbase_url="http://127.0.0.1:18417/v1"\n')
            source=base/'capacity.json';source.write_text(json.dumps(record))
            toolfile=base/'tools.json';toolfile.write_text(json.dumps(tools))
            for mode in ('k1-32k','k2-110k'):
                output=base/mode
                with patch.object(Path,'home',return_value=base),patch.object(probe,'TOOLS_FILE',toolfile),\
                     patch.object(probe,'cli',side_effect=[status,{'models':[]},[{'id':'fixture'}]]) as cli,\
                     patch.object(probe,'routes',return_value=[entry.copy()]),\
                     patch.object(probe.subprocess,'run') as run,patch.object(probe,'exchange') as exchange:
                    plan=probe.prepare(output,aliases=['glm-5.3'],context_mode=mode,capacity_path=source)
                    self.assertEqual([c.args for c in cli.call_args_list],[('status',),('models',),('profiles',)])
                    run.assert_not_called();exchange.assert_not_called()
                    self.assertEqual(probe.verify_execution_contract(plan,tools['tools']),probe.DEFAULT_MODE)
                self.assertEqual((output/'generated-context.txt').read_text(),probe.generated_context(mode))
                self.assertEqual(plan['maximumRequests'],2)
                self.assertEqual(plan['executionContract']['context']['capacity'],record)
                self.assertEqual(plan['executionContract']['context']['toolSummary']['namespaceMembers'],0)
                # Execute the frozen long-context plan end to end, with every
                # CLI/auth/HTTP boundary synthetic and no real process launched.
                tool={'type':'function_call','name':'exec_command','call_id':'fixture',
                      'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}
                final=[{'type':'message','content':[{'type':'output_text','text':json.dumps(self.answer())}]}]
                meta={'usage':{'input_tokens':probe.CONTEXT_SPECS[mode]['targetInputTokens']}}
                def synthetic_process(argv,**kwargs):
                    if argv[:3] == ['lain','auth','token']:
                        return probe.subprocess.CompletedProcess(argv,0,stdout='synthetic-local-token\n')
                    self.assertEqual(argv,['/usr/bin/cat',str(output/'fixture/src/invoice.py')])
                    return probe.subprocess.CompletedProcess(argv,0,stdout=probe.fixture_text(mode).encode())
                with patch.object(probe,'TOOLS_FILE',toolfile),\
                     patch.object(probe,'cli',side_effect=lambda name:status if name=='status' else {'models':[]}),\
                     patch.object(probe.subprocess,'run',side_effect=synthetic_process) as processes,\
                     patch.object(probe,'exchange',side_effect=[([tool],meta),(final,meta)]) as exchange,\
                     patch('sys.stdout',new_callable=io.StringIO):
                    probe.execute(output,probe.sha((output/'plan.json').read_bytes()),1)
                self.assertEqual(processes.call_count,2)
                self.assertEqual(exchange.call_count,2)
                report=json.loads((output/'report.json').read_bytes())
                self.assertEqual(report['counts'],{'passed':1})
                self.assertEqual(report['inferenceHttpRequests'],2)
                self.assertEqual(report['executionContract'],plan['executionContract'])
                self.assertEqual(report['rows'][0]['contextContract']['mode'],mode)



class DirectCohereTests(unittest.TestCase):
    def entry(self):
        return {'alias':probe.COHERE_ALIAS,'provider':'cohere','accountId':'synthetic-account','reasoningEffort':'none'}

    def tools(self):
        return json.loads(probe.TOOLS_FILE.read_bytes())['tools']

    def plan(self):
        return {'schemaVersion':2,'kind':'lain-realistic-model-qualification',
                'entries':[self.entry()],'maximumRequests':2,'maximumOutputTokens':None,
                'executionContract':probe.execution_contract([self.entry()],self.tools(),probe.COHERE_MODE)}

    def test_exact_cohere_controls_both_steps_without_changing_general_modes(self):
        for inputs in (None,[]):
            body=probe.payload(self.entry(),self.tools(),inputs,mode=probe.COHERE_MODE)
            self.assertEqual(body['tool_choice'],'auto')
            self.assertIs(body['parallel_tool_calls'],True)
            self.assertNotIn('max_output_tokens',body)
            self.assertNotIn('max_tokens',body)
            self.assertEqual(body['tools'],self.tools())
            self.assertEqual(body['reasoning'],{'effort':'none'})
            if inputs is None:self.assertIn('reject_if',body['input'][-1]['content'][0]['text'])
        for mode in (probe.DEFAULT_MODE,'forced-exec-v2',probe.LEGACY_MODE):
            body=probe.payload(self.entry(),self.tools(),mode=mode)
            self.assertIs(body['parallel_tool_calls'],False)
            self.assertEqual(body['max_output_tokens'],2048)
        contract=self.plan()['executionContract']
        self.assertEqual(contract['toolChoice'],{'initial':'auto','continuation':'auto'})
        self.assertEqual(contract['requestControls']['outputBudget'],'omitted_unknown')
        self.assertIsNone(contract['requestControls']['maximumOutputTokens'])
        self.assertEqual(contract['requestControls']['maximumResponseBytes'],probe.MAX_BODY)

    def test_scope_and_native_schema_boundaries(self):
        entry=self.entry();tools=self.tools()
        for changed in ({**entry,'provider':'commandcode'},{**entry,'provider':'Cohere'},
                        {**entry,'alias':'other'},{**entry,'alias':probe.COHERE_ALIAS+' '}):
            with self.subTest(entry=changed),self.assertRaises(ValueError):
                probe.payload(changed,tools,mode=probe.COHERE_MODE)
        for entries in ([],[entry,entry]):
            with self.assertRaises(ValueError):probe.execution_contract(entries,tools,probe.COHERE_MODE)
        for mode in ('k1-32k','k2-110k'):
            with self.assertRaises(ValueError):probe.execution_contract([entry],tools,probe.COHERE_MODE,mode)
        changed=copy.deepcopy(tools);changed[0]['description']='Changed schema'
        for other in ([],tools[:6],tools+[tools[0]],changed,[{'type':'namespace','tools':tools}]):
            with self.assertRaises(ValueError):probe.payload(entry,other,mode=probe.COHERE_MODE)
        for aliases,context in [(None,'k0'),(['other'],'k0'),([probe.COHERE_ALIAS]*2,'k0'),
                                ([probe.COHERE_ALIAS],'k1-32k')]:
            with patch.object(probe,'cli') as cli,self.assertRaises(ValueError):
                probe.prepare(Path('/never-created'),probe.COHERE_MODE,aliases,context)
            cli.assert_not_called()

    def test_contract_drift_and_legacy_rejected_before_cli_auth_or_http(self):
        original=self.plan()
        for mutation in ('legacy','source','mode','provider','alias','count','output','missing_output',
                         'controls','response_bound','first','continuation','quality'):
            plan=copy.deepcopy(original);contract=plan['executionContract']
            if mutation=='legacy':plan['schemaVersion']=1
            elif mutation=='source':contract['harnessSha256']='0'*64
            elif mutation=='mode':contract['payloadMode']=probe.DEFAULT_MODE
            elif mutation in ('provider','alias'):plan['entries'][0][mutation]='other'
            elif mutation=='count':plan['maximumRequests']=3
            elif mutation=='output':plan['maximumOutputTokens']=2048
            elif mutation=='missing_output':del plan['maximumOutputTokens']
            elif mutation=='controls':contract['requestControls']['parallelToolCalls']=False
            elif mutation=='response_bound':contract['requestControls']['maximumResponseBytes']+=1
            elif mutation=='first':contract['requests'][0]['firstSha256']='0'*64
            elif mutation=='continuation':contract['requests'][0]['continuationTemplateSha256']='0'*64
            else:contract['quality']['cases']['zero']='accept'
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as temp:
                output=Path(temp);raw=probe.encoded(plan);(output/'plan.json').write_bytes(raw)
                with patch.object(probe,'cli') as cli,patch.object(probe.subprocess,'run') as run,\
                     patch.object(probe,'build_opener') as network,self.assertRaises(ValueError):
                    probe.execute(output,probe.sha(raw),1)
                cli.assert_not_called();run.assert_not_called();network.assert_not_called()
        plan=self.plan();original_payload=probe.payload
        def unsupported_field(*args,**kwargs):
            body=original_payload(*args,**kwargs);body['max_output_tokens']=2048;return body
        with patch.object(probe,'payload',side_effect=unsupported_field),self.assertRaises(ValueError):
            probe.verify_execution_contract(plan,self.tools())

    def test_direct_responses_prepare_execute_and_semantic_failure_without_retry(self):
        status={'proxyReady':True,'revision':1,'appliedRevision':1,'activeProfileId':'fixture'}
        for guard,expected in [('type(quantity) is not int or quantity <= 0','passed'),
                               ('type(quantity) is not int','answer_review_needed')]:
            with self.subTest(guard=guard),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);(root/'.codex').mkdir();catalog=root/'catalog.json';catalog.write_text('{}')
                (root/'.codex/config.toml').write_text('model_provider="lain"\nmodel_catalog_json='+json.dumps(str(catalog))+
                    '\n[model_providers.lain]\nbase_url="http://127.0.0.1:18417/v1"\n')
                output=root/'run'
                with patch.object(Path,'home',return_value=root),\
                     patch.object(probe,'cli',side_effect=[status,{'models':[]},[{'id':'fixture'}]]),\
                     patch.object(probe,'routes',return_value=[self.entry()]),\
                     patch.object(probe.subprocess,'run') as processes,patch.object(probe,'build_opener') as network:
                    plan=probe.prepare(output,probe.COHERE_MODE,[probe.COHERE_ALIAS])
                    processes.assert_not_called();network.assert_not_called()
                self.assertIsNone(plan['maximumOutputTokens'])
                self.assertEqual(plan['maximumRequests'],2)
                self.assertFalse(any('explicitly 2048' in limit for limit in plan['limits']))
                tool={'type':'function_call','name':'exec_command','call_id':'fixture',
                      'arguments':json.dumps({'cmd':probe.COMMAND,'workdir':probe.WORKDIR})}
                final={'type':'message','content':[{'type':'output_text',
                       'text':json.dumps(LiveQualificationTests().review_answer(guard))}]}
                captured=[]
                def local_response(request,**kwargs):
                    self.assertEqual(request.full_url,'http://127.0.0.1:18417/v1/responses')
                    self.assertEqual(kwargs['timeout'],probe.SOCKET_TIMEOUT)
                    body=json.loads(request.data);captured.append(body)
                    self.assertEqual(body['model'],probe.COHERE_ALIAS)
                    self.assertEqual(body['tool_choice'],'auto');self.assertIs(body['parallel_tool_calls'],True)
                    self.assertNotIn('max_output_tokens',body)
                    response=io.BytesIO(b'data: '+json.dumps({'type':'response.completed','response':{
                        'status':'completed','output':[tool if len(captured)==1 else final]}}).encode()+b'\n\n')
                    response.status=200;response.headers={'Content-Type':'text/event-stream'};return response
                def local_process(argv,**kwargs):
                    if argv[:3]==['lain','auth','token']:
                        return probe.subprocess.CompletedProcess(argv,0,stdout='synthetic-local-only\n')
                    self.assertEqual(argv,['/usr/bin/cat',str(output/'fixture/src/invoice.py')])
                    return probe.subprocess.CompletedProcess(argv,0,stdout=probe.FIXTURE.encode())
                with patch.object(probe,'cli',side_effect=lambda name:status if name=='status' else {'models':[]}),\
                     patch.object(probe.subprocess,'run',side_effect=local_process),\
                     patch.object(probe,'build_opener') as opener,patch('sys.stdout',new_callable=io.StringIO):
                    opener.return_value.open.side_effect=local_response
                    probe.execute(output,probe.sha((output/'plan.json').read_bytes()),1)
                report=json.loads((output/'report.json').read_bytes())
                self.assertEqual(len(captured),2);self.assertEqual(report['inferenceHttpRequests'],2)
                self.assertEqual(report['counts'],{expected:1})
                self.assertEqual(report['executionContract'],plan['executionContract'])
                self.assertEqual(captured[0]['input'],captured[1]['input'][:2])
                self.assertEqual(captured[1]['input'][-1]['type'],'function_call_output')
                for call in report['rows'][0]['calls']:
                    self.assertIsNone(call['maxOutputTokens'])
                    self.assertEqual(call['usage'],{'input_tokens':None,'output_tokens':None,'total_tokens':None})
                self.assertEqual(report['rows'][0]['calls'][0]['requestSha256'],
                                 plan['executionContract']['requests'][0]['firstSha256'])

    def test_first_failure_stops_without_continuation_or_retry(self):
        row={**self.entry(),'calls':[]}
        with patch.object(probe,'exchange',side_effect=probe.OutcomeError('upstream_rejected')) as request,\
             patch.object(probe.subprocess,'run') as process:
            probe.qualify(self.entry(),self.tools(),None,None,Path('/never-read'),row,
                          lambda:None,threading.Event(),mode=probe.COHERE_MODE)
        self.assertEqual(request.call_count,1);process.assert_not_called()
        self.assertEqual(row['state'],'failed')

    def test_omitted_output_budget_still_has_response_byte_event_time_bounds(self):
        with self.assertRaises(probe.OutcomeError) as raised:
            probe.sse(io.BytesIO(b'x'*(probe.MAX_BODY+1)))
        self.assertEqual(raised.exception.category,'response_size')
        with patch.object(probe,'MAX_EVENTS',2),self.assertRaises(probe.OutcomeError) as raised:
            probe.sse(io.BytesIO(b'data: {"type":"ping"}\n\n'*4))
        self.assertEqual(raised.exception.category,'stream_deadline')
        with patch.object(probe.time,'monotonic',side_effect=[0,probe.MAX_STREAM_SECONDS+1]),\
             self.assertRaises(probe.OutcomeError) as raised:
            probe.sse(io.BytesIO(b''))
        self.assertEqual(raised.exception.category,'stream_deadline')


if __name__=='__main__':unittest.main()
