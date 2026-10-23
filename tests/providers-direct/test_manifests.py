#!/usr/bin/env python3
"""Secret-free direct API contract invariants; no network or SDK required."""
import json
from datetime import datetime, timezone
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'src/Lain.Host/Providers/Catalog'


class DirectManifestTests(unittest.TestCase):
    def setUp(self):
        self.providers = {
            name: json.loads((CATALOG / f'{name}.json').read_text())
            for name in ('gemini-api', 'groq', 'mistral')
        }

    def test_unknown_account_state_and_costs_fail_closed(self):
        for name, provider in self.providers.items():
            with self.subTest(name=name):
                self.assertEqual(provider['id'], name)
                self.assertEqual(provider['liveVerification'], 'not_tested')
                self.assertEqual(provider['quotaSupport'], 'unsupported')
                self.assertEqual(provider['credentials'][0]['id'], 'apiKey')
                self.assertTrue(provider['credentials'][0]['secret'])
                self.assertTrue(provider['setupSteps'])
                self.assertTrue(provider['restrictions'])
                self.assertTrue({'free', 'payg'}.issubset({p['kind'] for p in provider['billingPlans']}))
                for plan in provider['billingPlans']:
                    self.assertIsNone(plan['includedAmount'])
                    self.assertNotIn('recurringPrice', plan)
                    for limit in plan['limits']:
                        self.assertIsNone(limit['value'])
                for route in provider['routes']:
                    self.assertEqual(route['pricingClass'], 'unknown')
                    self.assertFalse(route['freeOnlyEligible'])
                    self.assertIsNone(route['freeOnlyEnforcement'])
                    self.assertTrue(route['restrictions'])
                    self.assertTrue(set(route['billingPlanIds']).issubset({p['id'] for p in provider['billingPlans']}))

    def test_official_origins_and_route_identity(self):
        endpoints = {
            'gemini-api': ('https://generativelanguage.googleapis.com/v1beta/openai/', 'Google', 'Google'),
            'groq': ('https://api.groq.com/openai/v1', 'OpenAI', 'Groq'),
            'mistral': ('https://api.mistral.ai/v1', 'Mistral', 'Mistral'),
        }
        seen = set()
        for name, provider in self.providers.items():
            for route in provider['routes']:
                self.assertNotIn(route['id'], seen)
                seen.add(route['id'])
                self.assertEqual(route['baseUrl'], endpoints[name][0])
                self.assertEqual(route['manufacturer'], endpoints[name][1])
                self.assertEqual(route['inferenceProvider'], endpoints[name][2])
                self.assertIsNone(route['gateway'])
                self.assertEqual(route['protocol'], 'openai-chat')
                self.assertTrue(route['sources'])
                self.assertTrue(route['capabilities']['streaming'])
                self.assertTrue(route['capabilities']['tools'])
                for source in provider['sources'] + route['sources']:
                    self.assertTrue(source['url'].startswith('https://'))
                    checked = datetime.fromisoformat(source['checkedAt'].replace('Z', '+00:00'))
                    self.assertIsNotNone(checked.tzinfo)
                    self.assertLessEqual(checked, datetime.now(timezone.utc))

    def test_concrete_models_do_not_inherit_unverified_capabilities(self):
        gemini = self.providers['gemini-api']
        self.assertEqual({r['model'] for r in gemini['routes']}, {'gemini-3.5-flash-lite', 'gemini-3.8-flash'})
        for route in gemini['routes']:
            self.assertEqual(route['capabilities']['contextWindow'], 1048576)
            self.assertEqual(route['capabilities']['maximumOutput'], 65536)
        flash = next(r for r in gemini['routes'] if r['model'] == 'gemini-3.8-flash')
        self.assertEqual(flash['capabilities']['reasoningModes'], ['low', 'medium', 'high'])
        self.assertTrue(all(p['scope'] == 'project' for p in gemini['billingPlans']))
        groq = self.providers['groq']
        for route in groq['routes']:
            self.assertEqual(route['capabilities']['contextWindow'], 131072)
            self.assertEqual(route['capabilities']['maximumOutput'], 65536)
            self.assertFalse(route['capabilities']['images'])
        self.assertTrue(all(p['scope'] == 'organization' for p in groq['billingPlans']))
        mistral = self.providers['mistral']
        self.assertEqual({p['kind'] for p in mistral['billingPlans']}, {'free', 'subscription', 'payg'})
        route = mistral['routes'][0]
        self.assertEqual(route['model'], 'mistral-small-2603')
        self.assertIsNone(route['capabilities']['contextWindow'])  # 256k is rounded documentation.
        self.assertIsNone(route['capabilities']['maximumOutput'])
        self.assertIsNone(route['capabilities']['images'])
        self.assertIsNone(route['capabilities']['reasoningModes'])


if __name__ == '__main__':
    unittest.main()
