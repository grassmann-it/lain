"""Acceptance checks for externally observed SQLite replay evidence."""
import copy
import unittest

from upgrade_gate import validate_consumer


class ConsumerEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.batch = {"highWatermark": 600, "events": [None] * 600}
        self.result = {"cursor": 600, "complete": True, "accounts": 2, "models": 1,
                       "totals": {"requests": 200, "attempts": 200, "errors": 0, "unknownUsageAttempts": 0,
                                  "tokens": {"input": 2200, "output": 1400, "cached": None, "reasoning": None, "total": 3600}}}

    def test_replayed_complete_source_is_accepted(self):
        validate_consumer(self.result, self.batch)

    def test_replay_overcount_and_unknown_zero_are_rejected(self):
        for mutation in [lambda result: result["totals"].update(requests=2000),
                         lambda result: result["totals"]["tokens"].update(cached=0),
                         lambda result: result.update(complete=False),
                         lambda result: result.update(cursor=400),
                         lambda result: result.update(accounts=1)]:
            result = copy.deepcopy(self.result)
            mutation(result)
            with self.assertRaises(AssertionError):
                validate_consumer(result, self.batch)


if __name__ == "__main__":
    unittest.main()
