"""No auth, native process or provider calls: frozen candidate execution gates."""
import copy
import contextlib
import io
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import cohere_candidate as candidate


class CohereCandidateContractTests(unittest.TestCase):
    def test_prepare_freezes_sources_both_payloads_and_two_call_budget(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'source';source.mkdir()
            entry={'alias':candidate.MODEL,'accountId':'synthetic-account'}
            (source/'plan.json').write_text(json.dumps({'entries':[entry]}))
            core=root/'core';core.write_bytes(b'synthetic-not-an-executable')
            args=SimpleNamespace(source_run=source,core=core,output=root/'out')
            with patch.object(candidate.boundaries,'verify_binding'), \
                 patch.object(candidate.subprocess,'run') as auth, \
                 contextlib.redirect_stdout(io.StringIO()):
                candidate.prepare(args)
            auth.assert_not_called()
            plan=json.loads((args.output/'plan.json').read_text())
            self.assertEqual(plan['schemaVersion'],2)
            self.assertEqual(plan['sourcesSha256'],candidate.source_contract())
            self.assertEqual(plan['maximumExistingGatewayRequests'],2)
            tools=json.loads(candidate.probe.TOOLS_FILE.read_bytes())['tools']
            for key,inputs in [('firstRequestSha256',None),('continuationTemplateSha256',[])]:
                self.assertEqual(plan[key],candidate.probe.sha(candidate.probe.encoded(candidate.body(tools,inputs))))

    def test_legacy_and_drifting_contracts_stop_before_auth_or_inference(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);core=root/'core';core.write_bytes(b'synthetic-not-an-executable')
            tools=json.loads(candidate.probe.TOOLS_FILE.read_bytes())['tools']
            original={'schemaVersion':2,'sourcesSha256':candidate.source_contract(),
                'maximumExistingGatewayRequests':2,'core':str(core),
                'coreSha256':candidate.probe.sha(core.read_bytes()),'binding':{},
                'nativeToolsSha256':candidate.probe.sha(candidate.probe.TOOLS_FILE.read_bytes()),
                'firstRequestSha256':candidate.probe.sha(candidate.probe.encoded(candidate.body(tools))),
                'continuationTemplateSha256':candidate.probe.sha(candidate.probe.encoded(candidate.body(tools,[])))}
            for mutation in ('legacy','source','budget','first','continuation'):
                plan=copy.deepcopy(original)
                if mutation=='legacy':plan['schemaVersion']=1
                elif mutation=='source':plan['sourcesSha256']['live/cohere_candidate.py']='0'*64
                elif mutation=='budget':plan['maximumExistingGatewayRequests']=3
                elif mutation=='first':plan['firstRequestSha256']='0'*64
                else:plan['continuationTemplateSha256']='0'*64
                raw=candidate.probe.encoded(plan);(root/'plan.json').write_bytes(raw)
                args=SimpleNamespace(output=root,expect_plan_sha256=candidate.probe.sha(raw))
                with self.subTest(mutation=mutation), \
                     patch.object(candidate.boundaries,'verify_binding'), \
                     patch.object(candidate.subprocess,'run') as auth, \
                     patch.object(candidate.probe,'exchange') as exchange, \
                     self.assertRaises(ValueError):
                    candidate.execute(args)
                auth.assert_not_called();exchange.assert_not_called()
                self.assertFalse((root/'report.json').exists())
                self.assertEqual((root/'plan.json').read_bytes(),raw)


if __name__=='__main__':unittest.main()
