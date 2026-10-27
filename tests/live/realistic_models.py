#!/usr/bin/env python3
"""Bounded, explicitly requested real model tasks through the running Lain gateway.

Default prepares a frozen plan; --execute sends at most two requests per alias.
Uses installed catalog defaults and captured native function schemas. The harness
does not read provider credentials, write configuration, change routes or retry.

New plans use auto-v2 (automatic choice on both requests) and a structured,
bounded quantity-validation assessment. forced-exec-v2 is an explicit control.
K1/K2 use fixed generated contexts, evidence sentinels and explicit byte bounds;
token targets are verified only against observed provider usage.
cohere-auto-v2 is restricted to the exact Cohere K0 route, automatic/parallel
tools and deliberately omitted output budget, through the installed Responses path.
The legacy payload helper default remains byte-compatible with the separate
model_boundaries.py diagnostics; v1 sweep plans cannot execute with this source.
"""
from __future__ import annotations

import argparse
import ast
from concurrent.futures import ThreadPoolExecutor, as_completed
import copy
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import threading
import time
import tomllib
import uuid
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

ROOT = Path(__file__).resolve().parents[2]
TOOLS_FILE = Path(__file__).with_name('fixtures') / 'codex-0.164.0-native-tools.json'
FIXTURE = '''from decimal import Decimal, ROUND_HALF_UP

def invoice_total(lines, discount_percent=Decimal("0")):
    """Return a cent-rounded total; quantities must be positive integers.

    Empty invoices total zero. Negative prices and discounts outside 0..100
    are rejected. Boolean quantities are not valid integer quantities.
    """
    if not Decimal("0") <= discount_percent <= Decimal("100"):
        raise ValueError("invalid discount")
    subtotal = Decimal("0")
    for line in lines:
        price = Decimal(line["price"])
        quantity = line["quantity"]
        if price < 0:
            raise ValueError("negative price")
        subtotal += price * quantity
    return (subtotal * (1 - discount_percent / 100)).quantize(
        Decimal("0.01"), rounding=ROUND_HALF_UP)
'''
COMMAND = 'cat src/invoice.py'
WORKDIR = '/workspace/lain-qualification'
MAX_BODY = 4 * 1024 * 1024
MAX_EVENTS = 20000
MAX_STREAM_SECONDS = 90
SOCKET_TIMEOUT = 45
LEGACY_MODE = 'forced-exec-v1'
DEFAULT_MODE = 'auto-v2'
COHERE_MODE = 'cohere-auto-v2'
COHERE_ALIAS = 'command-a-plus-05-2026'
MODES = (DEFAULT_MODE, 'forced-exec-v2', COHERE_MODE)
# Targets are scenario labels, never token counts inferred from bytes.
CONTEXT_MODES = ('k0', 'k1-32k', 'k2-110k')
CONTEXT_SPECS = {
    'k1-32k': {'targetInputTokens': 32000, 'generatedBytes': 128000,
               'maximumContextBytes': 196608, 'maximumInitialRequestBytes': 196608,
               'maximumContinuationRequestBytes': 294912},
    'k2-110k': {'targetInputTokens': 110000, 'generatedBytes': 440000,
                'maximumContextBytes': 786432, 'maximumInitialRequestBytes': 786432,
                'maximumContinuationRequestBytes': 884736},
}
CONTEXT_EVIDENCE = {'beginning': 'invoice-draft-revision-17',
                    'middle': 'preserve-half-up-cents', 'end': 'reject-invalid-before-subtotal',
                    'file': 'invoice-fixture-quantity-review-42'}
MAX_REPLAY_BYTES = 65536

QUALITY = {
    'version': 'quantity-validation-json-v1',
    'cases': {'positive': 'accept', 'zero': 'reject', 'negative': 'reject',
              'boolean': 'reject', 'fractional': 'reject'},
    'fix': {'placement': 'before_subtotal', 'exception': 'ValueError'},
    'guardCases': [
        {'id': 'positive_one', 'quantity': 1, 'reject': False},
        {'id': 'positive_many', 'quantity': 7, 'reject': False},
        {'id': 'large_integer', 'quantity': 10**30, 'reject': False},
        {'id': 'zero', 'quantity': 0, 'reject': True},
        {'id': 'negative', 'quantity': -2, 'reject': True},
        {'id': 'true', 'quantity': True, 'reject': True},
        {'id': 'false', 'quantity': False, 'reject': True},
        {'id': 'fractional', 'quantity': 1.5, 'reject': True},
        {'id': 'integral_float', 'quantity': 2.0, 'reject': True},
        {'id': 'numeric_string', 'quantity': '2', 'reject': True},
        {'id': 'null', 'quantity': None, 'reject': True},
        {'id': 'list', 'quantity': [], 'reject': True},
        {'id': 'object', 'quantity': {}, 'reject': True},
    ],
    'requiredChecks': ['finding_present', 'boundary_cases', 'fix_placement',
                       'fix_exception', 'guard_supported', 'guard_boundaries'],
    'guardMaximumCharacters': 300,
    'guardMaximumNodes': 64,
    'scope': 'Conservative structured review and restricted predicate boundary checks; '
             'no model code execution, arbitrary patch verification or general quality certification.',
}


def now():
    return datetime.now(timezone.utc).isoformat()


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2)+'\n').encode()


def save(path, value):
    path = Path(path)
    temporary = path.with_name('.'+path.name+'.tmp')
    with temporary.open('wb') as stream:
        os.chmod(temporary, 0o600)
        stream.write(encoded(value)); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, path)


def cli(*args):
    result = subprocess.run(['lain', *args, '--json'], capture_output=True, timeout=30)
    if result.returncode:
        raise ValueError('Lain public inspection failed: '+args[0])
    return json.loads(result.stdout)


def local_base(value):
    parsed = urlsplit(value)
    if (parsed.scheme != 'http' or parsed.hostname != '127.0.0.1' or
        parsed.port is None or parsed.path != '/v1' or parsed.query or
        parsed.fragment or parsed.username or parsed.password):
        raise ValueError('Only the explicit local Lain /v1 endpoint is permitted')
    return value.rstrip('/')


def routes(models, catalog, profile):
    """Resolve aliases from actual provider groups; never merge equal model names."""
    if len(profile['routeIds']) != len(models):
        raise ValueError('The active profile must cover the complete configured model set')
    accounts = {}
    for model in models:
        ids = model.get('accountIds', [])
        if len(ids) != 1:
            raise ValueError('Live qualification requires unambiguous account bindings')
        accounts.setdefault(model['provider'], set()).update(ids)
    if any(len(ids) != 1 for ids in accounts.values()):
        raise ValueError('One explicit account per provider is required for this frozen plan')
    groups = {}
    for group in catalog['lain_model_picker']['groups']:
        if group['id'].startswith('provider:'):
            provider = group['id'][len('provider:'):]
            for alias in group['models']:
                if alias in groups and groups[alias] != provider:
                    raise ValueError('Conflicting provider membership')
                groups[alias] = provider
    entries = []
    for model in catalog['models']:
        alias = model['slug']
        if alias.startswith('lain-auto-group-'):
            continue  # routers are not additional independent models
        provider = groups.get(alias)
        if provider is None:
            matches = [item for item in models if item['id'] == alias]
            if model.get('visibility') != 'hide' or len(matches) != 1:
                raise ValueError('Alias has no proven provider binding')
            provider = matches[0]['provider']
        if provider not in accounts:
            raise ValueError('Catalog provider is absent from public model inventory')
        entries.append({'alias': alias, 'provider': provider,
                        'accountId': next(iter(accounts[provider])),
                        'reasoningEffort': model.get('default_reasoning_level'),
                        'visibility': model.get('visibility')})
    if len(entries) != len(models) or len({e['alias'] for e in entries}) != len(entries):
        raise ValueError('Native catalog does not cover every configured route exactly once')
    for provider in accounts:
        if sum(e['provider'] == provider for e in entries) != sum(m['provider'] == provider for m in models):
            raise ValueError('Provider route coverage does not match public inventory')
    return entries


def project_context():
    # A useful project handbook, not a token-limit benchmark or private code dump.
    sections = [
        ('validation', 'Validate boundary inputs before arithmetic. Reject booleans where an integer is required. Do not silently coerce invalid quantities.'),
        ('money', 'Use Decimal for monetary values, ROUND_HALF_UP for final cents, and preserve native currencies. Never accumulate money in binary float.'),
        ('invoices', 'Empty drafts have a zero total. Posted invoices are immutable. Discounts apply to subtotal, never by inventing a negative line item.'),
        ('refunds', 'Represent refunds as separate documents referencing an original invoice. A negative quantity in an ordinary invoice is invalid.'),
        ('idempotency', 'An idempotency key identifies one logical operation. Reject reuse with a different body. Persist the completed result before returning success.'),
        ('concurrency', 'Check document revisions before replacing a draft. Preserve user edits on conflicts. Never accept a stale worker result as the current draft.'),
        ('storage', 'The SQLite ledger owns account and invoice identities. Use a transaction for document and journal writes. Do not scrape a GUI for application state.'),
        ('recovery', 'Stop writers before a consistent backup. Restore into a new location. A binary rollback alone does not establish schema compatibility.'),
        ('api', 'Document validation failures precisely. Keep validation, authentication, permission and capacity errors distinct. Do not turn an error into a fabricated total.'),
        ('privacy', 'The fixture contains no customer data or credentials. Avoid logging bearer tokens, payment details or arbitrary provider diagnostics.'),
        ('tools', 'Use only the tool needed for this review. Do not initialize accounts, call an external service, create goals or modify files.'),
        ('testing', 'Useful boundary cases include zero, negative and boolean quantities, fractional quantities, invalid discounts and half-cent rounding.'),
        ('review', 'Focus on observable correctness. Explain the concrete trigger and the smallest sufficient fix. A successful HTTP response alone is not an acceptance test.'),
        ('delivery', 'A release binds source revision, dependency locks and exact artifacts. Historical tests on another binary do not qualify the current package.'),
    ]
    text = '# Invoice service project context\n\n'
    for name, rule in sections:
        text += f'## {name}\n{rule}\n'
        for operation in ['create draft', 'update draft', 'preview total', 'post document', 'read history']:
            text += (f'- During {operation}: {rule} Preserve the documented behavior of all other operations. '
                     'Keep failure details actionable and retain partial diagnostic evidence.\n')
    return text


def generated_context(context_mode):
    if context_mode == 'k0':return project_context()
    spec = CONTEXT_SPECS[context_mode]
    # Three relevant rules embedded among deterministic, numbered ledger reviews.
    # No model-produced input, random padding, personal data or hand-edited corpus.
    rules = [
        'Review rule beginning: invoice-draft-revision-17. Preserve draft revision checks.',
        'Review rule middle: preserve-half-up-cents. Preserve Decimal ROUND_HALF_UP cents.',
        'Review rule end: reject-invalid-before-subtotal. Reject bad quantities before arithmetic.',
    ]
    size = spec['generatedBytes']
    lengths = [size // 2, size - size // 2 - 256, 256]
    chunks = []
    for section, (rule, length) in enumerate(zip(rules, lengths)):
        header = rule + '\n' if section != 2 else ''
        footer = '\n' + rule + '\n' if section == 2 else ''
        remaining = length - len((header + footer).encode())
        records = []
        i = 0
        while remaining:
            operation, detail = [
                ('draft', 'positive integer quantity 3 at Decimal price 12.50 totals 37.50 before discount'),
                ('validation', 'quantity 0 or -2 must fail before subtotal; do not invent a negative invoice line'),
                ('types', 'True, 2.0 and numeric strings are not positive integer quantities'),
                ('rounding', 'preserve Decimal arithmetic and final ROUND_HALF_UP cent rounding'),
                ('concurrency', 'reject a stale draft revision while preserving the current user edits'),
                ('refund', 'create a separate refund document referencing the original posted invoice'),
                ('discount', 'validate the 0..100 percent range; empty drafts total zero'),
                ('posting', 'posted invoices are immutable; retain the original currency and audit identity'),
            ][i % 8]
            record = f'Ledger review {section}:{i:05d} {operation}: {detail}.\n'
            take = record[:remaining]
            records.append(take);remaining -= len(take);i += 1
        chunks.append(header + ''.join(records) + footer)
    text = ''.join(chunks)
    if len(text.encode()) != size or size > spec['maximumContextBytes']:
        raise ValueError('Generated context outside fixed byte contract')
    return text


def fixture_text(context_mode):
    return FIXTURE if context_mode == 'k0' else (
        '# File review evidence: ' + CONTEXT_EVIDENCE['file'] + '\n' + FIXTURE)


def capacity_record(entry, record):
    """Reviewed public route evidence; unknown remains explicit, never a guessed limit."""
    if not isinstance(record, dict) or set(record) != {
            'alias', 'provider', 'accountId', 'contextWindowTokens', 'maximumOutputTokens',
            'source', 'checkedAt'}:
        raise ValueError('Context capacity needs an exact reviewed route evidence record')
    if any(record[k] != entry[k] for k in ('alias', 'provider', 'accountId')):
        raise ValueError('Context capacity route/account mismatch')
    for key in ('contextWindowTokens', 'maximumOutputTokens'):
        value = record[key]
        if value is not None and (type(value) is not int or not 0 < value <= 100000000):
            raise ValueError('Invalid capacity value')
    if not isinstance(record['source'], str) or not record['source'].startswith('https://'):
        raise ValueError('Capacity needs a reviewed public HTTPS source, including for unknown limits')
    if not isinstance(record['checkedAt'], str):raise ValueError('Capacity evidence needs checkedAt')
    datetime.fromisoformat(record['checkedAt'].replace('Z', '+00:00'))
    return copy.deepcopy(record)


def context_contract(entries, context_mode):
    if context_mode not in CONTEXT_MODES:raise ValueError('Unknown context mode')
    if context_mode == 'k0':return None
    spec = CONTEXT_SPECS[context_mode]
    if len(entries) != 1:raise ValueError('Long-context probes select exactly one concrete alias')
    capacity = capacity_record(entries[0], entries[0].get('contextCapacity'))
    window, maximum = capacity['contextWindowTokens'], capacity['maximumOutputTokens']
    if maximum is not None and maximum < 2048:
        raise ValueError('Known provider maximum output is below the requested 2048 reserve')
    if window is not None and spec['targetInputTokens'] + 2048 > window:
        raise ValueError('Context target plus output reserve exceeds known provider window')
    return {'mode': context_mode, **spec, 'outputReserveTokens': 2048,
            'contextSha256': sha(generated_context(context_mode).encode()),
            'fixtureSha256': sha(fixture_text(context_mode).encode()),
            'requiredEvidence': CONTEXT_EVIDENCE.copy(), 'capacity': capacity,
            'capacityStatus': 'unknown' if window is None else 'target_fits_not_tokenized',
            'tokenMeasurement': 'provider usage only; targets and UTF-8 bytes are not token measurements',
            'maximumReplayBytes': MAX_REPLAY_BYTES}


def check_context_request(body, context_mode, *, continuation=False):
    if context_mode == 'k0':return
    key = 'maximumContinuationRequestBytes' if continuation else 'maximumInitialRequestBytes'
    if len(encoded(body)) > CONTEXT_SPECS[context_mode][key]:
        raise OutcomeError('context_request_byte_limit')


def check_context_usage(meta, entry, context_mode):
    if context_mode == 'k0':return
    usage = meta.get('usage') or {}
    count = usage.get('input_tokens')
    window = entry['contextCapacity']['contextWindowTokens']
    if type(count) is int and window is not None and count + 2048 > window:
        raise OutcomeError('context_window_exceeded', {'inputTokens': count,
                           'outputReserveTokens': 2048, 'contextWindowTokens': window})


def validate_cohere_scope(entries, tools, context_mode):
    if (context_mode != 'k0' or len(entries) != 1 or
        entries[0].get('provider') != 'cohere' or entries[0].get('alias') != COHERE_ALIAS):
        raise ValueError('cohere-auto-v2 requires exactly provider cohere, alias '+COHERE_ALIAS+' and K0')
    native = json.loads(TOOLS_FILE.read_bytes())['tools']
    if (len(native) != 7 or any(t.get('type') != 'function' for t in native) or
        encoded(tools) != encoded(native)):
        raise ValueError('cohere-auto-v2 requires the exact seven captured native function tools')


def payload(entry, tools, inputs=None, *, mode=LEGACY_MODE, context_mode='k0'):
    # Keep the legacy default for model_boundaries.py's independently frozen
    # diagnostics. All new sweep plans/qualify calls pass a v2 mode explicitly.
    if context_mode not in CONTEXT_MODES:raise ValueError('Unknown context mode')
    if context_mode != 'k0' and mode != DEFAULT_MODE:
        raise ValueError('Long-context qualification requires auto on both requests')
    if mode not in (*MODES, LEGACY_MODE):
        raise ValueError('Unknown qualification payload mode')
    if mode == COHERE_MODE:validate_cohere_scope([entry],tools,context_mode)
    body = {'model': entry['alias'], 'stream': True, 'store': False,
            'max_output_tokens': 2048, 'parallel_tool_calls': False,
            'tools': copy.deepcopy(tools),
            'instructions': 'You are a coding assistant reviewing a synthetic invoice service. '+
                'Read the requested file with the supplied exec_command tool. Do not change files or invoke other tools.',
            'input': inputs if inputs is not None else [
                {'role': 'developer', 'content': [{'type':'input_text', 'text':project_context()}]},
                {'role': 'user', 'content': [{'type':'input_text', 'text':
                    'Review src/invoice.py for a concrete quantity-validation bug. First call exec_command '+
                    f'with cmd exactly "{COMMAND}" and workdir exactly "{WORKDIR}"; omit other arguments. '+
                    'After reading it, answer in at most 80 words using FINDING: and FIX: on separate lines. '+
                    'Give the failing input category and the smallest correction. Do not patch the file.'}]}],
            'tool_choice': {'type':'function','name':'exec_command'} if inputs is None else 'none'}
    if mode in MODES:
        body['instructions'] = (
            'You are a coding assistant reviewing a synthetic invoice service. '
            'Use the available tool to inspect the file. Do not change files or invoke other tools.')
        if inputs is None:
            body['input'][-1]['content'][0]['text'] = (
                'Review src/invoice.py for a concrete quantity-validation bug. The file contents '
                'are not included; inspect the file before answering. The permitted read is '
                f'exec_command with cmd exactly "{COMMAND}" and workdir exactly "{WORKDIR}"; '
                'Optional native execution metadata may be omitted or use login:false, tty:false, '
                'sandbox_permissions:"use_default", max_output_tokens (1..10000), or yield_time_ms (0..30000). '
                'Do not patch the file. Return one JSON object with: '
                'finding (a brief explanation of the missing check and its effect), '
                'cases (map positive, zero, negative, boolean, fractional quantities to '
                '"accept" or "reject" according to the file contract), and fix '
                '(an object with reject_if: a concrete Python boolean expression on quantity, '
                'placement: "before_subtotal" if it belongs before subtotal accumulation, '
                'and exception: the exception class to raise for invalid quantities). '
                'The reject_if expression may use only quantity, int, bool, type(quantity), '
                'isinstance(quantity, int), isinstance(quantity, bool), literals, comparisons '
                'and and/or/not. Preserve valid positive integer quantities and all other behavior.')
        if mode in (DEFAULT_MODE, COHERE_MODE):
            body['tool_choice'] = 'auto'
    if mode == COHERE_MODE:
        body['parallel_tool_calls'] = True
        del body['max_output_tokens']  # Unsupported by this route; no invented provider budget.
    if entry['reasoningEffort'] is not None:
        body['reasoning'] = {'effort': entry['reasoningEffort']}
    if context_mode != 'k0' and inputs is None:
        body['input'][0]['content'][0]['text'] = generated_context(context_mode)
        body['input'][-1]['content'][0]['text'] += (
            ' Also return context_evidence with keys beginning, middle, end and file: '
            'copy the three review-rule identifiers from the supplied project context and '
            'the file review evidence identifier from the inspected file. Preserve these rules in your fix.')
    return body


def parse_guard(expression):
    """Accept a tiny predicate grammar, never Python eval/exec or model calls."""
    if not isinstance(expression, str) or len(expression) > QUALITY['guardMaximumCharacters']:
        raise ValueError('Unsupported guard')
    tree = ast.parse(expression, mode='eval')
    nodes = list(ast.walk(tree))
    allowed = (ast.Expression, ast.BoolOp, ast.And, ast.Or, ast.UnaryOp, ast.Not, ast.USub,
               ast.Compare, ast.Lt, ast.LtE, ast.Gt, ast.GtE, ast.Eq, ast.NotEq, ast.Is,
               ast.IsNot, ast.Name, ast.Load, ast.Constant, ast.Call)
    if len(nodes) > QUALITY['guardMaximumNodes'] or any(not isinstance(n, allowed) for n in nodes):
        raise ValueError('Unsupported guard')
    for node in nodes:
        if isinstance(node, ast.Name) and node.id not in ('quantity', 'int', 'bool', 'type', 'isinstance'):
            raise ValueError('Unsupported name')
        if isinstance(node, ast.Call):
            if (node.keywords or not isinstance(node.func, ast.Name) or not node.args or
                not isinstance(node.args[0], ast.Name) or node.args[0].id != 'quantity'):
                raise ValueError('Unsupported call')
            if node.func.id == 'type' and len(node.args) == 1:
                continue
            if (node.func.id == 'isinstance' and len(node.args) == 2 and
                isinstance(node.args[1], ast.Name) and node.args[1].id in ('int', 'bool')):
                continue
            raise ValueError('Unsupported call')
    return tree.body


def guard_value(node, quantity):
    if isinstance(node, ast.Constant):return node.value
    if isinstance(node, ast.Name):return {'quantity': quantity, 'int': int, 'bool': bool}[node.id]
    if isinstance(node, ast.Call):
        if node.func.id == 'type':return type(quantity)
        return isinstance(quantity, int if node.args[1].id == 'int' else bool)
    if isinstance(node, ast.UnaryOp):
        value = guard_value(node.operand, quantity)
        return not value if isinstance(node.op, ast.Not) else -value
    if isinstance(node, ast.BoolOp):
        for operand in node.values:
            value = guard_value(operand, quantity)
            if isinstance(node.op, ast.And) and not value:return value
            if isinstance(node.op, ast.Or) and value:return value
        return value
    if isinstance(node, ast.Compare):
        left = guard_value(node.left, quantity)
        for op, right_node in zip(node.ops, node.comparators):
            right = guard_value(right_node, quantity)
            if isinstance(op, ast.Lt):matched = left < right
            elif isinstance(op, ast.LtE):matched = left <= right
            elif isinstance(op, ast.Gt):matched = left > right
            elif isinstance(op, ast.GtE):matched = left >= right
            elif isinstance(op, ast.Eq):matched = left == right
            elif isinstance(op, ast.NotEq):matched = left != right
            elif isinstance(op, ast.Is):matched = left is right
            else:matched = left is not right
            if not matched:return False
            left = right
        return True
    raise ValueError('Unsupported guard')


def assess_answer(text, context_mode='k0'):
    """Report check outcomes only; retain no raw answer or model expression."""
    checks = dict.fromkeys(QUALITY['requiredChecks'], False)
    result = {'version': QUALITY['version'], 'checks': checks, 'guardCases': {}}
    # A fenced object is common in otherwise correct coding answers.
    candidate = re.sub(r'^```(?:json)?\s*\n(.*?)\n```$', r'\1', text.strip(), flags=re.S)
    try:answer = json.loads(candidate)
    except (ValueError, RecursionError):answer = None
    if isinstance(answer, dict):
        checks['finding_present'] = isinstance(answer.get('finding'), str) and bool(answer['finding'].strip())
        checks['boundary_cases'] = answer.get('cases') == QUALITY['cases']
        fix = answer.get('fix')
        if isinstance(fix, dict):
            checks['fix_placement'] = fix.get('placement') == QUALITY['fix']['placement']
            checks['fix_exception'] = fix.get('exception') == QUALITY['fix']['exception']
            try:
                guard = parse_guard(fix.get('reject_if'))
                checks['guard_supported'] = True
                for case in QUALITY['guardCases']:
                    try:
                        actual = guard_value(guard, case['quantity'])
                        matched = type(actual) is bool and actual == case['reject']
                    except (TypeError, ValueError, KeyError, OverflowError):matched = False
                    result['guardCases'][case['id']] = matched
                checks['guard_boundaries'] = all(result['guardCases'].values())
            except (SyntaxError, ValueError, TypeError, RecursionError):pass
    if context_mode != 'k0':
        checks['context_evidence'] = isinstance(answer, dict) and answer.get('context_evidence') == CONTEXT_EVIDENCE
    result['passed'] = all(checks.values())
    return result


def execution_contract(entries, tools, mode, context_mode='k0'):
    if mode not in MODES:raise ValueError('A new plan requires an explicit v2 payload mode')
    if mode == COHERE_MODE:validate_cohere_scope(entries,tools,context_mode)
    context = context_contract(entries, context_mode)
    contract = {'version': 2, 'payloadMode': mode, 'quality': copy.deepcopy(QUALITY),
            'fixtureArguments':'exact-read-with-bounded-neutral-native-metadata-v1',
            'toolChoice': {'initial': 'auto', 'continuation': 'auto'} if mode in (DEFAULT_MODE, COHERE_MODE) else
                          {'initial': {'type': 'function', 'name': 'exec_command'}, 'continuation': 'none'},
            'harnessSha256': sha(Path(__file__).read_bytes()),
            'requests': [{
                'alias': entry['alias'],
                'firstSha256': sha(encoded(payload(entry, tools, mode=mode, context_mode=context_mode))),
                # Output items are unknown until inference; pin every static
                # continuation field using an empty-input template as well.
                'continuationTemplateSha256': sha(encoded(payload(entry, tools, [], mode=mode, context_mode=context_mode))),
            } for entry in entries]}
    if context is not None:
        if mode != DEFAULT_MODE:raise ValueError('K1/K2 require auto on both requests')
        for entry in entries:
            check_context_request(payload(entry,tools,mode=mode,context_mode=context_mode),context_mode)
        if any(t.get('type') != 'function' for t in tools):
            raise ValueError('K1/K2 accept only the existing native function fixture')
        contract['quality']['requiredChecks'].append('context_evidence')
        contract['context'] = context
        contract['context']['toolSummary'] = {'topLevelTools':len(tools),
            'callableDefinitions':len(tools), 'namespaceMembers':0, 'toolBytes':len(encoded(tools))}
    if mode == COHERE_MODE:
        contract['requestControls'] = {'parallelToolCalls':True, 'maximumOutputTokens':None,
            'outputBudget':'omitted_unknown', 'contextMode':'k0', 'provider':'cohere', 'alias':COHERE_ALIAS,
            'maximumResponseBytes':MAX_BODY, 'maximumSseEvents':MAX_EVENTS,
            'streamDeadlineSeconds':MAX_STREAM_SECONDS, 'socketTimeoutSeconds':SOCKET_TIMEOUT}
    return contract


def verify_execution_contract(plan, tools):
    contract = plan.get('executionContract')
    if (plan.get('schemaVersion') != 2 or plan.get('kind') != 'lain-realistic-model-qualification' or
        not isinstance(contract, dict)):
        raise ValueError('Legacy or unknown plan: prepare a distinct v2 run; preserve original evidence')
    current = execution_contract(plan['entries'], tools, contract.get('payloadMode'),
                                 contract.get('context', {}).get('mode', 'k0'))
    if encoded(contract) != encoded(current):
        raise ValueError('Frozen payload/quality/source contract changed; prepare a distinct run')
    if plan.get('maximumRequests') != len(plan['entries']) * 2:
        raise ValueError('Frozen request budget changed')
    if contract['payloadMode'] == COHERE_MODE and plan.get('maximumOutputTokens','missing') is not None:
        raise ValueError('Cohere plan must explicitly declare unknown/omitted output budget')
    return contract['payloadMode']


def classify(raw):
    text = raw.decode(errors='replace').lower()
    for name, patterns in [
        ('context_limit', ['context_length_exceeded', 'context limit', 'context window', 'maximum context']),
        ('tool_limit', ['at most 128', 'maximum of 128', 'max is 128', 'maximum number of items is 128', 'too many tools', 'tool_limit_exceeded']),
        ('reasoning_configuration', ['unsupported value: \'none\'', 'unsupported value: "none"', 'reasoning.effort', 'reasoning_effort', 'reasoning effort']),
        ('tool_schema', ['provider-defined', 'provider defined', 'unsupported tool', 'tools[', 'tools.', 'invalid tool', 'tool_choice']),
        ('account_or_capacity', ['insufficient_quota', 'rate_limit', 'rate limit', 'resource_exhausted', 'quota exceeded']),
        ('model_unavailable', ['model_not_found', 'model not found', 'no such model', 'unknown model', 'not a valid model']),
        ('authentication', ['unauthorized', 'invalid_api_key', 'invalid api key', 'authentication']),
    ]:
        if any(pattern in text for pattern in patterns):
            return name
    return 'upstream_rejected'


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


class OutcomeError(Exception):
    def __init__(self, category, details=None):
        self.category, self.details = category, details or {}


def safe_error(raw, token):
    """Bounded provider diagnostics from synthetic prompts; never expose auth."""
    try:value=json.loads(raw)
    except ValueError:return {'format':'non-json','bytes':len(raw)}
    for _ in range(8):
        if isinstance(value,dict):
            if isinstance(value.get('error'),(dict,str)):value=value['error'];continue
            if isinstance(value.get('response'),dict):value=value['response'];continue
            message=value.get('message')
            if isinstance(message,str) and message.lstrip().startswith('{'):
                try:value=json.loads(message);continue
                except ValueError:pass
        elif isinstance(value,str):
            try:value=json.loads(value);continue
            except ValueError:return {'format':'string-error','bytes':len(raw)}
        break
    if not isinstance(value,dict):return {'format':'unstructured','bytes':len(raw)}
    result={}
    for key in ('code','type','param','message'):
        text=value.get(key)
        if not isinstance(text,(str,int)) or isinstance(text,bool):continue
        text=str(text)
        if token:text=text.replace(token,'[redacted]')
        text=re.sub(r'(?i)bearer\s+[^\s\"<>]+','Bearer [redacted]',text)
        text=re.sub(r'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+','[redacted-jwt]',text)
        text=re.sub(r'(?i)\bsk-[A-Za-z0-9_-]{8,}','[redacted-key]',text)
        text=re.sub(r'[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}','[redacted-email]',text)
        if '<html' in text.lower() or '<!doctype' in text.lower():text='HTML diagnostic omitted'
        result[key]=' '.join(text.split())[:800]
    return result


def sse(response, token=''):
    started = time.monotonic(); count = size = 0; data = []; items = []; final = None
    def consume(lines):
        nonlocal count, final
        raw = b'\n'.join(lines)
        if not raw or raw == b'[DONE]':
            return
        try:event = json.loads(raw)
        except ValueError:raise OutcomeError('invalid_sse') from None
        count += 1
        if event.get('type') in ('error','response.failed','response.incomplete'):
            raise OutcomeError(classify(raw), {'sseEvents': count, 'responseBytes':size,
                                               'diagnostic':safe_error(raw,token)})
        if event.get('type') == 'response.output_item.done' and isinstance(event.get('item'),dict):
            items.append(event['item'])
        if event.get('type') == 'response.completed':
            final = event.get('response')
    while final is None:
        if time.monotonic()-started > MAX_STREAM_SECONDS or count > MAX_EVENTS:
            raise OutcomeError('stream_deadline')
        line = response.readline(MAX_BODY+1)
        size += len(line)
        if size > MAX_BODY:raise OutcomeError('response_size')
        if not line:
            if data:consume(data)
            break
        if not line.strip():
            if data:consume(data); data=[]
        elif line.startswith(b'data:'):
            data.append(line[5:].lstrip().rstrip(b'\r\n'))
    if not isinstance(final,dict):raise OutcomeError('incomplete_stream',{'sseEvents':count,'responseBytes':size})
    if final.get('status') not in (None,'completed'):
        raise OutcomeError('incomplete_response')
    output = final.get('output') or items
    usage = final.get('usage') or {}
    measured = {k:usage.get(k) if type(usage.get(k)) is int and usage[k]>=0 else None
                for k in ('input_tokens','output_tokens','total_tokens')}
    return output, {'sseEvents':count,'responseBytes':size,'usage':measured,
                    'reportedModel':final.get('model'),'responseId':final.get('id')}


def exchange(base, token, entry, body, record):
    raw = encoded(body)
    record.update(startedAt=now(), requestBytes=len(raw), requestSha256=sha(raw),
                  tools=len(body['tools']), maxOutputTokens=body.get('max_output_tokens'),
                  toolChoice=copy.deepcopy(body['tool_choice']),
                  inputBytes=len(encoded(body['input'])), instructionsBytes=len(body['instructions'].encode()))
    headers={
        'Authorization':'Bearer '+token, 'Content-Type':'application/json',
        'User-Agent':'lain-realistic-model-qualification/0.2',
        'X-Lain-Account-ID':entry['accountId']}
    if entry.get('conversationId') and entry.get('turnId'):
        headers['X-Codex-Turn-Metadata']=json.dumps({
            'thread_id':entry['conversationId'],'turn_id':entry['turnId']})
    request = Request(base+'/responses',raw,method='POST',headers=headers)
    opener = build_opener(ProxyHandler({}),NoRedirect())
    started = time.monotonic()
    try:
        try:response = opener.open(request, timeout=SOCKET_TIMEOUT)
        except HTTPError as error:response=error
        with response:
            record['statusCode'] = response.status
            request_id=response.headers.get('X-Request-ID')
            if request_id and re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}',request_id):record['requestId']=request_id
            if response.status >= 300:
                raw = response.read(65537)
                raise OutcomeError(classify(raw),{'diagnosticBytes':len(raw),'diagnosticSha256':sha(raw),
                                                  'diagnostic':safe_error(raw,token)})
            if 'text/event-stream' not in response.headers.get('Content-Type',''):
                raise OutcomeError('not_sse')
            return sse(response,token)
    except (URLError, TimeoutError, OSError):
        raise OutcomeError('transport_or_timeout') from None
    finally:
        record.update(finishedAt=now(),durationMs=round((time.monotonic()-started)*1000,3))


def tool_result(item, fixture, *, allow_native_options=False):
    if item.get('type') != 'function_call' or item.get('name') != 'exec_command' or not item.get('call_id'):
        raise OutcomeError('unexpected_tool_call')
    try:arguments=json.loads(item['arguments'])
    except (ValueError,KeyError,TypeError):raise OutcomeError('invalid_tool_arguments') from None
    if not isinstance(arguments,dict):raise OutcomeError('invalid_tool_arguments')
    options=set(arguments)-{'cmd','workdir'}
    neutral={'login':False,'tty':False,'sandbox_permissions':'use_default'}
    numeric={'max_output_tokens':(1,10000),'yield_time_ms':(0,30000)}
    valid_options=allow_native_options and all(
        (key in neutral and type(arguments[key]) is type(neutral[key]) and arguments[key]==neutral[key]) or
        (key in numeric and type(arguments[key]) is int and numeric[key][0]<=arguments[key]<=numeric[key][1])
        for key in options)
    if (arguments.get('cmd')!=COMMAND or arguments.get('workdir')!=WORKDIR or options and not valid_options):
        known={'cmd','workdir',*neutral,*numeric,'shell','justification','prefix_rule'}
        raise OutcomeError('tool_arguments_outside_fixture',{
            'commandMatches':arguments.get('cmd')==COMMAND,'workdirMatches':arguments.get('workdir')==WORKDIR,
            'knownArgumentKeys':sorted(set(arguments)&known),'unknownArgumentCount':len(set(arguments)-known)})
    # Execute only this fixed read; never execute model-provided shell text.
    result=subprocess.run(['/usr/bin/cat',str(fixture)],capture_output=True,timeout=5,check=True)
    return {'type':'function_call_output','call_id':item['call_id'],
            'output':'Process exited with code 0\nOutput:\n'+result.stdout.decode()}


def qualify(entry, tools, base, token, fixture, row, checkpoint, stop, *, mode=DEFAULT_MODE, context_mode='k0'):
    try:
        if stop.is_set():row['state']='cancelled';return
        if mode not in MODES:raise ValueError('Qualification requires a v2 payload mode')
        row['payloadMode']=mode
        if context_mode != 'k0':row['contextContract']=context_contract([entry],context_mode)
        body=payload(entry,tools,mode=mode,context_mode=context_mode)
        check_context_request(body,context_mode)
        call={'step':'tool'};row['calls'].append(call);checkpoint()
        output, meta=exchange(base,token,entry,body,call);call.update(meta,state='passed');checkpoint()
        check_context_usage(meta,entry,context_mode)
        if context_mode != 'k0' and len(encoded(output)) > MAX_REPLAY_BYTES:
            raise OutcomeError('context_replay_byte_limit')
        calls=[v for v in output if v.get('type') in ('function_call','custom_tool_call')]
        if len(calls)!=1:raise OutcomeError('expected_one_tool_call')
        result=tool_result(calls[0],fixture,allow_native_options=True)
        if stop.is_set():row['state']='cancelled';return
        inputs=body['input']+output+[result]
        follow=payload(entry,tools,inputs,mode=mode,context_mode=context_mode)
        check_context_request(follow,context_mode,continuation=True)
        call={'step':'continuation'};row['calls'].append(call);checkpoint()
        output,meta=exchange(base,token,entry,follow,call)
        call.update(meta,state='passed')
        check_context_usage(meta,entry,context_mode)
        text=''.join(c.get('text','') for item in output if item.get('type')=='message'
                     for c in item.get('content',[]) if c.get('type')=='output_text')
        if any(item.get('type') in ('function_call','custom_tool_call') for item in output):
            raise OutcomeError('unexpected_continuation_tool')
        if not text.strip():raise OutcomeError('empty_final_answer')
        assessment=assess_answer(text,context_mode)
        row.update(state='passed' if assessment['passed'] else 'answer_review_needed',
                   toolRoundTrip=True, answerBytes=len(text.encode()),answerSha256=sha(text.encode()),
                   answerAssessment=assessment)
        if context_mode != 'k0':
            observed = [(c.get('usage') or {}).get('input_tokens') for c in row['calls']]
            initial = observed[0]
            target = CONTEXT_SPECS[context_mode]['targetInputTokens']
            reached = initial >= target if type(initial) is int else None
            row['contextObservation'] = {'inputTokens':observed, 'targetInputTokens':target,
                                         'targetReached':reached}
            if assessment['passed'] and reached is not True:
                row['state'] = 'context_usage_unknown' if reached is None else 'context_target_not_reached'
    except OutcomeError as error:
        row.update(state='failed',category=error.category,details=error.details)
        if row['calls']:row['calls'][-1].setdefault('state','failed')
    except Exception as error:
        row.update(state='harness_error',errorType=type(error).__name__)
    finally:
        row['finishedAt']=now();checkpoint()


def select_entries(entries, aliases):
    if aliases is None:return entries
    by_alias={entry['alias']:entry for entry in entries}
    if not aliases or len(set(aliases))!=len(aliases) or any(alias not in by_alias for alias in aliases):
        raise ValueError('Regression selection requires distinct exact concrete aliases from the complete inventory')
    return [by_alias[alias] for alias in aliases]


def prepare(output, mode=DEFAULT_MODE, aliases=None, context_mode='k0', capacity_path=None):
    if mode not in MODES:raise ValueError('Unknown v2 qualification mode')
    if context_mode not in CONTEXT_MODES:raise ValueError('Unknown context mode')
    if mode == COHERE_MODE and (context_mode != 'k0' or aliases != [COHERE_ALIAS]):
        raise ValueError('Cohere preparation requires exactly --only-alias '+COHERE_ALIAS+' and K0')
    if context_mode != 'k0' and (mode != DEFAULT_MODE or not aliases or len(aliases) != 1 or capacity_path is None):
        raise ValueError('K1/K2 require auto, one --only-alias and --context-capacity evidence')
    if context_mode == 'k0' and capacity_path is not None:raise ValueError('Capacity evidence is for K1/K2 only')
    output.mkdir(mode=0o700,parents=True,exist_ok=False)
    status=cli('status'); models=cli('models'); profiles=cli('profiles')
    if not status.get('proxyReady') or status['revision']!=status['appliedRevision']:
        raise ValueError('The existing applied gateway must be ready; no profile apply is performed')
    profile=next(p for p in profiles if p['id']==status['activeProfileId'])
    config_path=Path.home()/'.codex/config.toml'; config=tomllib.loads(config_path.read_text())
    if config.get('model_provider')!='lain':raise ValueError('The actual Codex standard must select Lain')
    base=local_base(config['model_providers']['lain']['base_url'])
    catalog_path=Path(config['model_catalog_json']); catalog_raw=catalog_path.read_bytes()
    catalog=json.loads(catalog_raw);all_entries=routes(models['models'],catalog,profile)
    entries=select_entries(all_entries,aliases)
    if context_mode != 'k0':
        if capacity_path.stat().st_size > 16384:raise ValueError('Capacity evidence exceeds 16384 bytes')
        entries[0]['contextCapacity']=capacity_record(entries[0],json.loads(capacity_path.read_bytes()))
        context_contract(entries,context_mode)
        (output/'generated-context.txt').write_text(generated_context(context_mode))
    fixture=output/'fixture/src/invoice.py';fixture.parent.mkdir(parents=True);fixture.write_text(fixture_text(context_mode))
    tool_record=json.loads(TOOLS_FILE.read_bytes()); tools=tool_record['tools']
    if len(tools)!=7 or any(t.get('type')!='function' for t in tools):raise ValueError('Unreviewed native tool fixture')
    plan={'schemaVersion':2,'kind':'lain-realistic-model-qualification','createdAt':now(),
          'executionContract':execution_contract(entries,tools,mode,context_mode),
          'revision':status['revision'],'profileId':status['activeProfileId'],'endpoint':base,
          'configuredConcreteAliases':len(all_entries),'selectedAliases':[e['alias'] for e in entries],
          'conversationId':'lain-realistic-v2-'+uuid.uuid4().hex,
          'entries':entries,'maximumRequests':len(entries)*2,
          'clientToolFixtureSha256':sha(TOOLS_FILE.read_bytes()),'clientToolProvenance':tool_record['provenance'],
          'nativeTools':len(tools),'projectContextBytes':len(generated_context(context_mode).encode()),
          'maximumOutputTokens':None if mode == COHERE_MODE else 2048,'fixtureSha256':sha(fixture.read_bytes()),
          'catalogPath':str(catalog_path),'catalogSha256':sha(catalog_raw),
          'modelInventory':models,'profile':profile,
          'limits':['One bounded file-review task per concrete alias, at most two HTTP requests.',
                    'Seven captured native function tools; delegation, plugins, MCP, custom and namespace tools are not qualified.',
                    'Meaningful project context is synthetic; no personal project code or conversations are sent.',
                    ('Cohere output token budget is deliberately omitted/unknown; response bytes, SSE events and time are bounded.'
                     if mode == COHERE_MODE else 'Output budget is explicitly 2048 tokens, not a test of unbounded native defaults.'),
                    'Payload mode and structured quantity-review criteria are frozen in executionContract; v1 evidence is not regraded.',
                    'Quality uses explicit boundary cases and a restricted fix predicate, not FINDING/FIX keywords; unsupported answers need review.',
                    'Only the exact fixture read is executed; bounded neutral native execution metadata is accepted, never executed as shell text.',
                    'A passed task establishes this payload only, not a maximum context size or full model certification.',
                    'No harness retry, route/account substitution, login, profile apply or service restart; upstream internal attempts require independent evidence.']}
    save(output/'plan.json',plan);save(output/'catalog.json',catalog)
    save(output/'source-tool-fixture.json',tool_record);save(output/'status-before.json',status)
    return plan


def execute(output, expected, workers):
    raw=(output/'plan.json').read_bytes()
    if sha(raw)!=expected:raise ValueError('Explicit frozen plan hash mismatch')
    plan=json.loads(raw)
    if (output/'report.json').exists():raise ValueError('Existing live evidence must not be overwritten or retried')
    tools=json.loads(TOOLS_FILE.read_bytes())['tools']
    # Fail closed before runtime inspection, auth helper or network access.
    mode=verify_execution_contract(plan,tools)
    context_mode=plan['executionContract'].get('context',{}).get('mode','k0')
    if context_mode != 'k0':
        if (output/'generated-context.txt').read_bytes() != generated_context(context_mode).encode():
            raise ValueError('Generated context changed; never hand-edit generated inputs')
        if (output/'fixture/src/invoice.py').read_bytes() != fixture_text(context_mode).encode():
            raise ValueError('Generated file task changed')
    status=cli('status');inventory=cli('models')
    if (not status.get('proxyReady') or status['revision']!=plan['revision'] or
        status['appliedRevision']!=plan['revision'] or status['activeProfileId']!=plan['profileId'] or
        inventory!=plan['modelInventory']):raise ValueError('Applied runtime changed since preparation')
    if sha(Path(plan['catalogPath']).read_bytes())!=plan['catalogSha256']:
        raise ValueError('Source catalog changed since preparation')
    if sha(TOOLS_FILE.read_bytes())!=plan['clientToolFixtureSha256']:
        raise ValueError('Native tool fixture changed since preparation')
    fixture=output/'fixture/src/invoice.py'
    if sha(fixture.read_bytes())!=plan['fixtureSha256']:raise ValueError('Fixture changed since preparation')
    result=subprocess.run(['lain','auth','token','--audience','codex'],capture_output=True,text=True,timeout=20,check=True)
    token=result.stdout.strip()
    if not token or '\n' in token:raise ValueError('Inference helper returned invalid credentials')
    local_base(plan['endpoint'])
    report={'schemaVersion':2,'kind':plan['kind'],'startedAt':now(),'planSha256':sha(raw),
            'executionContract':copy.deepcopy(plan['executionContract']),
            'limits':plan['limits'],'rows':[{**entry,'state':'pending','calls':[]} for entry in plan['entries']]}
    if plan.get('conversationId'):
        report['conversationId']=plan['conversationId']
        for row in report['rows']:
            row.update(conversationId=plan['conversationId'],turnId=row['alias'])
    lock=threading.Lock();stop=threading.Event();provider_locks={e['provider']:threading.Semaphore(1) for e in plan['entries']}
    def checkpoint():
        with lock:save(output/'report.json',report)
    def task(row):
        with provider_locks[row['provider']]:
            if (output/'stop').exists():stop.set()
            if stop.is_set():row['state']='cancelled';checkpoint();return
            current=cli('status')
            if (current['revision']!=plan['revision'] or current['appliedRevision']!=plan['revision'] or
                current['activeProfileId']!=plan['profileId'] or not current.get('proxyReady')):
                stop.set();row['state']='binding_changed';checkpoint();return
            row.update(state='running',startedAt=now());checkpoint()
            qualify(row,tools,plan['endpoint'],token,fixture,row,checkpoint,stop,mode=mode,
                    **({'context_mode':context_mode} if context_mode != 'k0' else {}))
    checkpoint()
    pending={}
    for row in report['rows']:pending.setdefault(row['provider'],[]).append(row)
    scheduled=[]
    while any(pending.values()):
        for queue in pending.values():
            if queue:scheduled.append(queue.pop(0))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        jobs=[pool.submit(task,row) for row in scheduled]
        for job in as_completed(jobs):
            job.result()
    report.update(finishedAt=now(),state='finished',inferenceHttpRequests=sum(len(r['calls']) for r in report['rows']))
    report['counts']={state:sum(r['state']==state for r in report['rows'])
                      for state in sorted({r['state'] for r in report['rows']})}
    report['profileAfter']=cli('status')
    report['bindingSnapshotStable']=all(report['profileAfter'].get(k)==status.get(k)
                                       for k in ('revision','appliedRevision','activeProfileId'))
    checkpoint()
    print(json.dumps({'counts':report['counts'],'requests':report['inferenceHttpRequests'],'report':str(output/'report.json')}))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--execute',action='store_true')
    parser.add_argument('--expect-plan-sha256')
    parser.add_argument('--workers',type=int,choices=[1,2,3],default=3)
    parser.add_argument('--tool-mode',choices=MODES,
                        help='Preparation only: auto-v2 default, forced-exec-v2 control, or exact Cohere K0 cohere-auto-v2 contract')
    parser.add_argument('--only-alias',action='append',help='Preparation only: exact concrete alias for bounded regression selection; repeat as needed')
    parser.add_argument('--context-mode',choices=CONTEXT_MODES,help='Preparation only: k0 default, k1-32k or k2-110k generated context')
    parser.add_argument('--context-capacity',type=Path,help='Preparation only: reviewed public route capacity JSON, at most 16384 bytes')
    args=parser.parse_args();os.umask(0o077)
    if args.execute:
        if not args.expect_plan_sha256:parser.error('--execute requires --expect-plan-sha256')
        if args.tool_mode is not None:parser.error('Execution uses only the frozen plan mode; omit --tool-mode')
        if args.context_mode is not None or args.context_capacity is not None:parser.error('Execution uses the frozen generated context and capacity evidence')
        if args.only_alias is not None:parser.error('Execution uses only the frozen alias selection')
        execute(args.output,args.expect_plan_sha256,args.workers)
    else:
        plan=prepare(args.output,args.tool_mode or DEFAULT_MODE,args.only_alias,args.context_mode or 'k0',args.context_capacity)
        print(json.dumps({'prepared':True,'models':len(plan['entries']),'maximumRequests':plan['maximumRequests'],
                          'payloadMode':plan['executionContract']['payloadMode'],
                          'qualityVersion':plan['executionContract']['quality']['version'],
                          'planSha256':sha((args.output/'plan.json').read_bytes()),'output':str(args.output)}))


if __name__=='__main__':main()
