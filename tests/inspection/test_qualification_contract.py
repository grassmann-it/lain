"""Pure synthetic tests for the qualification validator; launches no processes."""
from copy import deepcopy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from qualify import assert_complete


def events():
    identity = dict(sourceId="source", requestId="request", attemptId="attempt", accountId="account",
                    credentialId="credential", routeId="route", model="fixture")
    unknown = dict(inputTokens=None, outputTokens=None, totalTokens=None, cacheReadTokens=None,
                   cacheCreationTokens=None, reasoningTokens=None, outcome=None, statusCode=None,
                   usageReported=False, latencyMs=None, ttftMs=None)
    measurements = dict(source="core_monotonic_v1", totalDurationMs=10, firstOutputLatencyMs=5,
                        finishedAt="2026-10-24T00:00:00Z", firstOutputAt="2026-10-24T00:00:00Z")
    return [
        dict(**identity, **unknown, kind="attempt_started", sequence=1, eventId="one"),
        {**identity, **unknown, "kind": "usage", "sequence": 2, "eventId": "two", "inputTokens": 11,
         "outputTokens": 7, "totalTokens": 18, "outcome": "success", "statusCode": 200, "usageReported": True},
        dict(**identity, **unknown, kind="attempt_finished", sequence=3, eventId="three",
             completionState="success", measurements=measurements)
    ]


class QualificationContract(unittest.TestCase):
    def test_known_reporter_has_one_usage_and_metadata_completion(self):
        starts, usages = assert_complete(events(), 1)
        self.assertEqual((len(starts), len(usages), usages[0]["totalTokens"]), (1, 1, 18))

    def test_unknown_reporter_has_one_combined_terminal_fact(self):
        rows = events()
        final = {**rows[-1], "kind": "usage", "sequence": 2}
        final["outcome"] = "success"
        assert_complete([rows[0], final], 1)

    def test_wrapper_cancel_does_not_rewrite_earlier_successful_usage(self):
        rows = events()
        rows[-1]["completionState"] = "canceled"
        assert_complete(rows, 1)

    def test_missing_completion_is_not_accepted(self):
        with self.assertRaises(AssertionError):
            assert_complete(events()[:-1], 1)

    def test_completion_cannot_double_count_tokens(self):
        rows = events()
        rows[-1]["totalTokens"] = 18
        with self.assertRaises(AssertionError):
            assert_complete(rows, 1)

    def test_completion_cannot_repeat_consumption_outcome(self):
        rows = events()
        rows[-1]["outcome"] = "success"
        with self.assertRaises(AssertionError):
            assert_complete(rows, 1)

    def test_completion_identity_must_match_dispatch(self):
        rows = events()
        rows[-1]["credentialId"] = "other"
        with self.assertRaises(AssertionError):
            assert_complete(rows, 1)

    def test_duplicate_usage_is_rejected(self):
        rows = events()
        duplicate = {**deepcopy(rows[1]), "eventId": "four", "sequence": 4}
        with self.assertRaises(AssertionError):
            assert_complete([*rows, duplicate], 1)

    def test_unknown_reporter_cannot_invent_tokens_or_http_200(self):
        rows = events()
        final = {**rows[-1], "kind": "usage", "sequence": 2, "outcome": "success", "statusCode": 200}
        with self.assertRaises(AssertionError):
            assert_complete([rows[0], final], 1)

    def test_finish_measurements_must_have_monotonic_source(self):
        rows = events()
        rows[-1]["measurements"]["source"] = "guessed-page-timing"
        with self.assertRaises(AssertionError):
            assert_complete(rows, 1)


if __name__ == "__main__":
    unittest.main()
