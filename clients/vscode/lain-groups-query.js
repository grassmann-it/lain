// Local Lain adapter. Native model retrieval and native selection stay upstream.
export async function loadLainModels({request, groups, hostId, modelProvider, modelCatalogPath, limit, signal, onStale}) {
  const check = active => { if (active?.aborted) throw new DOMException('Aborted', 'AbortError'); };
  check(signal);
  const first = await request({includeHidden: true, cursor: null, limit});
  check(signal);
  if (hostId !== 'local' || modelProvider !== 'lain') return first;
  const data = [...first.data], seen = new Set();
  let cursor = first.nextCursor;
  while (cursor != null) {
    check(signal);
    if (seen.has(cursor) || seen.size >= 100 || data.length >= 10000) throw new Error('Lain: invalid model pagination');
    seen.add(cursor);
    const page = await request({includeHidden: true, cursor, limit});
    check(signal);
    data.push(...page.data);
    if (data.length > 10000) throw new Error('Lain: model pagination limit exceeded');
    cursor = page.nextCursor;
  }
  const params = {hostId, modelProvider, modelCatalogPath, models: data};
  let invalidated = false;
  const verify = async (abortSignal, refresh = true) => {
    try {
      check(abortSignal);
      const result = await groups(params, abortSignal);
      check(abortSignal);
      if (refresh && !invalidated && (result.reason === 'models-changed' || result.reason === 'context-changed')) {
        invalidated = true;
        onStale?.();
      }
      return result;
    } catch (error) {
      if (abortSignal?.aborted) throw error;
      return {status: 'native', reason: 'unavailable'};
    }
  };
  const metadata = {...await verify(signal, false), verify};
  return {...first, nextCursor: null, data: data.map(model => ({...model, _lainGroups: metadata}))};
}
